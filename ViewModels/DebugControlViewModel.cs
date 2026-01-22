using ActiproRoslynPOC.Services;
using GalaSoft.MvvmLight.Command;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ActiproRoslynPOC.ViewModels
{
    /// <summary>
    /// 调试控制 ViewModel
    /// 负责调试会话的启动、停止、步进等操作
    /// </summary>
    public class DebugControlViewModel : INotifyPropertyChanged
    {
        private readonly DebuggerServiceV3Enhanced _debugger;
        private readonly RoslynCompilerService _compiler;

        private bool _isDebugging;
        private int _currentDebugLine = -1;
        private string _variablesText;
        private HashSet<int> _breakpoints;
        private Dictionary<string, string> _workflowArguments;

        public event PropertyChangedEventHandler PropertyChanged;
        public event Action<string> OutputMessageReceived;
        public event Action<int> CurrentLineChanged;
        public event Action<int> BreakpointHit;
        public event Action DebugSessionEnded;

        #region 属性

        /// <summary>
        /// 是否正在调试
        /// </summary>
        public bool IsDebugging
        {
            get => _isDebugging;
            set
            {
                if (_isDebugging != value)
                {
                    _isDebugging = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 当前调试行号
        /// </summary>
        public int CurrentDebugLine
        {
            get => _currentDebugLine;
            set
            {
                if (_currentDebugLine != value)
                {
                    _currentDebugLine = value;
                    OnPropertyChanged();
                    CurrentLineChanged?.Invoke(value);
                }
            }
        }

        /// <summary>
        /// 变量窗口文本
        /// </summary>
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

        /// <summary>
        /// 断点集合
        /// </summary>
        public HashSet<int> Breakpoints
        {
            get => _breakpoints;
            set
            {
                _breakpoints = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 工作流参数
        /// </summary>
        public Dictionary<string, string> WorkflowArguments
        {
            get => _workflowArguments;
            set
            {
                _workflowArguments = value;
                OnPropertyChanged();
            }
        }

        #endregion

        #region 命令

        public ICommand StartDebugCommand { get; }
        public ICommand StopDebugCommand { get; }
        public ICommand StepOverCommand { get; }
        public ICommand ContinueCommand { get; }

        #endregion

        public DebugControlViewModel(DebuggerServiceV3Enhanced debugger, RoslynCompilerService compiler)
        {
            _debugger = debugger ?? throw new ArgumentNullException(nameof(debugger));
            _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));

            _breakpoints = new HashSet<int>();
            _workflowArguments = new Dictionary<string, string>();

            // 订阅调试器事件
            _debugger.CurrentLineChanged += OnDebuggerCurrentLineChanged;
            _debugger.BreakpointHit += OnDebuggerBreakpointHit;
            _debugger.DebugSessionEnded += OnDebugSessionEnded;
            _debugger.VariablesUpdated += OnVariablesUpdated;
            _debugger.OutputMessage += OnDebuggerOutputMessage;

            // 初始化命令
            StartDebugCommand = new RelayCommand(async () => await ExecuteStartDebugAsync(), () => !IsDebugging);
            StopDebugCommand = new RelayCommand(ExecuteStopDebug, () => IsDebugging);
            StepOverCommand = new RelayCommand(async () => await ExecuteStepOverAsync(), () => IsDebugging);
            ContinueCommand = new RelayCommand(async () => await ExecuteContinueAsync(), () => IsDebugging);
        }

        #region 调试操作

        /// <summary>
        /// 启动调试
        /// </summary>
        public async Task ExecuteStartDebugAsync(string currentFileName, List<string> codeFiles)
        {
            if (IsDebugging)
            {
                OutputMessageReceived?.Invoke("[警告] 调试会话已在运行");
                return;
            }

            try
            {
                OutputMessageReceived?.Invoke("=== 启动调试会话 ===");
                LoggingService.Instance.Info("DebugControl", "启动调试会话");

                // 设置断点
                _debugger.SetBreakpoints(_breakpoints);

                // 设置工作流参数（转换为 object 字典）
                var arguments = new Dictionary<string, object>();
                foreach (var kvp in _workflowArguments)
                {
                    arguments[kvp.Key] = kvp.Value;
                }
                _debugger.SetWorkflowArguments(arguments);

                // 启动调试
                //bool success = await _debugger.StartDebuggingAsync(codeFiles, _compiler, currentFileName);

                //if (success)
                //{
                //    IsDebugging = true;
                //    OutputMessageReceived?.Invoke("✓ 调试启动成功");
                //    LoggingService.Instance.Info("DebugControl", "调试启动成功");
                //}
                //else
                //{
                //    OutputMessageReceived?.Invoke("[错误] 调试启动失败");
                //    IsDebugging = false;
                //    LoggingService.Instance.Error("DebugControl", "调试启动失败");
                //}
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("DebugControl", "启动调试失败", ex);
                OutputMessageReceived?.Invoke($"[错误] {ex.Message}");
                if (ex.InnerException != null)
                {
                    OutputMessageReceived?.Invoke($"[内部错误] {ex.InnerException.Message}");
                }
                IsDebugging = false;
            }
        }

        /// <summary>
        /// 停止调试
        /// </summary>
        private void ExecuteStopDebug()
        {
            try
            {
                _debugger.StopDebugging();
                IsDebugging = false;
                CurrentDebugLine = -1;
                VariablesText = string.Empty;

                OutputMessageReceived?.Invoke("✓ 调试已停止");
                LoggingService.Instance.Info("DebugControl", "调试已停止");
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("DebugControl", "停止调试失败", ex);
                OutputMessageReceived?.Invoke($"[错误] 停止调试失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 单步执行
        /// </summary>
        private async Task ExecuteStepOverAsync()
        {
            try
            {
                await _debugger.StepOverAsync();
                LoggingService.Instance.Debug("DebugControl", "执行单步");
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("DebugControl", "单步执行失败", ex);
                OutputMessageReceived?.Invoke($"[错误] 单步执行失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 继续执行
        /// </summary>
        private async Task ExecuteContinueAsync()
        {
            try
            {
                await _debugger.ContinueAsync();
                LoggingService.Instance.Debug("DebugControl", "继续执行");
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("DebugControl", "继续执行失败", ex);
                OutputMessageReceived?.Invoke($"[错误] 继续执行失败: {ex.Message}");
            }
        }

        #endregion

        #region 断点管理

        /// <summary>
        /// 添加断点
        /// </summary>
        public void AddBreakpoint(int lineNumber)
        {
            if (_breakpoints.Add(lineNumber))
            {
                OutputMessageReceived?.Invoke($"● 添加断点: 第 {lineNumber} 行");
                LoggingService.Instance.Debug("DebugControl", $"添加断点: 第 {lineNumber} 行");

                // 如果正在调试，动态添加到调试器
                if (IsDebugging)
                {
                    _debugger.AddBreakpoint(lineNumber);
                }
            }
        }

        /// <summary>
        /// 移除断点
        /// </summary>
        public void RemoveBreakpoint(int lineNumber)
        {
            if (_breakpoints.Remove(lineNumber))
            {
                OutputMessageReceived?.Invoke($"○ 移除断点: 第 {lineNumber} 行");
                LoggingService.Instance.Debug("DebugControl", $"移除断点: 第 {lineNumber} 行");

                // 如果正在调试，从调试器中移除
                if (IsDebugging)
                {
                    _debugger.RemoveBreakpoint(lineNumber);
                }
            }
        }

        /// <summary>
        /// 切换断点
        /// </summary>
        public void ToggleBreakpoint(int lineNumber)
        {
            if (_breakpoints.Contains(lineNumber))
            {
                RemoveBreakpoint(lineNumber);
            }
            else
            {
                AddBreakpoint(lineNumber);
            }
        }

        /// <summary>
        /// 更新断点列表
        /// </summary>
        public void UpdateBreakpoints(List<int> newBreakpoints)
        {
            _breakpoints = new HashSet<int>(newBreakpoints);
            OnPropertyChanged(nameof(Breakpoints));

            LoggingService.Instance.Debug("DebugControl", $"更新断点列表: {_breakpoints.Count} 个断点");
        }

        #endregion

        #region 调试器事件处理

        private void OnDebuggerCurrentLineChanged(int line)
        {
            CurrentDebugLine = line;
        }

        private void OnDebuggerBreakpointHit(int line)
        {
            BreakpointHit?.Invoke(line);
            OutputMessageReceived?.Invoke($"● 断点命中: 第 {line} 行");
        }

        private void OnDebugSessionEnded()
        {
            IsDebugging = false;
            CurrentDebugLine = -1;
            VariablesText = string.Empty;
            DebugSessionEnded?.Invoke();

            OutputMessageReceived?.Invoke("=== 调试会话结束 ===");
            LoggingService.Instance.Info("DebugControl", "调试会话结束");
        }

        private void OnVariablesUpdated(Dictionary<string, object> variables)
        {
            // 格式化变量显示
            var varLines = variables
                .OrderBy(kvp => kvp.Key)
                .Select(kvp => $"{kvp.Key} = {FormatValue(kvp.Value)}");

            VariablesText = string.Join(Environment.NewLine, varLines);
        }

        private void OnDebuggerOutputMessage(string message)
        {
            OutputMessageReceived?.Invoke(message);
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 格式化变量值
        /// </summary>
        private string FormatValue(object value)
        {
            if (value == null)
                return "null";

            if (value is string str)
                return $"\"{str}\"";

            return value.ToString();
        }

        /// <summary>
        /// 设置工作流参数
        /// </summary>
        public void SetWorkflowArgument(string name, string value)
        {
            _workflowArguments[name] = value;
            LoggingService.Instance.Debug("DebugControl", $"设置工作流参数: {name} = {value}");
        }

        #endregion

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private async Task ExecuteStartDebugAsync()
        {
            // 空实现，实际调用需要从外部传入文件列表
            OutputMessageReceived?.Invoke("[警告] 请使用带参数的 ExecuteStartDebugAsync 方法");
        }
    }
}
