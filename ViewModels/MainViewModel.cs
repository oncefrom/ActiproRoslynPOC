using ActiproRoslynPOC.Models;
using ActiproRoslynPOC.Services;
using GalaSoft.MvvmLight.Command;
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
        // 子 ViewModels (职责分离)
        private readonly FileManagementViewModel _fileManagement;
        private readonly ProjectManagementViewModel _projectManagement;
        private readonly DebugControlViewModel _debugControl;

        // 核心服务
        private readonly RoslynCompilerService _compiler;
        private readonly CodeExecutionService _executor;
        private readonly DebuggerServiceV3Enhanced _debugger;

        // 输出和编译
        private string _output;

        // 工作流参数
        private readonly WorkflowParameterService _parameterService = new WorkflowParameterService();
        private WorkflowSignatureInfo _currentWorkflowSignature;

        // 项目管理（委托给 ProjectManagementViewModel）
        private ProjectConfig _currentProject;

        /// <summary>
        /// 文件管理 ViewModel
        /// </summary>
        public FileManagementViewModel FileManagement => _fileManagement;

        /// <summary>
        /// 项目管理 ViewModel
        /// </summary>
        public ProjectManagementViewModel ProjectManagement => _projectManagement;

        /// <summary>
        /// 调试控制 ViewModel
        /// </summary>
        public DebugControlViewModel DebugControl => _debugControl;

        /// <summary>
        /// 项目根节点（委托给 ProjectManagementViewModel）
        /// </summary>
        public ObservableCollection<FileTreeNode> ProjectRootNodes => _projectManagement.ProjectTree;

        /// <summary>
        /// 当前项目路径（委托给 ProjectManagementViewModel）
        /// </summary>
        public string CurrentProjectPath => _projectManagement.CurrentProjectPath;

        public MainViewModel()
        {
            // 初始化核心服务
            _compiler = new RoslynCompilerService();
            _executor = new CodeExecutionService();
            _debugger = new DebuggerServiceV3Enhanced();

            // 初始化子 ViewModels
            _fileManagement = new FileManagementViewModel();
            _projectManagement = new ProjectManagementViewModel();
            _debugControl = new DebugControlViewModel(_debugger, _compiler);

            // 订阅文件管理事件
            _fileManagement.PropertyChanged += OnFileManagementPropertyChanged;
            _fileManagement.OutputMessageReceived += AppendOutput;
            _fileManagement.FileLoaded += OnFileLoaded;
            _fileManagement.FileSaved += OnFileSaved;

            // 订阅项目管理事件
            _projectManagement.OutputMessageReceived += AppendOutput;
            _projectManagement.FileSelected += OnFileSelected;

            // 订阅调试控制事件（暂时注释，稍后启用）
            //_debugControl.OutputMessageReceived += AppendOutput;
            //_debugControl.CurrentLineChanged += OnDebugLineChanged;
            //_debugControl.BreakpointHit += OnDebugBreakpointHit;
            //_debugControl.DebugSessionEnded += OnDebugEnded;

            // 订阅全局 Log 管理器
            GlobalLogManager.LogReceived += (msg) => AppendOutput(msg);

            // 重定向 Console 输出
            var consoleWriter = new ConsoleRedirectWriter(msg => AppendOutput(msg));
            Console.SetOut(consoleWriter);

            // 初始化命令
            RunCommand = new RelayCommand(ExecuteRun);
            CheckSyntaxCommand = new RelayCommand(ExecuteCheckSyntax);
            ClearOutputCommand = new RelayCommand(() => Output = "");

            // 文件命令（委托给 FileManagementViewModel）
            SaveCommand = _fileManagement.SaveCommand;
            OpenFileCommand = _fileManagement.OpenFileCommand;
            NewFileCommand = _fileManagement.NewFileCommand;

            // 调试命令（暂时注释）
            //StartDebugCommand = _debugControl.StartDebugCommand;
            //StopDebugCommand = _debugControl.StopDebugCommand;
            //StepOverCommand = _debugControl.StepOverCommand;
            //ContinueCommand = _debugControl.ContinueCommand;

            // 初始化集合
            Diagnostics = new ObservableCollection<DiagnosticInfo>();

            // 启动时加载默认文件
            LoadDefaultFile();
        }

        #region 属性（委托给子 ViewModels）

        /// <summary>
        /// 代码内容（委托给 FileManagementViewModel）
        /// </summary>
        public string Code
        {
            get => _fileManagement.Code;
            set
            {
                if (_fileManagement.Code != value)
                {
                    _fileManagement.Code = value;
                    OnPropertyChanged();
                    AnalyzeWorkflowSignature(); // 分析工作流签名
                }
            }
        }

        /// <summary>
        /// 当前文件路径（委托给 FileManagementViewModel）
        /// </summary>
        public string CurrentFilePath
        {
            get => _fileManagement.CurrentFilePath;
            set
            {
                _fileManagement.PropertyChanged -= OnFileManagementPropertyChanged;
                if (_fileManagement.CurrentFilePath != value)
                {
                    // 通过 setter 触发
                    // FileManagementViewModel 内部会触发 PropertyChanged
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentFileName));
                    OnPropertyChanged(nameof(WindowTitle));
                }
                _fileManagement.PropertyChanged += OnFileManagementPropertyChanged;
            }
        }

        /// <summary>
        /// 当前文件名（委托给 FileManagementViewModel）
        /// </summary>
        public string CurrentFileName => _fileManagement.CurrentFileName;

        /// <summary>
        /// 是否已修改（委托给 FileManagementViewModel）
        /// </summary>
        public bool IsModified
        {
            get => _fileManagement.IsModified;
            set
            {
                if (_fileManagement.IsModified != value)
                {
                    _fileManagement.IsModified = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(WindowTitle));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>
        /// 窗口标题（委托给 FileManagementViewModel）
        /// </summary>
        public string WindowTitle => _fileManagement.WindowTitle;

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

        // 工作流参数属性
        public WorkflowSignatureInfo CurrentWorkflowSignature
        {
            get => _currentWorkflowSignature;
            set
            {
                _currentWorkflowSignature = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasWorkflowParameters));
            }
        }

        public bool HasWorkflowParameters => CurrentWorkflowSignature?.HasCustomParameters == true;

        public Dictionary<string, string> WorkflowArguments
        {
            get => _debugControl.WorkflowArguments;
            set
            {
                if (value != null)
                {
                    foreach (var kvp in value)
                    {
                        _debugControl.SetWorkflowArgument(kvp.Key, kvp.Value);
                    }
                }
                OnPropertyChanged();
            }
        }
        #endregion

        /// <summary>
        /// 分析当前代码的工作流签名
        /// </summary>
        public void AnalyzeWorkflowSignature()
        {
            try
            {
                // 获取项目目录中的所有代码文件
                var codeFiles = new Dictionary<string, string>();
                var projectDirectory = GetProjectDirectory();

                if (!string.IsNullOrEmpty(projectDirectory) && Directory.Exists(projectDirectory))
                {
                    var csFiles = Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories);
                    foreach (var filePath in csFiles)
                    {
                        var fileName = Path.GetFileName(filePath);
                        codeFiles[fileName] = File.ReadAllText(filePath);
                    }
                }
                else
                {
                    // 单文件模式
                    codeFiles[CurrentFileName ?? "main.cs"] = Code;
                }

                var result = _compiler.CompileMultiple(codeFiles);

                if (result.Success)
                {
                    var workflowType = result.Assembly.GetTypes()
                        .FirstOrDefault(t => t.IsSubclassOf(typeof(CodedWorkflowBase)) && !t.IsAbstract);

                    if (workflowType != null)
                    {
                        CurrentWorkflowSignature = _parameterService.GetWorkflowSignature(workflowType);

                        if (CurrentWorkflowSignature.HasCustomParameters)
                        {
                            AppendOutput($"[参数分析] 检测到工作流参数:");
                            foreach (var param in CurrentWorkflowSignature.InputParameters)
                            {
                                var defaultInfo = param.HasDefaultValue ? $" = {param.DefaultValue}" : " (必填)";
                                AppendOutput($"  - {param.TypeDisplayName} {param.Name}{defaultInfo}");
                            }

                            if (CurrentWorkflowSignature.HasReturnValue)
                            {
                                AppendOutput($"  返回类型: {CurrentWorkflowSignature.ReturnTypeDisplayName}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AnalyzeWorkflowSignature] 分析失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置工作流参数
        /// </summary>
        public void SetWorkflowArgument(string name, string value)
        {
            _debugControl.SetWorkflowArgument(name, value);
        }

        // 调试状态属性（委托给 DebugControlViewModel）
        public bool IsDebugging
        {
            get => _debugControl.IsDebugging;
            set { /* 只读属性，但支持绑定 */ }
        }

        public int CurrentDebugLine
        {
            get => _debugControl.CurrentDebugLine;
            set { /* 只读属性，但支持绑定 */ }
        }

        public string VariablesText
        {
            get => _debugControl.VariablesText;
            set { /* 只读属性，但支持绑定 */ }
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
                // LogEvent subscription removed - GlobalLogManager handles all logs

                // 设置工作流参数
                SetWorkflowArgumentsFromUI(workflow);

                AppendOutput("开始执行...");
                workflow.Execute();

                sw.Stop();
                AppendOutput($"执行完成，耗时 {sw.ElapsedMilliseconds}ms");
                if (workflow.Result != null)
                    AppendOutput($"返回结果: {FormatResult(workflow.Result)}");
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("MainViewModel", "执行工作流失败", ex);
                AppendOutput($"[错误] {ex.Message}");
                if (ex.InnerException != null)
                {
                    AppendOutput($"[内部错误] {ex.InnerException.Message}");
                }
            }
        }

        public string GetProjectDirectory()
        {
            // 使用配置服务获取工作流目录
            return ConfigurationService.Instance.GetProjectWorkflowDirectory(CurrentProjectPath);
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
                // LogEvent subscription removed - GlobalLogManager handles all logs

                // 设置工作流参数
                SetWorkflowArgumentsFromUI(workflow);

                workflow.Execute();

                AppendOutput("执行完成！");
                if (workflow.Result != null)
                    AppendOutput($"返回结果: {FormatResult(workflow.Result)}");
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("MainViewModel", "调试执行失败", ex);
                AppendOutput($"[错误] {ex.Message}");
                if (ex.InnerException != null)
                {
                    AppendOutput($"[内部错误] {ex.InnerException.Message}");
                }
            }
        }

        /// <summary>
        /// 从 UI 设置的参数传递给工作流实例
        /// </summary>
        private void SetWorkflowArgumentsFromUI(CodedWorkflowBase workflow)
        {
            if (workflow == null) return;

            // 初始化 Services（支持 services.WorkflowInvocationService.RunWorkflow）
            var projectDirectory = GetProjectDirectory();
            workflow.Services = new WorkflowServices(projectDirectory);

            // 获取工作流签名
            var signature = _parameterService.GetWorkflowSignature(workflow.GetType());
            if (signature == null || !signature.HasCustomParameters) return;

            // 将 UI 中设置的参数值传递给工作流
            foreach (var param in signature.InputParameters)
            {
                if (WorkflowArguments.TryGetValue(param.Name, out var stringValue))
                {
                    var convertedValue = _parameterService.ConvertValue(stringValue, param.ParameterType);
                    workflow.Arguments[param.Name] = convertedValue;
                    AppendOutput($"[参数] {param.Name} = {convertedValue}");
                }
                else if (param.HasDefaultValue)
                {
                    workflow.Arguments[param.Name] = param.DefaultValue;
                }
            }
        }

        /// <summary>
        /// 格式化返回结果（支持元组显示）
        /// </summary>
        private string FormatResult(object result)
        {
            if (result == null) return "<null>";

            var type = result.GetType();

            // 处理元组类型
            if (type.IsGenericType && type.Name.StartsWith("ValueTuple"))
            {
                var fields = type.GetFields();
                var values = new List<string>();

                foreach (var field in fields)
                {
                    var value = field.GetValue(result);
                    values.Add($"{field.Name}: {value}");
                }

                return $"({string.Join(", ", values)})";
            }

            return result.ToString();
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

                    // 4. 智能添加时间戳：如果消息已经有时间戳格式 [HH:mm:ss]，则不再添加
                    string timestamp = DateTime.Now.ToString("HH:mm:ss");
                    bool hasTimestamp = message.StartsWith("[") && message.Length > 10 && message[9] == ']';

                    if (hasTimestamp)
                    {
                        // 消息已有时间戳，直接输出
                        Output += $"{message}{Environment.NewLine}";
                    }
                    else
                    {
                        // 消息无时间戳，添加时间戳
                        Output += $"[{timestamp}] {message}{Environment.NewLine}";
                    }
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
        /// 加载指定文件到编辑器（委托给 FileManagementViewModel）
        /// </summary>
        public void LoadFile(string filePath)
        {
            _fileManagement.LoadFile(filePath);
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
        /// 开始调试 (PDB 增强版)
        /// </summary>
//        private async void ExecuteStartDebug()
//        {
//            try
//            {
//                Output = "";
//                AppendOutput("=== 开始调试 (PDB 增强版) ===");
//
//                // 从当前活动编辑器获取断点
//                var breakpoints = GetBreakpointsFromUI?.Invoke() ?? new List<int>();
//                _debugger.SetBreakpoints(breakpoints);
//
//                AppendOutput($"设置了 {breakpoints.Count} 个断点: {string.Join(", ", breakpoints)}");
//
//                // 先保存当前编辑器中的代码（如果有修改）
//                if (IsModified && !string.IsNullOrEmpty(CurrentFilePath))
//                {
//                    AppendOutput("检测到未保存的修改，自动保存中...");
//                    ExecuteSave();
//                }
//
//                string projectDirectory = GetProjectDirectory();
//
//                // 检查是否有其他依赖文件（同目录下的其他 .cs 文件）
//                bool hasOtherCsFiles = !string.IsNullOrEmpty(projectDirectory) &&
//                                      Directory.Exists(projectDirectory) &&
//                                      Directory.GetFiles(projectDirectory, "*.cs").Length > 1;
//
//                AppendOutput($"调试文件: {CurrentFileName}");
//
//                var codeFiles = new Dictionary<string, string>();
//
//                if (hasOtherCsFiles)
//                {
//                    // 多文件模式：加载所有文件以满足依赖，但只调试当前文件
//                    AppendOutput($"检测到项目目录中有其他文件，加载依赖文件...");
//
//                    var csFiles = Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories);
//                    foreach (var filePath in csFiles)
//                    {
//                        var fileName = Path.GetFileName(filePath);
//
//                        // 当前文件使用编辑器中的代码（可能有未保存的修改）
//                        if (fileName.Equals(CurrentFileName, StringComparison.OrdinalIgnoreCase))
//                        {
//                            codeFiles[fileName] = Code;
//                            AppendOutput($"  [主] {fileName}");
//                        }
//                        else
//                        {
//                            // 其他文件从磁盘读取
//                            var fileCode = File.ReadAllText(filePath);
//                            codeFiles[fileName] = fileCode;
//                            AppendOutput($"  [依赖] {fileName}");
//                        }
//                    }
//                }
//                else
//                {
//                    // 单文件模式：只有当前文件
//                    codeFiles[CurrentFileName] = Code;
//                }
//
//                // 设置工作流参数（从 UI 获取）
//                var debugArguments = new Dictionary<string, object>();
//                if (CurrentWorkflowSignature?.HasCustomParameters == true)
//                {
//                    foreach (var param in CurrentWorkflowSignature.InputParameters)
//                    {
//                        if (_workflowArguments.TryGetValue(param.Name, out var stringValue))
//                        {
//                            debugArguments[param.Name] = _parameterService.ConvertValue(stringValue, param.ParameterType);
//                        }
//                        else if (param.HasDefaultValue)
//                        {
//                            debugArguments[param.Name] = param.DefaultValue;
//                        }
//                    }
//                }
//                _debugger.SetWorkflowArguments(debugArguments);
//
//                // 启动调试：PDB 增强版会自动进行智能插桩
//                // 明确指定当前文件为主调试对象
//                bool success = await _debugger.StartDebuggingAsync(codeFiles, _compiler, CurrentFileName);
//
//                if (success)
//                {
//                    IsDebugging = true;
//                    AppendOutput("✓ 调试启动成功");
//                }
//                else
//                {
//                    AppendOutput("[错误] 调试启动失败");
//                    IsDebugging = false;
//                }
//            }
//            catch (Exception ex)
//            {
//                LoggingService.Instance.Error("MainViewModel", "启动调试失败", ex);
//                AppendOutput($"[错误] {ex.Message}");
//                if (ex.InnerException != null)
//                {
//                    AppendOutput($"[内部错误] {ex.InnerException.Message}");
//                }
//                IsDebugging = false;
//            }
//        }
//
//        /// <summary>
//        /// 停止调试
//        /// </summary>
//        private void ExecuteStopDebug()
//        {
//            _debugger.StopDebugging();
//            IsDebugging = false;
//            AppendOutput("=== 调试已停止 ===");
//        }
//
//        /// <summary>
//        /// 单步执行
//        /// </summary>
//        private async Task ExecuteStepOverAsync()
//        {
//            await _debugger.StepOverAsync();
//        }
//
//        /// <summary>
//        /// 继续执行
//        /// </summary>
//        private async Task ExecuteContinueAsync()
//        {
//            await _debugger.ContinueAsync();
//        }
//
//        /// <summary>
//        /// 当前行变化事件处理
//        /// </summary>
//        private void OnDebuggerCurrentLineChanged(int line)
//        {
//            CurrentDebugLine = line;
//            DebugLineChanged?.Invoke(line);
//        }
//
//        /// <summary>
//        /// 断点命中事件处理
//        /// </summary>
//        private void OnDebuggerBreakpointHit(int line)
//        {
//            AppendOutput($"● 断点命中: 第 {line} 行");
//        }
//
//        /// <summary>
//        /// 调试会话结束事件处理
//        /// </summary>
//        private void OnDebugSessionEnded()
//        {
//            IsDebugging = false;
//            CurrentDebugLine = -1;
//            DebugLineChanged?.Invoke(-1);
//            AppendOutput("=== 调试完成 ===");
//        }
//
//        /// <summary>
//        /// 变量更新事件处理
//        /// </summary>
//        private void OnVariablesUpdated(Dictionary<string, object> variables)
//        {
//            var sb = new System.Text.StringBuilder();
//            sb.AppendLine("变量:");
//            foreach (var kvp in variables)
//            {
//                sb.AppendLine($"  {kvp.Key} = {kvp.Value}");
//            }
//            VariablesText = sb.ToString();
//        }
//
//        /// <summary>
        /// 从 UI 获取断点的委托（由 MainWindow 设置）
        /// </summary>
        public Func<List<int>> GetBreakpointsFromUI { get; set; }

        /// <summary>
        /// 添加断点（调试过程中动态添加）
        /// </summary>
        public void AddBreakpoint(int lineNumber)
        {
            if (_debugger.IsDebugging)
            {
                _debugger.AddBreakpoint(lineNumber);
            }
        }

        /// <summary>
        /// 移除断点（调试过程中动态移除）
        /// </summary>
        public void RemoveBreakpoint(int lineNumber)
        {
            if (_debugger.IsDebugging)
            {
                _debugger.RemoveBreakpoint(lineNumber);
            }
        }

        /// <summary>
        /// 切换断点状态（调试过程中动态切换）
        /// </summary>
        public void ToggleBreakpoint(int lineNumber)
        {
            if (_debugger.IsDebugging)
            {
                _debugger.ToggleBreakpoint(lineNumber);
            }
        }

        /// <summary>
        /// 更新所有断点（调试过程中批量更新）
        /// </summary>
        public void UpdateBreakpoints(List<int> newBreakpoints)
        {
            if (_debugger.IsDebugging)
            {
                _debugger.SetBreakpoints(newBreakpoints);
                AppendOutput($"[断点更新] 当前 {newBreakpoints.Count} 个断点: {string.Join(", ", newBreakpoints)}");
            }
        }

        #endregion

        #region 项目管理

        /// <summary>
        /// 加载项目到文件树（委托给 ProjectManagementViewModel）
        /// </summary>
        public void LoadProject(string projectPath)
        {
            try
            {
                if (!ProjectService.IsValidProject(projectPath))
                {
                    AppendOutput($"[错误] 无效的项目目录: {projectPath}");
                    return;
                }

                _projectManagement.LoadProject(projectPath);
                _currentProject = ProjectService.OpenProject(projectPath);

                AppendOutput($"[项目] 已加载项目: {_currentProject.Name}");
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("MainViewModel", "加载项目失败", ex);
                AppendOutput($"[错误] 加载项目失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 刷新项目文件树
        /// </summary>
        public void RefreshProjectTree()
        {
            if (string.IsNullOrEmpty(CurrentProjectPath))
                return;

            // 保存展开状态
            var expandedPaths = new HashSet<string>();
            CollectExpandedPaths(ProjectRootNodes, expandedPaths);

            // 重建树
            ProjectRootNodes.Clear();
            var rootNode = FileTreeNode.FromPath(CurrentProjectPath);
            rootNode.IsExpanded = true;
            ProjectRootNodes.Add(rootNode);

            // 恢复展开状态
            RestoreExpandedPaths(ProjectRootNodes, expandedPaths);
        }

        /// <summary>
        /// 收集所有展开节点的路径
        /// </summary>
        private void CollectExpandedPaths(ObservableCollection<FileTreeNode> nodes, HashSet<string> expandedPaths)
        {
            foreach (var node in nodes)
            {
                if (node.IsExpanded)
                {
                    expandedPaths.Add(node.FullPath);
                }

                if (node.Children.Count > 0)
                {
                    CollectExpandedPaths(node.Children, expandedPaths);
                }
            }
        }

        /// <summary>
        /// 恢复展开节点的状态
        /// </summary>
        private void RestoreExpandedPaths(ObservableCollection<FileTreeNode> nodes, HashSet<string> expandedPaths)
        {
            foreach (var node in nodes)
            {
                if (expandedPaths.Contains(node.FullPath))
                {
                    node.IsExpanded = true;
                }

                if (node.Children.Count > 0)
                {
                    RestoreExpandedPaths(node.Children, expandedPaths);
                }
            }
        }

        /// <summary>
        /// 获取选中的文件树节点
        /// </summary>
        public FileTreeNode GetSelectedNode()
        {
            return FindSelectedNode(ProjectRootNodes);
        }

        private FileTreeNode FindSelectedNode(ObservableCollection<FileTreeNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.IsSelected)
                    return node;

                if (node.Children.Count > 0)
                {
                    var selected = FindSelectedNode(node.Children);
                    if (selected != null)
                        return selected;
                }
            }
            return null;
        }

        #endregion

        #region 子 ViewModel 事件处理

        /// <summary>
        /// 监听 FileManagementViewModel 的属性变化
        /// </summary>
        private void OnFileManagementPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // 转发相关属性的变化通知
            switch (e.PropertyName)
            {
                case nameof(FileManagementViewModel.CurrentFilePath):
                    OnPropertyChanged(nameof(CurrentFilePath));
                    OnPropertyChanged(nameof(CurrentFileName));
                    OnPropertyChanged(nameof(WindowTitle));
                    break;
                case nameof(FileManagementViewModel.IsModified):
                    OnPropertyChanged(nameof(IsModified));
                    OnPropertyChanged(nameof(WindowTitle));
                    break;
                case nameof(FileManagementViewModel.Code):
                    OnPropertyChanged(nameof(Code));
                    AnalyzeWorkflowSignature();
                    break;
            }
        }

        /// <summary>
        /// 文件加载完成事件
        /// </summary>
        private void OnFileLoaded(string filePath)
        {
            LoggingService.Instance.Info("MainViewModel", $"文件加载完成: {filePath}");
            AnalyzeWorkflowSignature();
        }

        /// <summary>
        /// 文件保存完成事件
        /// </summary>
        private void OnFileSaved(string filePath)
        {
            LoggingService.Instance.Info("MainViewModel", $"文件保存完成: {filePath}");
        }

        /// <summary>
        /// 项目树中文件被选中事件
        /// </summary>
        private void OnFileSelected(string filePath)
        {
            LoggingService.Instance.Info("MainViewModel", $"选中文件: {filePath}");
            _fileManagement.LoadFile(filePath);
        }

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
}