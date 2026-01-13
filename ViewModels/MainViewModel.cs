using ActiproRoslynPOC.Models;
using ActiproRoslynPOC.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace ActiproRoslynPOC.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private string _code;
        private string _output;
        private readonly RoslynCompilerService _compiler;
        private readonly CodeExecutionService _executor;
        private readonly DebuggerServiceV3 _debugger;

        // 文件追踪
        private string _currentFilePath;
        private bool _isModified;
        private bool _isLoadingFile;

        // 调试状态
        private bool _isDebugging;
        private int _currentDebugLine = -1;
        private string _variablesText;

        public MainViewModel()
        {

            _compiler = new RoslynCompilerService();
            _executor = new CodeExecutionService();
            _executor.LogEvent += (s, msg) => AppendOutput(msg);

            // 初始化调试服务 V3
            _debugger = new DebuggerServiceV3();
            _debugger.CurrentLineChanged += OnDebuggerCurrentLineChanged;
            _debugger.BreakpointHit += OnDebuggerBreakpointHit;
            _debugger.DebugSessionEnded += OnDebugSessionEnded;
            _debugger.VariablesUpdated += OnVariablesUpdated;
            _debugger.OutputMessage += (msg) => AppendOutput(msg);

            // --- 新增：重定向 Console 输出 ---
            // 将所有 Console.Write/WriteLine 转发给 AppendOutput 方法
            var consoleWriter = new ConsoleRedirectWriter(msg => AppendOutput(msg));
            Console.SetOut(consoleWriter);

            // 命令
            RunCommand = new RelayCommand(ExecuteRun);
            CheckSyntaxCommand = new RelayCommand(ExecuteCheckSyntax);
            ClearOutputCommand = new RelayCommand(() => Output = "");
            SaveCommand = new RelayCommand(ExecuteSave, CanExecuteSave);
            OpenFileCommand = new RelayCommand(ExecuteOpenFile);

            // 调试命令
            StartDebugCommand = new RelayCommand(async () => await ExecuteStartDebugAsync(), () => !IsDebugging);
            StopDebugCommand = new RelayCommand(ExecuteStopDebug, () => IsDebugging);
            StepOverCommand = new RelayCommand(async () => await ExecuteStepOverAsync(), () => IsDebugging);
            ContinueCommand = new RelayCommand(async () => await ExecuteContinueAsync(), () => IsDebugging);

            // 在构造函数中初始化
            NewFileCommand = new RelayCommand(ExecuteNewFile);
            Diagnostics = new ObservableCollection<DiagnosticInfo>();

            // 启动时加载默认文件
            LoadDefaultFile();
        }

        public string Code
        {
            get => _code;
            set
            {
                if (_code != value)
                {
                    _code = value;
                    OnPropertyChanged();

                    // 标记为已修改（仅当不是加载文件时）
                    if (!string.IsNullOrEmpty(CurrentFilePath) && !_isLoadingFile)
                    {
                        IsModified = true;
                    }
                }
            }
        }

        public string CurrentFilePath
        {
            get => _currentFilePath;
            set
            {
                if (_currentFilePath != value)
                {
                    _currentFilePath = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentFileName));
                    OnPropertyChanged(nameof(WindowTitle));
                }
            }
        }

        public string CurrentFileName =>
            string.IsNullOrEmpty(CurrentFilePath)
                ? "未命名"
                : Path.GetFileName(CurrentFilePath);

        public bool IsModified
        {
            get => _isModified;
            set
            {
                if (_isModified != value)
                {
                    _isModified = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(WindowTitle));
                    CommandManager.InvalidateRequerySuggested(); // 刷新命令状态
                }
            }
        }

        public string WindowTitle =>
            $"{(IsModified ? "*" : "")}{CurrentFileName} - Actipro Roslyn POC";

        public string Output
        {
            get => _output ?? string.Empty;
            set
            {
                // 绝对不要在这里写任何可能触发 Console.WriteLine 的逻辑
                if (_output == value) return;
                _output = value;
                OnPropertyChanged(); // 使用 CallerMemberName 不需要写 "Output"
            }
        }

        public ObservableCollection<DiagnosticInfo> Diagnostics { get; set; }

        public ICommand RunCommand { get; }
        public ICommand CheckSyntaxCommand { get; }
        public ICommand ClearOutputCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand OpenFileCommand { get; }

        // 调试命令
        public ICommand StartDebugCommand { get; }
        public ICommand StopDebugCommand { get; }
        public ICommand StepOverCommand { get; }
        public ICommand ContinueCommand { get; }

        // 调试状态属性
        public bool IsDebugging
        {
            get => _isDebugging;
            set
            {
                if (_isDebugging != value)
                {
                    _isDebugging = value;
                    OnPropertyChanged();
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public int CurrentDebugLine
        {
            get => _currentDebugLine;
            set
            {
                if (_currentDebugLine != value)
                {
                    _currentDebugLine = value;
                    OnPropertyChanged();
                }
            }
        }

        public string VariablesText
        {
            get => _variablesText;
            set
            {
                if (_variablesText != value)
                {
                    _variablesText = value;
                    OnPropertyChanged();
                }
            }
        }


        // 调试事件：通知 UI 更新当前语句指示器
        public event Action<int> DebugLineChanged;

        private void ExecuteRun()
        {
            Output = "";
            Diagnostics.Clear();

            // 检查是否有项目目录（可从配置或用户设置获取）
            string projectDirectory = GetProjectDirectory();

            // 判断是否需要包含其他文件
            bool hasOtherCsFiles = !string.IsNullOrEmpty(projectDirectory) &&
                                  Directory.Exists(projectDirectory) &&
                                  Directory.GetFiles(projectDirectory, "*.cs").Length > 1;

            if (hasOtherCsFiles)
            {
                // 多文件模式
                ExecuteWithDependencies(projectDirectory);
            }
            else
            {
                // 单文件模式（原有逻辑）
                ExecuteSingleFile();
            }
        }

        private void ExecuteSingleFile()
        {
            Output = "";
            Diagnostics.Clear();
            var sw = Stopwatch.StartNew();

            try
            {
                AppendOutput("开始编译...");
                var result = _compiler.Compile(Code);

                if (!result.Success)
                {
                    AppendOutput($"编译失败: {result.ErrorSummary}");
                    foreach (var diag in result.Diagnostics)
                        Diagnostics.Add(diag);
                    return;
                }

                AppendOutput($"编译成功 ({sw.ElapsedMilliseconds}ms)");

                var type = result.Assembly.GetTypes()
                    .FirstOrDefault(t => t.IsSubclassOf(typeof(CodedWorkflowBase)) && !t.IsAbstract);

                if (type == null)
                {
                    AppendOutput("[错误] 找不到 CodedWorkflowBase 的子类");
                    return;
                }

                var workflow = _compiler.CreateInstance<CodedWorkflowBase>(result.Assembly, type.Name);
                workflow.LogEvent += (s, msg) => AppendOutput(msg);

                AppendOutput("开始执行...");
                workflow.Execute();

                sw.Stop();
                AppendOutput($"执行完成，耗时 {sw.ElapsedMilliseconds}ms");
                if (workflow.Result != null)
                    AppendOutput($"返回结果: {workflow.Result}");
            }
            catch (Exception ex)
            {
                AppendOutput($"[异常] {ex.Message}");
            }
        }

        private string GetProjectDirectory()
        {
            // 方法 1: 从配置文件读取
            // return ConfigurationManager.AppSettings["ProjectDirectory"];

            // 方法 2: 固定目录（测试用）
            return @"E:\ai_app\actipro_rpa\TestWorkflows";

            // 方法 3: 当前文件所在目录
            // return Path.GetDirectoryName(CurrentFilePath);
        }

        private void ExecuteWithDependencies(string projectDirectory)
        {
            AppendOutput($"项目模式：编译 {projectDirectory} 中的所有 .cs 文件");

            try
            {
                // 先保存当前编辑器中的代码
                if (IsModified && !string.IsNullOrEmpty(CurrentFilePath))
                {
                    AppendOutput("检测到未保存的修改，自动保存中...");
                    ExecuteSave();
                }

                // 获取所有 .cs 文件
                var codeFiles = new Dictionary<string, string>();
                var csFiles = Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories);

                foreach (var filePath in csFiles)
                {
                    var fileName = Path.GetFileName(filePath);
                    var code = File.ReadAllText(filePath);
                    codeFiles[fileName] = code;
                    AppendOutput($"  - {fileName}");
                }

                // 编译
                var compileResult = _compiler.CompileMultiple(codeFiles);

                if (!compileResult.Success)
                {
                    AppendOutput($"编译失败: {compileResult.ErrorSummary}");
                    foreach (var diag in compileResult.Diagnostics)
                        Diagnostics.Add(diag);
                    return;
                }

                AppendOutput("编译成功！");

                // 根据当前文件查找类型
                var targetTypeName = GetTypeNameFromFile(CurrentFilePath);
                var workflowType = FindWorkflowType(compileResult.Assembly, targetTypeName);

                if (workflowType == null)
                {
                    AppendOutput($"[错误] 在 {CurrentFileName} 中找不到 CodedWorkflowBase 的子类");
                    return;
                }

                AppendOutput($"执行类型: {workflowType.Name}");

                var workflow = Activator.CreateInstance(workflowType) as CodedWorkflowBase;
                workflow.LogEvent += (s, msg) => AppendOutput(msg);
                workflow.Execute();

                AppendOutput("执行完成！");
                if (workflow.Result != null)
                    AppendOutput($"返回结果: {workflow.Result}");
            }
            catch (Exception ex)
            {
                AppendOutput($"[异常] {ex.Message}");
            }
        }

        /// <summary>
        /// 从文件路径推断类型名称
        /// </summary>
        private string GetTypeNameFromFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return null;

            // MainWorkflow.cs -> MainWorkflow
            return Path.GetFileNameWithoutExtension(filePath);
        }

        /// <summary>
        /// 查找工作流类型（优先匹配指定名称）
        /// </summary>
        private Type FindWorkflowType(System.Reflection.Assembly assembly, string preferredTypeName)
        {
            var workflowTypes = assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(CodedWorkflowBase)) && !t.IsAbstract)
                .ToList();

            if (workflowTypes.Count == 0)
                return null;

            // 优先查找名称匹配的类型
            if (!string.IsNullOrEmpty(preferredTypeName))
            {
                var matchedType = workflowTypes.FirstOrDefault(
                    t => t.Name.Equals(preferredTypeName, StringComparison.OrdinalIgnoreCase)
                );
                if (matchedType != null)
                    return matchedType;
            }

            // 如果只有一个类型，返回它
            if (workflowTypes.Count == 1)
                return workflowTypes[0];

            // 多个类型时，返回第一个并警告
            AppendOutput($"[警告] 找到 {workflowTypes.Count} 个工作流类型，使用第一个: {workflowTypes[0].Name}");
            return workflowTypes[0];
        }

        private void ExecuteCheckSyntax()
        {
            Diagnostics.Clear();
            var diagnostics = _compiler.CheckSyntax(Code);

            foreach (var diag in diagnostics)
            {
                Diagnostics.Add(diag);
            }

            if (diagnostics.Count == 0)
            {
                AppendOutput("[✓] 语法检查通过");
            }
            else
            {
                AppendOutput($"[!] 发现 {diagnostics.Count} 个问题");
            }
        }

        private bool _isAppending = false; // 递归保护锁

        private void AppendOutput(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            // 1. 使用 BeginInvoke (异步) 代替 Invoke (同步)，切断当前的函数堆栈
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                // 2. 递归卫兵：防止控制台重定向导致的自我触发
                if (_isAppending) return;

                try
                {
                    _isAppending = true;

                    // 3. 性能优化：限制 Output 长度，防止内存溢出
                    if (_output?.Length > 50000)
                        _output = _output.Substring(20000);

                    // 4. 使用更加显式的赋值逻辑
                    string timestamp = DateTime.Now.ToString("HH:mm:ss");
                    Output += $"[{timestamp}] {message}{Environment.NewLine}";
                }
                finally
                {
                    _isAppending = false;
                }
            }));
        }
        // 在 MainViewModel 类中添加
        public ICommand NewFileCommand { get; }

        // 定义一个事件，通知 View 有新文件创建了（用于 Actipro 注册）
        public event Action<string> FileCreated;

        // 定义一个事件，通知 View 文件保存后刷新 IntelliSense
        public event Action<string> FileSaved;

        /// <summary>
        /// 触发 FileSaved 事件（供外部调用，刷新其他文档的 IntelliSense）
        /// </summary>
        public void TriggerFileSaved(string filePath)
        {
            FileSaved?.Invoke(filePath);
        }

        private void ExecuteNewFile()
        {
            try
            {
                string projectDirectory = GetProjectDirectory();
                // 生成一个不重复的文件名，例如 NewWorkflow1.cs
                string fileName = "NewWorkflow";
                string fullPath;
                int count = 1;
                do
                {
                    fullPath = Path.Combine(projectDirectory, $"{fileName}{count}.cs");
                    count++;
                } while (File.Exists(fullPath));

                // 1. 写入基础模板代码
                string template = $@"using System;
using ActiproRoslynPOC.Models;
using TestProject;

public class {Path.GetFileNameWithoutExtension(fullPath)} : CodedWorkflowBase
{{
    public override void Execute()
    {{
        Log(""新工作流已启动"");
    }}
}}";
                File.WriteAllText(fullPath, template);

                // 2. 通知 View (MainWindow) 注册到 Actipro 引擎
                FileCreated?.Invoke(fullPath);

                // 3. 刷新文件列表显示 (这里你可以简单通过重新读取目录或触发通知)
                // 假设你在 MainWindow 监听了此事件并刷新了 ListBox
                AppendOutput($"[新建] 文件已创建并集成: {Path.GetFileName(fullPath)}");

                // 4. 自动加载这个新文件到编辑器
                LoadFile(fullPath);
            }
            catch (Exception ex)
            {
                AppendOutput($"[错误] 新建文件失败: {ex.Message}");
            }
        }
        private string GetDefaultTemplate()
        {
            return @"using System;
using ActiproRoslynPOC.Models;

public class SampleWorkflow : CodedWorkflowBase
{
    [Workflow(Name = ""示例工作流"")]
    public override void Execute()
    {
        Log(""Hello from Coded Workflow!"");

        // 您的业务逻辑
        int sum = 1 + 2 + 3;
        Log($""计算结果: {sum}"");

        Result = sum;
    }
}";
        }

        #region 文件操作

        /// <summary>
        /// 启动时加载默认文件
        /// </summary>
        private void LoadDefaultFile()
        {
            var projectDirectory = GetProjectDirectory();
            var defaultFilePath = Path.Combine(projectDirectory, "MainWorkflow.cs");

            if (File.Exists(defaultFilePath))
            {
                LoadFile(defaultFilePath);
            }
            else
            {
                // 如果默认文件不存在，使用模板
                Code = GetDefaultTemplate();
                CurrentFilePath = defaultFilePath;
                IsModified = true;
                AppendOutput("[提示] 默认文件不存在，已加载模板代码");
            }
        }

        /// <summary>
        /// 加载指定文件到编辑器
        /// </summary>
        public void LoadFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    AppendOutput($"[错误] 文件不存在: {filePath}");
                    return;
                }

                _isLoadingFile = true;
                var code = File.ReadAllText(filePath);
                Code = code;
                CurrentFilePath = filePath;
                IsModified = false;
                _isLoadingFile = false;

                AppendOutput($"已加载文件: {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                _isLoadingFile = false;
                AppendOutput($"[错误] 加载文件失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 保存命令是否可执行
        /// </summary>
        private bool CanExecuteSave()
        {
            return IsModified && !string.IsNullOrEmpty(CurrentFilePath);
        }

        /// <summary>
        /// 保存文件
        /// </summary>
        private void ExecuteSave()
        {
            try
            {
                // 确保目录存在
                var directory = Path.GetDirectoryName(CurrentFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // 保存文件
                File.WriteAllText(CurrentFilePath, Code);
                IsModified = false;

                AppendOutput($"✓ 文件已保存: {Path.GetFileName(CurrentFilePath)}");

                // 通知 View 刷新 IntelliSense（关键步骤：让其他文件感知到新保存的改动）
                FileSaved?.Invoke(CurrentFilePath);
            }
            catch (Exception ex)
            {
                AppendOutput($"[错误] 保存文件失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 打开文件（循环切换项目中的.cs文件）
        /// </summary>
        private void ExecuteOpenFile()
        {
            try
            {
                var projectDirectory = GetProjectDirectory();
                var csFiles = Directory.GetFiles(projectDirectory, "*.cs", SearchOption.TopDirectoryOnly)
                    .OrderBy(f => f)
                    .ToArray();

                if (csFiles.Length == 0)
                {
                    AppendOutput("[提示] 项目目录中没有 .cs 文件");
                    return;
                }

                // 循环切换文件
                var currentIndex = Array.FindIndex(csFiles, f => f.Equals(CurrentFilePath, StringComparison.OrdinalIgnoreCase));
                var nextIndex = (currentIndex + 1) % csFiles.Length;
                LoadFile(csFiles[nextIndex]);
            }
            catch (Exception ex)
            {
                AppendOutput($"[错误] 打开文件失败: {ex.Message}");
            }
        }

        #endregion

        #region 调试功能 (DebuggerServiceV3)

        /// <summary>
        /// 开始调试
        /// </summary>
        private async Task ExecuteStartDebugAsync()
        {
            try
            {
                Output = "";
                AppendOutput("=== 开始调试 ===");

                // 从当前活动编辑器获取断点
                var breakpoints = GetBreakpointsFromUI?.Invoke() ?? new List<int>();
                _debugger.SetBreakpoints(breakpoints);

                AppendOutput($"设置了 {breakpoints.Count} 个断点");

                // 先保存当前编辑器中的代码（如果有修改）
                if (IsModified && !string.IsNullOrEmpty(CurrentFilePath))
                {
                    AppendOutput("检测到未保存的修改，自动保存中...");
                    ExecuteSave();
                }

                IsDebugging = true;  // 先设置状态，避免按钮不可点击

                bool success;
                string projectDirectory = GetProjectDirectory();

                // 检查是否有其他依赖文件（同目录下的其他 .cs 文件）
                bool hasOtherCsFiles = !string.IsNullOrEmpty(projectDirectory) &&
                                      Directory.Exists(projectDirectory) &&
                                      Directory.GetFiles(projectDirectory, "*.cs").Length > 1;

                AppendOutput($"调试文件: {CurrentFileName}");

                var codeFiles = new Dictionary<string, string>();

                if (hasOtherCsFiles)
                {
                    // 多文件模式：加载所有文件以满足依赖，但只调试当前文件
                    AppendOutput($"检测到项目目录中有其他文件，加载依赖文件...");

                    var csFiles = Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories);
                    foreach (var filePath in csFiles)
                    {
                        var fileName = Path.GetFileName(filePath);

                        // 当前文件使用编辑器中的代码（可能有未保存的修改）
                        if (fileName.Equals(CurrentFileName, StringComparison.OrdinalIgnoreCase))
                        {
                            codeFiles[fileName] = Code;
                            AppendOutput($"  [主] {fileName}");
                        }
                        else
                        {
                            // 其他文件从磁盘读取
                            var fileCode = File.ReadAllText(filePath);
                            codeFiles[fileName] = fileCode;
                            AppendOutput($"  [依赖] {fileName}");
                        }
                    }
                }
                else
                {
                    // 单文件模式：只有当前文件
                    codeFiles[CurrentFileName] = Code;
                }

                // 启动调试：明确指定当前文件为主调试对象
                // DebuggerServiceV3 会只对 CurrentFilePath 对应的文件进行插桩
                success = await _debugger.StartDebuggingAsync(codeFiles, _compiler, CurrentFilePath);

                if (!success)
                {
                    AppendOutput("[错误] 调试启动失败");
                    IsDebugging = false;
                }
            }
            catch (Exception ex)
            {
                AppendOutput($"[异常] {ex.Message}");
                IsDebugging = false;
            }
        }

        /// <summary>
        /// 停止调试
        /// </summary>
        private void ExecuteStopDebug()
        {
            _debugger.StopDebugging();
            IsDebugging = false;
            AppendOutput("=== 调试已停止 ===");
        }

        /// <summary>
        /// 单步执行
        /// </summary>
        private async Task ExecuteStepOverAsync()
        {
            await _debugger.StepOverAsync();
        }

        /// <summary>
        /// 继续执行
        /// </summary>
        private async Task ExecuteContinueAsync()
        {
            await _debugger.ContinueAsync();
        }

        /// <summary>
        /// 当前行变化事件处理
        /// </summary>
        private void OnDebuggerCurrentLineChanged(int line)
        {
            CurrentDebugLine = line;
            DebugLineChanged?.Invoke(line);
        }

        /// <summary>
        /// 断点命中事件处理
        /// </summary>
        private void OnDebuggerBreakpointHit(int line)
        {
            AppendOutput($"● 断点命中: 第 {line} 行");
        }

        /// <summary>
        /// 调试会话结束事件处理
        /// </summary>
        private void OnDebugSessionEnded()
        {
            IsDebugging = false;
            CurrentDebugLine = -1;
            DebugLineChanged?.Invoke(-1);
            AppendOutput("=== 调试完成 ===");
        }

        /// <summary>
        /// 变量更新事件处理
        /// </summary>
        private void OnVariablesUpdated(Dictionary<string, object> variables)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("变量:");
            foreach (var kvp in variables)
            {
                sb.AppendLine($"  {kvp.Key} = {kvp.Value}");
            }
            VariablesText = sb.ToString();
        }

        /// <summary>
        /// 从 UI 获取断点的委托（由 MainWindow 设置）
        /// </summary>
        public Func<List<int>> GetBreakpointsFromUI { get; set; }

        #endregion

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            // 检查是否有订阅者
            var handler = PropertyChanged;
            if (handler != null)
            {
                // 检查当前线程是否有权访问 UI
                if (Application.Current.Dispatcher.CheckAccess())
                {
                    handler(this, new PropertyChangedEventArgs(name));
                }
                else
                {
                    // 如果不在 UI 线程，则封送到 UI 线程执行
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        handler(this, new PropertyChangedEventArgs(name));
                    }));
                }
            }
        }
    }

    // 简单的 RelayCommand 实现
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object parameter) => _execute();

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}