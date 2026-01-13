using ActiproRoslynPOC.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ActiproRoslynPOC.Services
{
    /// <summary>
    /// 改进版调试器服务 V3
    /// 核心改进:
    /// 1. 使用 TaskCompletionSource 替代 AutoResetEvent
    /// 2. 记录行号映射关系
    /// 3. 更好的线程模型
    /// </summary>
    public class DebuggerServiceV3
    {
        private HashSet<int> _breakpoints = new HashSet<int>();
        private Dictionary<int, int> _lineMapping = new Dictionary<int, int>(); // 插桩后行号 -> 原始行号
        private int _currentLine = -1;
        private bool _isDebugging = false;
        private DebugMode _debugMode = DebugMode.StepOver;
        private Assembly _debugAssembly;
        private object _workflowInstance;
        private SynchronizationContext _uiContext;
        private string _mainFileName; // 记录主文件名，用于查找对应的类型

        // 使用 TaskCompletionSource 替代 AutoResetEvent
        private TaskCompletionSource<bool> _pauseSignal;

        // 事件
        public event Action<int> CurrentLineChanged;
        public event Action<int> BreakpointHit;
        public event Action DebugSessionEnded;
        public event Action<Dictionary<string, object>> VariablesUpdated;
        public event Action<string> OutputMessage; // 输出消息事件

        public bool IsDebugging => _isDebugging;
        public int CurrentLine => _currentLine;

        private enum DebugMode
        {
            StepOver,    // 单步执行
            Continue     // 继续到下一个断点
        }

        public void SetBreakpoints(IEnumerable<int> lineNumbers)
        {
            _breakpoints = new HashSet<int>(lineNumbers);
            System.Diagnostics.Debug.WriteLine($"[DebuggerV3] 设置 {_breakpoints.Count} 个断点: {string.Join(", ", _breakpoints)}");
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
        /// 开始调试会话（多文件支持）
        /// </summary>
        public async Task<bool> StartDebuggingAsync(
            Dictionary<string, string> codeFiles,
            RoslynCompilerService compiler,
            string mainFilePath)
        {
            if (_isDebugging)
            {
                System.Diagnostics.Debug.WriteLine("[DebuggerV3] 已在调试中");
                return false;
            }

            try
            {
                _isDebugging = true;
                _debugMode = DebugMode.Continue;  // 初始模式：运行到断点，而不是单步
                _currentLine = -1;
                _lineMapping.Clear();
                _mainFileName = null; // 重置主文件名

                // 捕获UI线程的 SynchronizationContext
                _uiContext = SynchronizationContext.Current;

                // 输出主文件路径用于调试
                var mainFileInfo = $"[调试] 主文件路径: {mainFilePath ?? "null"}";
                System.Diagnostics.Debug.WriteLine(mainFileInfo);
                if (_uiContext != null)
                    _uiContext.Post(_ => OutputMessage?.Invoke(mainFileInfo), null);

                // 对需要调试的文件进行插桩
                var instrumentedFiles = new Dictionary<string, string>();

                foreach (var kvp in codeFiles)
                {
                    var fileName = kvp.Key;
                    var code = kvp.Value;

                    // 判断是否是主文件（需要插桩的文件）
                    bool isMainFile = string.IsNullOrEmpty(mainFilePath) ||
                                     fileName.Equals(Path.GetFileName(mainFilePath), StringComparison.OrdinalIgnoreCase) ||
                                     codeFiles.Count == 1;

                    if (isMainFile)
                    {
                        // 记录主文件名（用于后续查找对应的类型）
                        _mainFileName = Path.GetFileNameWithoutExtension(fileName);

                        // 对主文件插桩，并记录行号映射（传递文件名以保留行号信息）
                        var (instrumentedCode, lineMapping) = InstrumentCodeWithMapping(code, fileName);
                        _lineMapping = lineMapping;

                        var msg = $"[调试] ✓ 主文件已插桩: {fileName}";
                        System.Diagnostics.Debug.WriteLine(msg);
                        if (_uiContext != null)
                            _uiContext.Post(_ => OutputMessage?.Invoke(msg), null);

                        // 输出插桩后的代码（用于调试）
                        System.Diagnostics.Debug.WriteLine($"[调试] 插桩后的代码:\n{instrumentedCode}");

                        instrumentedFiles[fileName] = instrumentedCode;
                    }
                    else
                    {
                        // 其他文件保持原样
                        var msg = $"[调试] • 依赖文件(未插桩): {fileName}";
                        System.Diagnostics.Debug.WriteLine(msg);
                        if (_uiContext != null)
                            _uiContext.Post(_ => OutputMessage?.Invoke(msg), null);

                        instrumentedFiles[fileName] = code;
                    }
                }

                // 编译所有文件
                var result = compiler.CompileMultiple(instrumentedFiles);
                if (!result.Success)
                {
                    System.Diagnostics.Debug.WriteLine($"[DebuggerV3] 编译失败: {result.ErrorSummary}");

                    // 输出详细的编译错误信息
                    if (_uiContext != null)
                    {
                        _uiContext.Post(_ =>
                        {
                            OutputMessage?.Invoke($"[编译错误] {result.ErrorSummary}");
                            foreach (var diag in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
                            {
                                OutputMessage?.Invoke($"  第 {diag.Line} 行: {diag.Message}");
                            }
                        }, null);
                    }

                    _isDebugging = false;
                    return false;
                }

                _debugAssembly = result.Assembly;

                // 在后台线程执行工作流
                _ = Task.Run(() => ExecuteWorkflowAsync());

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DebuggerV3] 启动调试失败: {ex.Message}");
                _isDebugging = false;
                return false;
            }
        }

        /// <summary>
        /// 执行工作流（在后台线程中，但使用 async/await）
        /// </summary>
        private async Task ExecuteWorkflowAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[DebuggerV3] 开始执行工作流");

                // 查找工作流类型：优先查找与主文件名匹配的类
                var allTypes = _debugAssembly.GetTypes();
                var candidateTypes = allTypes
                    .Where(t => t.IsSubclassOf(typeof(CodedWorkflowBase)) &&
                               !t.IsAbstract &&
                               t.GetField("__debugCallback", BindingFlags.Public | BindingFlags.Static) != null)
                    .ToList();

                var msg1 = $"[调试] 找到 {candidateTypes.Count} 个被插桩的工作流类: {string.Join(", ", candidateTypes.Select(t => t.Name))}";
                System.Diagnostics.Debug.WriteLine(msg1);
                if (_uiContext != null)
                    _uiContext.Post(_ => OutputMessage?.Invoke(msg1), null);

                Type workflowType = null;

                // 优先查找与主文件名匹配的类型
                if (!string.IsNullOrEmpty(_mainFileName))
                {
                    workflowType = candidateTypes.FirstOrDefault(t =>
                        t.Name.Equals(_mainFileName, StringComparison.OrdinalIgnoreCase));

                    if (workflowType != null)
                    {
                        var msg2 = $"[调试] ✓ 执行类: {workflowType.Name} (匹配文件名)";
                        System.Diagnostics.Debug.WriteLine(msg2);
                        if (_uiContext != null)
                            _uiContext.Post(_ => OutputMessage?.Invoke(msg2), null);
                    }
                }

                // 如果没有找到匹配的，使用第一个候选类型
                if (workflowType == null)
                {
                    workflowType = candidateTypes.FirstOrDefault();
                    if (workflowType != null)
                    {
                        var msg3 = $"[调试] ⚠ 执行类: {workflowType.Name} (未匹配文件名，使用第一个)";
                        System.Diagnostics.Debug.WriteLine(msg3);
                        if (_uiContext != null)
                            _uiContext.Post(_ => OutputMessage?.Invoke(msg3), null);
                    }
                }

                if (workflowType == null)
                {
                    System.Diagnostics.Debug.WriteLine("[DebuggerV3] 找不到工作流类");
                    StopDebugging();
                    return;
                }

                // 创建实例
                _workflowInstance = Activator.CreateInstance(workflowType);

                // 设置调试回调委托
                var callbackField = workflowType.GetField("__debugCallback", BindingFlags.Public | BindingFlags.Static);
                if (callbackField != null)
                {
                    // 使用同步回调，内部使用 TaskCompletionSource 等待
                    Action<int> callback = OnLineExecuting;
                    callbackField.SetValue(null, callback);
                    System.Diagnostics.Debug.WriteLine("[DebuggerV3] 回调委托已设置");
                }

                // 执行 Execute 方法
                var executeMethod = workflowType.GetMethod("Execute");
                if (executeMethod != null)
                {
                    System.Diagnostics.Debug.WriteLine("[DebuggerV3] 开始调用 Execute 方法");
                    executeMethod.Invoke(_workflowInstance, null);
                    System.Diagnostics.Debug.WriteLine("[DebuggerV3] Execute 方法执行完成");
                }

                // 执行完成
                StopDebugging();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DebuggerV3] 执行异常: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[DebuggerV3] 堆栈跟踪: {ex.StackTrace}");
                StopDebugging();
            }
        }

        /// <summary>
        /// 停止调试
        /// </summary>
        public void StopDebugging()
        {
            if (!_isDebugging) return;

            _isDebugging = false;
            _currentLine = -1;

            // 释放任何等待的暂停信号
            _pauseSignal?.TrySetResult(true);

            // 在 UI 线程上触发事件
            if (_uiContext != null)
            {
                _uiContext.Post(_ => DebugSessionEnded?.Invoke(), null);
            }
            else
            {
                DebugSessionEnded?.Invoke();
            }

            System.Diagnostics.Debug.WriteLine("[DebuggerV3] 调试已停止");
        }

        /// <summary>
        /// 单步执行
        /// </summary>
        public Task StepOverAsync()
        {
            if (!_isDebugging) return Task.CompletedTask;

            _debugMode = DebugMode.StepOver;
            _pauseSignal?.TrySetResult(true); // 释放暂停
            return Task.CompletedTask;
        }

        /// <summary>
        /// 继续执行
        /// </summary>
        public Task ContinueAsync()
        {
            if (!_isDebugging) return Task.CompletedTask;

            _debugMode = DebugMode.Continue;
            _pauseSignal?.TrySetResult(true); // 释放暂停
            return Task.CompletedTask;
        }

        /// <summary>
        /// 调试回调 - 由插桩代码调用（同步版本，内部阻塞等待）
        /// </summary>
        /// <param name="lineNumber">原始行号（插桩代码中已经传递了原始行号）</param>
        private void OnLineExecuting(int lineNumber)
        {
            if (!_isDebugging) return;

            // 输出调试信息
            var debugMsg = $"[调试] 执行行: {lineNumber}";
            System.Diagnostics.Debug.WriteLine(debugMsg);
            if (_uiContext != null)
                _uiContext.Post(_ => OutputMessage?.Invoke(debugMsg), null);

            // 回调传递的就是原始行号，无需映射
            _currentLine = lineNumber;

            // 在 UI 线程上通知当前行变化
            if (_uiContext != null)
            {
                _uiContext.Post(_ => CurrentLineChanged?.Invoke(lineNumber), null);
            }
            else
            {
                CurrentLineChanged?.Invoke(lineNumber);
            }

            // 检查是否命中断点
            bool hitBreakpoint = _breakpoints.Contains(lineNumber);
            if (hitBreakpoint)
            {
                if (_uiContext != null)
                {
                    _uiContext.Post(_ => BreakpointHit?.Invoke(lineNumber), null);
                }
                else
                {
                    BreakpointHit?.Invoke(lineNumber);
                }
            }

            // 决定是否暂停
            bool shouldPause = false;
            switch (_debugMode)
            {
                case DebugMode.StepOver:
                    shouldPause = true; // 每行都暂停
                    break;
                case DebugMode.Continue:
                    shouldPause = hitBreakpoint; // 只在断点处暂停
                    break;
            }

            if (shouldPause)
            {
                UpdateVariables(); // 更新变量信息

                // 使用 TaskCompletionSource 暂停
                _pauseSignal = new TaskCompletionSource<bool>();
                _pauseSignal.Task.Wait(); // 同步等待用户操作（单步或继续）
            }

            // 短暂延迟，让 UI 有时间响应
            Thread.Sleep(50);
        }

        /// <summary>
        /// 更新变量信息
        /// </summary>
        private void UpdateVariables()
        {
            if (_workflowInstance == null) return;

            try
            {
                var variables = new Dictionary<string, object>();
                var type = _workflowInstance.GetType();

                // 获取所有字段
                var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var field in fields)
                {
                    if (field.Name.StartsWith("__")) continue; // 跳过编译器生成的字段
                    try
                    {
                        var value = field.GetValue(_workflowInstance);
                        variables[field.Name] = value ?? "<null>";
                    }
                    catch { }
                }

                // 获取所有属性
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

                // 在 UI 线程上触发事件
                if (_uiContext != null)
                {
                    _uiContext.Post(_ => VariablesUpdated?.Invoke(variables), null);
                }
                else
                {
                    VariablesUpdated?.Invoke(variables);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DebuggerV3] 更新变量失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 在代码中插入调试回调，并记录行号映射
        /// </summary>
        private (string instrumentedCode, Dictionary<int, int> lineMapping) InstrumentCodeWithMapping(string code, string fileName = null)
        {
            // 使用文件名解析，保留行号信息
            var tree = string.IsNullOrEmpty(fileName)
                ? CSharpSyntaxTree.ParseText(code)
                : CSharpSyntaxTree.ParseText(code, path: fileName, encoding: System.Text.Encoding.UTF8);

            var root = tree.GetCompilationUnitRoot();

            // 使用重写器插入调试回调
            var rewriter = new DebugInstrumentationRewriter();
            var newRoot = rewriter.Visit(root);

            // 获取行号映射
            var lineMapping = rewriter.GetLineMapping();

            // 添加必要的 using 语句
            var instrumentedCode = newRoot.ToFullString();

            if (!instrumentedCode.Contains("using System;"))
            {
                instrumentedCode = "using System;\r\n" + instrumentedCode;
            }

            return (instrumentedCode, lineMapping);
        }

        /// <summary>
        /// 语法重写器 - 在每个语句前插入调试回调
        /// </summary>
        private class DebugInstrumentationRewriter : CSharpSyntaxRewriter
        {
            private Dictionary<int, int> _lineMapping = new Dictionary<int, int>(); // 插桩后行号 -> 原始行号
            private int _lineOffset = 0; // 行号偏移（如果代码不是从第1行开始）

            public Dictionary<int, int> GetLineMapping() => _lineMapping;

            public void SetLineOffset(int offset)
            {
                _lineOffset = offset;
            }

            public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
            {
                // 添加静态调试回调委托字段（使用 Action<int>）
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
                // 只处理 Execute 方法
                if (node.Identifier.Text != "Execute")
                    return base.VisitMethodDeclaration(node);

                if (node.Body == null)
                    return base.VisitMethodDeclaration(node);

                // 访问方法体中的语句
                var newBody = (BlockSyntax)base.Visit(node.Body);
                return node.WithBody(newBody);
            }

            public override SyntaxNode VisitBlock(BlockSyntax node)
            {
                var newStatements = new List<StatementSyntax>();
                int insertedLines = 0; // 跟踪插入了多少行

                foreach (var statement in node.Statements)
                {
                    // 跳过空语句
                    if (statement is EmptyStatementSyntax)
                    {
                        newStatements.Add(statement);
                        continue;
                    }

                    // 获取语句在源代码中的真实行号
                    // 使用 SourceText 的 Lines 集合来获取基于源文本的行号
                    var location = statement.GetLocation();
                    var sourceText = location.SourceTree.GetText();
                    var linePosition = sourceText.Lines.GetLinePosition(location.SourceSpan.Start);
                    int lineNumber = 6 + linePosition.Line + 1;

                    // 调试输出：显示语句位置和内容
                    var stmtText = statement.ToString().Trim();
                    if (stmtText.Length > 50) stmtText = stmtText.Substring(0, 50) + "...";
                    System.Diagnostics.Debug.WriteLine($"[插桩] Line={lineNumber}, Span={location.SourceSpan.Start}-{location.SourceSpan.End}, Text={stmtText}");

                    // 插入调试回调
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
