using ActiproRoslynPOC.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ActiproRoslynPOC.Services
{
    /// <summary>
    /// PDB 增强版调试器服务
    /// 核心改进: 使用 PDB 信息智能插桩,只在真正可执行的行插入回调
    /// </summary>
    public class DebuggerServiceV3Enhanced
    {
        private HashSet<int> _breakpoints = new HashSet<int>();
        private Dictionary<int, int> _lineMapping = new Dictionary<int, int>();
        private int _currentLine = -1;
        private bool _isDebugging = false;
        private DebugMode _debugMode = DebugMode.StepOver;
        private Assembly _debugAssembly;
        private object _workflowInstance;
        private SynchronizationContext _uiContext;
        private string _mainFileName;
        private TaskCompletionSource<bool> _pauseSignal;

        // 事件
        public event Action<int> CurrentLineChanged;
        public event Action<int> BreakpointHit;
        public event Action DebugSessionEnded;
        public event Action<Dictionary<string, object>> VariablesUpdated;
        public event Action<string> OutputMessage;

        public bool IsDebugging => _isDebugging;
        public int CurrentLine => _currentLine;

        private enum DebugMode
        {
            StepOver,
            Continue
        }

        public void SetBreakpoints(IEnumerable<int> lineNumbers)
        {
            _breakpoints = new HashSet<int>(lineNumbers);
            System.Diagnostics.Debug.WriteLine($"[DebuggerEnhanced] 设置 {_breakpoints.Count} 个断点: {string.Join(", ", _breakpoints)}");
        }

        /// <summary>
        /// 添加单个断点（调试过程中动态添加）
        /// </summary>
        public void AddBreakpoint(int lineNumber)
        {
            if (_breakpoints.Add(lineNumber))
            {
                System.Diagnostics.Debug.WriteLine($"[DebuggerEnhanced] 添加断点: 第 {lineNumber} 行");
                OutputMessage?.Invoke($"● 添加断点: 第 {lineNumber} 行");
            }
        }

        /// <summary>
        /// 移除单个断点（调试过程中动态移除）
        /// </summary>
        public void RemoveBreakpoint(int lineNumber)
        {
            if (_breakpoints.Remove(lineNumber))
            {
                System.Diagnostics.Debug.WriteLine($"[DebuggerEnhanced] 移除断点: 第 {lineNumber} 行");
                OutputMessage?.Invoke($"○ 移除断点: 第 {lineNumber} 行");
            }
        }

        /// <summary>
        /// 切换断点状态（调试过程中动态切换）
        /// </summary>
        public void ToggleBreakpoint(int lineNumber)
        {
            if (_breakpoints.Contains(lineNumber))
                RemoveBreakpoint(lineNumber);
            else
                AddBreakpoint(lineNumber);
        }

        /// <summary>
        /// 获取当前所有断点
        /// </summary>
        public IEnumerable<int> GetBreakpoints()
        {
            return _breakpoints.ToList();
        }

        /// <summary>
        /// 开始调试会话（单文件）
        /// </summary>
        public async Task<bool> StartDebuggingAsync(string code, RoslynCompilerService compiler)
        {
            var codeFiles = new Dictionary<string, string> { { "main.cs", code } };
            return await StartDebuggingAsync(codeFiles, compiler, null);
        }

        /// <summary>
        /// 开始调试会话（多文件支持，PDB 增强）
        /// </summary>
        public async Task<bool> StartDebuggingAsync(
            Dictionary<string, string> codeFiles,
            RoslynCompilerService compiler,
            string mainFilePath)
        {
            if (_isDebugging)
            {
                System.Diagnostics.Debug.WriteLine("[DebuggerEnhanced] 已在调试中");
                return false;
            }

            try
            {
                _isDebugging = true;
                _debugMode = DebugMode.Continue;
                _currentLine = -1;
                _lineMapping.Clear();
                _mainFileName = null;
                _uiContext = SynchronizationContext.Current;

                var msg1 = $"[PDB增强] 主文件路径: {mainFilePath ?? "null"}";
                System.Diagnostics.Debug.WriteLine(msg1);
                OutputMessage?.Invoke(msg1);

                // ============================================
                // 步骤 1: 先编译一次生成 PDB
                // ============================================
                var tempResult = compiler.CompileMultiple(codeFiles);
                if (!tempResult.Success)
                {
                    System.Diagnostics.Debug.WriteLine($"[DebuggerEnhanced] 初始编译失败: {tempResult.ErrorSummary}");
                    OutputMessage?.Invoke($"[编译错误] {tempResult.ErrorSummary}");
                    _isDebugging = false;
                    return false;
                }

                // ============================================
                // 步骤 2: 读取 PDB 获取可执行行信息
                // ============================================
                var pdbReader = new PdbReaderService();
                if (!pdbReader.LoadFromBytes(tempResult.PdbBytes))
                {
                    System.Diagnostics.Debug.WriteLine("[DebuggerEnhanced] PDB 加载失败,回退到普通插桩");
                    OutputMessage?.Invoke("[警告] PDB 加载失败,使用普通插桩模式");
                    return await FallbackToNormalInstrumentation(codeFiles, compiler, mainFilePath);
                }

                var executableLines = pdbReader.GetAllExecutableLines();
                var msg2 = $"[PDB增强] ✓ 已识别 {executableLines.Count} 个可执行行: {string.Join(", ", executableLines.Take(10))}{(executableLines.Count > 10 ? "..." : "")}";
                System.Diagnostics.Debug.WriteLine(msg2);
                OutputMessage?.Invoke(msg2);

                // ============================================
                // 步骤 3: 智能插桩 - 只在可执行行插入回调
                // ============================================
                var instrumentedFiles = new Dictionary<string, string>();

                foreach (var kvp in codeFiles)
                {
                    var fileName = kvp.Key;
                    var code = kvp.Value;

                    bool isMainFile = string.IsNullOrEmpty(mainFilePath) ||
                                     fileName.Equals(System.IO.Path.GetFileName(mainFilePath), StringComparison.OrdinalIgnoreCase) ||
                                     codeFiles.Count == 1;

                    if (isMainFile)
                    {
                        _mainFileName = System.IO.Path.GetFileNameWithoutExtension(fileName);

                        // 使用智能插桩 - 只在 PDB 标记的可执行行插入回调
                        var instrumentedCode = InstrumentOnlyExecutableLines(code, executableLines, fileName);

                        var msg3 = $"[PDB增强] ✓ 主文件已智能插桩: {fileName}";
                        System.Diagnostics.Debug.WriteLine(msg3);
                        OutputMessage?.Invoke(msg3);

                        instrumentedFiles[fileName] = instrumentedCode;
                    }
                    else
                    {
                        var msg4 = $"[PDB增强] • 依赖文件(未插桩): {fileName}";
                        System.Diagnostics.Debug.WriteLine(msg4);
                        OutputMessage?.Invoke(msg4);
                        instrumentedFiles[fileName] = code;
                    }
                }

                // ============================================
                // 步骤 4: 重新编译插桩后的代码
                // ============================================
                var finalResult = compiler.CompileMultiple(instrumentedFiles);
                if (!finalResult.Success)
                {
                    System.Diagnostics.Debug.WriteLine($"[DebuggerEnhanced] 插桩后编译失败: {finalResult.ErrorSummary}");
                    OutputMessage?.Invoke($"[编译错误] {finalResult.ErrorSummary}");
                    _isDebugging = false;
                    return false;
                }

                _debugAssembly = finalResult.Assembly;

                // ============================================
                // 步骤 5: 在后台线程执行工作流
                // ============================================
                _ = Task.Run(() => ExecuteWorkflowAsync());

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DebuggerEnhanced] 启动调试失败: {ex.Message}");
                OutputMessage?.Invoke($"[错误] {ex.Message}");
                _isDebugging = false;
                return false;
            }
        }

        /// <summary>
        /// 回退到普通插桩模式（如果 PDB 加载失败）
        /// </summary>
        private async Task<bool> FallbackToNormalInstrumentation(
            Dictionary<string, string> codeFiles,
            RoslynCompilerService compiler,
            string mainFilePath)
        {
            // 使用旧版调试器的逻辑
            var instrumentedFiles = new Dictionary<string, string>();

            foreach (var kvp in codeFiles)
            {
                var fileName = kvp.Key;
                var code = kvp.Value;

                bool isMainFile = string.IsNullOrEmpty(mainFilePath) ||
                                 fileName.Equals(System.IO.Path.GetFileName(mainFilePath), StringComparison.OrdinalIgnoreCase) ||
                                 codeFiles.Count == 1;

                if (isMainFile)
                {
                    _mainFileName = System.IO.Path.GetFileNameWithoutExtension(fileName);
                    var (instrumentedCode, lineMapping) = InstrumentCodeWithMapping(code, fileName);
                    _lineMapping = lineMapping;
                    instrumentedFiles[fileName] = instrumentedCode;
                }
                else
                {
                    instrumentedFiles[fileName] = code;
                }
            }

            var result = compiler.CompileMultiple(instrumentedFiles);
            if (!result.Success)
            {
                _isDebugging = false;
                return false;
            }

            _debugAssembly = result.Assembly;
            _ = Task.Run(() => ExecuteWorkflowAsync());

            return true;
        }

        /// <summary>
        /// 智能插桩 - 只在 PDB 标记的可执行行插入回调
        /// </summary>
        private string InstrumentOnlyExecutableLines(string code, List<int> executableLines, string fileName)
        {
            var tree = CSharpSyntaxTree.ParseText(code, path: fileName, encoding: System.Text.Encoding.UTF8);
            var root = tree.GetCompilationUnitRoot();

            // 使用智能重写器
            var rewriter = new SmartInstrumentationRewriter(executableLines);
            var newRoot = rewriter.Visit(root);

            var instrumentedCode = newRoot.ToFullString();

            // 添加必要的 using 语句
            if (!instrumentedCode.Contains("using System;"))
            {
                instrumentedCode = "using System;\r\n" + instrumentedCode;
            }

            return instrumentedCode;
        }

        /// <summary>
        /// 普通插桩（回退模式）
        /// </summary>
        private (string instrumentedCode, Dictionary<int, int> lineMapping) InstrumentCodeWithMapping(string code, string fileName = null)
        {
            var tree = string.IsNullOrEmpty(fileName)
                ? CSharpSyntaxTree.ParseText(code)
                : CSharpSyntaxTree.ParseText(code, path: fileName, encoding: System.Text.Encoding.UTF8);

            var root = tree.GetCompilationUnitRoot();
            var rewriter = new DebugInstrumentationRewriter();
            var newRoot = rewriter.Visit(root);
            var lineMapping = rewriter.GetLineMapping();
            var instrumentedCode = newRoot.ToFullString();

            if (!instrumentedCode.Contains("using System;"))
            {
                instrumentedCode = "using System;\r\n" + instrumentedCode;
            }

            return (instrumentedCode, lineMapping);
        }

        /// <summary>
        /// 执行工作流
        /// </summary>
        private async Task ExecuteWorkflowAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[DebuggerEnhanced] 开始执行工作流");

                // GlobalLogManager 订阅已移除 - MainViewModel 已全局订阅，避免重复输出

                var allTypes = _debugAssembly.GetTypes();
                var candidateTypes = allTypes
                    .Where(t => t.IsSubclassOf(typeof(CodedWorkflowBase)) &&
                               !t.IsAbstract &&
                               t.GetField("__debugCallback", BindingFlags.Public | BindingFlags.Static) != null)
                    .ToList();

                var msg1 = $"[PDB增强] 找到 {candidateTypes.Count} 个被插桩的工作流类: {string.Join(", ", candidateTypes.Select(t => t.Name))}";
                System.Diagnostics.Debug.WriteLine(msg1);
                OutputMessage?.Invoke(msg1);

                Type workflowType = null;

                if (!string.IsNullOrEmpty(_mainFileName))
                {
                    workflowType = candidateTypes.FirstOrDefault(t =>
                        t.Name.Equals(_mainFileName, StringComparison.OrdinalIgnoreCase));

                    if (workflowType != null)
                    {
                        var msg2 = $"[PDB增强] ✓ 执行类: {workflowType.Name}";
                        System.Diagnostics.Debug.WriteLine(msg2);
                        OutputMessage?.Invoke(msg2);
                    }
                }

                if (workflowType == null)
                {
                    workflowType = candidateTypes.FirstOrDefault();
                }

                if (workflowType == null)
                {
                    System.Diagnostics.Debug.WriteLine("[DebuggerEnhanced] 找不到工作流类");
                    StopDebugging();
                    return;
                }

                _workflowInstance = Activator.CreateInstance(workflowType);

                var callbackField = workflowType.GetField("__debugCallback", BindingFlags.Public | BindingFlags.Static);
                if (callbackField != null)
                {
                    Action<int> callback = OnLineExecuting;
                    callbackField.SetValue(null, callback);
                }

                var executeMethod = workflowType.GetMethod("Execute");
                if (executeMethod != null)
                {
                    executeMethod.Invoke(_workflowInstance, null);
                }

                StopDebugging();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DebuggerEnhanced] 执行异常: {ex.Message}");
                OutputMessage?.Invoke($"[异常] {ex.Message}");
                StopDebugging();
            }
            finally
            {
                // GlobalLogManager 订阅清理已移除 - 无需清理
            }
        }

        public void StopDebugging()
        {
            if (!_isDebugging) return;

            _isDebugging = false;
            _currentLine = -1;
            _pauseSignal?.TrySetResult(true);

            if (_uiContext != null)
                _uiContext.Post(_ => DebugSessionEnded?.Invoke(), null);
            else
                DebugSessionEnded?.Invoke();

            System.Diagnostics.Debug.WriteLine("[DebuggerEnhanced] 调试已停止");
        }

        public Task StepOverAsync()
        {
            if (!_isDebugging) return Task.CompletedTask;
            _debugMode = DebugMode.StepOver;
            _pauseSignal?.TrySetResult(true);
            return Task.CompletedTask;
        }

        public Task ContinueAsync()
        {
            if (!_isDebugging) return Task.CompletedTask;
            _debugMode = DebugMode.Continue;
            _pauseSignal?.TrySetResult(true);
            return Task.CompletedTask;
        }

        private void OnLineExecuting(int lineNumber)
        {
            if (!_isDebugging) return;

            _currentLine = lineNumber;

            bool hitBreakpoint = _breakpoints.Contains(lineNumber);

            bool shouldPause = false;
            switch (_debugMode)
            {
                case DebugMode.StepOver:
                    shouldPause = true;
                    break;
                case DebugMode.Continue:
                    shouldPause = hitBreakpoint;
                    break;
            }

            // 优化: 只在需要暂停时才更新 UI，避免频繁闪烁
            if (shouldPause)
            {
                // 通知 UI 当前行变化
                if (_uiContext != null)
                    _uiContext.Post(_ => CurrentLineChanged?.Invoke(lineNumber), null);
                else
                    CurrentLineChanged?.Invoke(lineNumber);

                // 如果命中断点，触发断点事件
                if (hitBreakpoint)
                {
                    if (_uiContext != null)
                        _uiContext.Post(_ => BreakpointHit?.Invoke(lineNumber), null);
                    else
                        BreakpointHit?.Invoke(lineNumber);
                }

                // 更新变量
                UpdateVariables();

                // 暂停执行
                _pauseSignal = new TaskCompletionSource<bool>();
                _pauseSignal.Task.Wait();
            }

            // 短暂延迟（仅在暂停模式下需要，让 UI 有时间响应）
            if (shouldPause)
            {
                Thread.Sleep(50);
            }
        }

        private void UpdateVariables()
        {
            if (_workflowInstance == null) return;

            try
            {
                var variables = new Dictionary<string, object>();
                var type = _workflowInstance.GetType();

                var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var field in fields)
                {
                    if (field.Name.StartsWith("__")) continue;
                    try
                    {
                        var value = field.GetValue(_workflowInstance);
                        variables[field.Name] = value ?? "<null>";
                    }
                    catch { }
                }

                var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
                foreach (var prop in properties)
                {
                    if (!prop.CanRead) continue;
                    try
                    {
                        var value = prop.GetValue(_workflowInstance);
                        variables[prop.Name] = value ?? "<null>";
                    }
                    catch { }
                }

                if (_uiContext != null)
                    _uiContext.Post(_ => VariablesUpdated?.Invoke(variables), null);
                else
                    VariablesUpdated?.Invoke(variables);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DebuggerEnhanced] 更新变量失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 智能插桩重写器 - 只在 PDB 标记的可执行行插入回调
        /// </summary>
        private class SmartInstrumentationRewriter : CSharpSyntaxRewriter
        {
            private HashSet<int> _executableLines;

            public SmartInstrumentationRewriter(List<int> executableLines)
            {
                _executableLines = new HashSet<int>(executableLines);
            }

            public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
            {
                // 添加静态调试回调委托字段
                var contextField = SyntaxFactory.FieldDeclaration(
                    SyntaxFactory.VariableDeclaration(
                        SyntaxFactory.IdentifierName("Action<int>"))
                    .WithVariables(SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(
                            SyntaxFactory.Identifier("__debugCallback")))))
                .WithModifiers(SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                    SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
                .NormalizeWhitespace();

                node = node.AddMembers(contextField);
                return base.VisitClassDeclaration(node);
            }

            public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
            {
                if (node.Identifier.Text != "Execute")
                    return base.VisitMethodDeclaration(node);

                if (node.Body == null)
                    return base.VisitMethodDeclaration(node);

                var newBody = (BlockSyntax)base.Visit(node.Body);
                return node.WithBody(newBody);
            }

            public override SyntaxNode VisitBlock(BlockSyntax node)
            {
                var newStatements = new List<StatementSyntax>();

                foreach (var statement in node.Statements)
                {
                    if (statement is EmptyStatementSyntax)
                    {
                        newStatements.Add(statement);
                        continue;
                    }

                    var location = statement.GetLocation();
                    var sourceText = location.SourceTree.GetText();
                    var linePosition = sourceText.Lines.GetLinePosition(location.SourceSpan.Start);
                    int lineNumber = 6 + linePosition.Line + 1;

                    // 关键: 只在 PDB 标记的可执行行插入回调
                    if (_executableLines.Contains(lineNumber))
                    {
                        var callbackStatement = SyntaxFactory.ParseStatement(
                            $"__debugCallback?.Invoke({lineNumber});\r\n");
                        newStatements.Add(callbackStatement);

                        System.Diagnostics.Debug.WriteLine($"[智能插桩] ✓ Line {lineNumber}: {statement.ToString().Trim().Substring(0, Math.Min(40, statement.ToString().Trim().Length))}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[智能插桩] ✗ Line {lineNumber}: 跳过 (非可执行行)");
                    }

                    // ✅ 递归访问语句，处理嵌套的块（如循环体、if体等）
                    var visitedStatement = (StatementSyntax)Visit(statement);
                    newStatements.Add(visitedStatement);
                }

                return node.WithStatements(SyntaxFactory.List(newStatements));
            }
        }

        /// <summary>
        /// 普通插桩重写器（回退模式）
        /// </summary>
        private class DebugInstrumentationRewriter : CSharpSyntaxRewriter
        {
            private Dictionary<int, int> _lineMapping = new Dictionary<int, int>();

            public Dictionary<int, int> GetLineMapping() => _lineMapping;

            public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
            {
                var contextField = SyntaxFactory.FieldDeclaration(
                    SyntaxFactory.VariableDeclaration(
                        SyntaxFactory.IdentifierName("Action<int>"))
                    .WithVariables(SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(
                            SyntaxFactory.Identifier("__debugCallback")))))
                .WithModifiers(SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                    SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
                .NormalizeWhitespace();

                node = node.AddMembers(contextField);
                return base.VisitClassDeclaration(node);
            }

            public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
            {
                if (node.Identifier.Text != "Execute")
                    return base.VisitMethodDeclaration(node);

                if (node.Body == null)
                    return base.VisitMethodDeclaration(node);

                var newBody = (BlockSyntax)base.Visit(node.Body);
                return node.WithBody(newBody);
            }

            public override SyntaxNode VisitBlock(BlockSyntax node)
            {
                var newStatements = new List<StatementSyntax>();

                foreach (var statement in node.Statements)
                {
                    if (statement is EmptyStatementSyntax)
                    {
                        newStatements.Add(statement);
                        continue;
                    }

                    var location = statement.GetLocation();
                    var sourceText = location.SourceTree.GetText();
                    var linePosition = sourceText.Lines.GetLinePosition(location.SourceSpan.Start);
                    int lineNumber = 6 + linePosition.Line + 1;

                    var callbackStatement = SyntaxFactory.ParseStatement(
                        $"__debugCallback?.Invoke({lineNumber});\r\n");

                    newStatements.Add(callbackStatement);
                    newStatements.Add(statement);
                }

                return node.WithStatements(SyntaxFactory.List(newStatements));
            }
        }
    }
}
