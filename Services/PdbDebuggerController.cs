using Microsoft.Diagnostics.Runtime;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ActiproRoslynPOC.Services
{
    /// <summary>
    /// 基于 PDB 的调试器控制器
    /// 使用 Microsoft.Diagnostics.Runtime (ClrMD) 进行进程调试
    /// </summary>
    public class PdbDebuggerController
    {
        private Process _targetProcess;
        private DataTarget _dataTarget;
        private ClrRuntime _runtime;
        private PdbReaderService _pdbReader;
        private string _dllPath;
        private string _pdbPath;
        private HashSet<int> _breakpoints = new HashSet<int>();
        private bool _isDebugging = false;
        private SynchronizationContext _uiContext;
        private CancellationTokenSource _cancellationTokenSource;

        // 事件
        public event Action<int> CurrentLineChanged;
        public event Action<int> BreakpointHit;
        public event Action DebugSessionEnded;
        public event Action<Dictionary<string, object>> VariablesUpdated;
        public event Action<string> OutputMessage;

        public bool IsDebugging => _isDebugging;

        /// <summary>
        /// 设置断点
        /// </summary>
        public void SetBreakpoints(IEnumerable<int> lineNumbers)
        {
            _breakpoints = new HashSet<int>(lineNumbers);
            LogMessage($"已设置 {_breakpoints.Count} 个断点: {string.Join(", ", _breakpoints)}");
        }

        /// <summary>
        /// 开始调试会话
        /// </summary>
        public async Task<bool> StartDebuggingAsync(
            byte[] dllBytes,
            byte[] pdbBytes,
            Dictionary<string, string> codeFiles,
            string mainFilePath)
        {
            if (_isDebugging)
            {
                LogMessage("调试会话已在运行中");
                return false;
            }

            try
            {
                _isDebugging = true;
                _uiContext = SynchronizationContext.Current;
                _cancellationTokenSource = new CancellationTokenSource();

                // 1. 保存 DLL 和 PDB 到临时目录
                var tempDir = Path.Combine(Path.GetTempPath(), "ActiproDebug_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);

                _dllPath = Path.Combine(tempDir, "Workflow.dll");
                _pdbPath = Path.Combine(tempDir, "Workflow.pdb");

                File.WriteAllBytes(_dllPath, dllBytes);
                File.WriteAllBytes(_pdbPath, pdbBytes);

                LogMessage($"临时文件已创建:");
                LogMessage($"  DLL: {_dllPath}");
                LogMessage($"  PDB: {_pdbPath}");

                // 2. 加载 PDB 信息
                _pdbReader = new PdbReaderService();
                if (!_pdbReader.LoadFromBytes(pdbBytes))
                {
                    LogMessage("错误: 无法加载 PDB 文件");
                    _isDebugging = false;
                    return false;
                }

                LogMessage($"✓ PDB 已加载,可执行行: {string.Join(", ", _pdbReader.GetAllExecutableLines())}");

                // 3. 启动 WorkflowRunner.exe
                var runnerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WorkflowRunner.exe");
                if (!File.Exists(runnerPath))
                {
                    LogMessage($"错误: 找不到 WorkflowRunner.exe: {runnerPath}");
                    _isDebugging = false;
                    return false;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = runnerPath,
                    Arguments = $"\"{_dllPath}\" --wait-for-debugger",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = tempDir
                };

                _targetProcess = Process.Start(startInfo);
                _targetProcess.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        LogMessage($"[Runner] {e.Data}");
                };
                _targetProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        LogMessage($"[Runner Error] {e.Data}");
                };

                _targetProcess.BeginOutputReadLine();
                _targetProcess.BeginErrorReadLine();

                LogMessage($"✓ WorkflowRunner 已启动, PID: {_targetProcess.Id}");

                // 4. 等待进程初始化
                await Task.Delay(1000);

                // 5. 附加到目标进程
                if (!AttachToProcess(_targetProcess.Id))
                {
                    LogMessage("错误: 无法附加到目标进程");
                    StopDebugging();
                    return false;
                }

                // 6. 开始监控调试会话
                _ = Task.Run(() => MonitorDebugSessionAsync(_cancellationTokenSource.Token));

                return true;
            }
            catch (Exception ex)
            {
                LogMessage($"启动调试失败: {ex.Message}");
                LogMessage($"堆栈跟踪: {ex.StackTrace}");
                _isDebugging = false;
                return false;
            }
        }

        /// <summary>
        /// 附加到目标进程
        /// </summary>
        private bool AttachToProcess(int processId)
        {
            try
            {
                LogMessage($"正在附加到进程 {processId}...");

                // 使用 ClrMD 附加到进程
                _dataTarget = DataTarget.AttachToProcess(processId, suspend: false);

                // 获取 CLR 运行时
                if (_dataTarget.ClrVersions.Length == 0)
                {
                    LogMessage("错误: 目标进程中没有 CLR 运行时");
                    return false;
                }

                _runtime = _dataTarget.ClrVersions[0].CreateRuntime();
                LogMessage($"✓ 已附加到进程, CLR 版本: {_runtime.ClrInfo.Version}");

                return true;
            }
            catch (Exception ex)
            {
                LogMessage($"附加进程失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 监控调试会话
        /// </summary>
        private async Task MonitorDebugSessionAsync(CancellationToken cancellationToken)
        {
            try
            {
                LogMessage("调试监控已启动");

                while (!cancellationToken.IsCancellationRequested && !_targetProcess.HasExited)
                {
                    await Task.Delay(100, cancellationToken);

                    // 检查断点 (简化版本 - ClrMD 不直接支持断点,需要轮询线程状态)
                    // 实际生产环境应使用 ICorDebug API
                    CheckThreadsForBreakpoints();
                }

                LogMessage("调试监控已停止");
            }
            catch (OperationCanceledException)
            {
                LogMessage("调试监控被取消");
            }
            catch (Exception ex)
            {
                LogMessage($"调试监控异常: {ex.Message}");
            }
            finally
            {
                StopDebugging();
            }
        }

        /// <summary>
        /// 检查线程是否命中断点 (简化版本)
        /// 注意: ClrMD 主要用于快照分析,不是实时调试器
        /// 真正的断点需要使用 ICorDebug API
        /// </summary>
        private void CheckThreadsForBreakpoints()
        {
            try
            {
                foreach (var thread in _runtime.Threads)
                {
                    if (!thread.IsAlive) continue;

                    foreach (var frame in thread.EnumerateStackTrace())
                    {
                        var method = frame.Method;
                        if (method == null) continue;

                        // 获取当前 IL 偏移量
                        var ilOffset = frame.InstructionPointer;

                        // 查找对应的行号
                        var lineNumber = _pdbReader.GetLineNumberForILOffset((int)ilOffset);
                        if (lineNumber > 0 && _breakpoints.Contains(lineNumber))
                        {
                            LogMessage($"命中断点: 第 {lineNumber} 行");
                            NotifyBreakpointHit(lineNumber);

                            // 提取变量
                            ExtractVariables(thread);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PdbDebugger] 检查断点失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 提取变量 (从线程栈帧)
        /// </summary>
        private void ExtractVariables(ClrThread thread)
        {
            try
            {
                var variables = new Dictionary<string, object>();

                foreach (var frame in thread.EnumerateStackTrace())
                {
                    var method = frame.Method;
                    if (method == null) continue;

                    // 获取局部变量 (需要结合 PDB 信息)
                    // ClrMD 可以读取堆对象,但局部变量需要更底层的 API

                    LogMessage($"方法: {method.Name}");
                }

                NotifyVariablesUpdated(variables);
            }
            catch (Exception ex)
            {
                LogMessage($"提取变量失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 停止调试
        /// </summary>
        public void StopDebugging()
        {
            if (!_isDebugging) return;

            _isDebugging = false;

            try
            {
                _cancellationTokenSource?.Cancel();

                // 分离调试器
                _runtime = null;
                _dataTarget?.Dispose();
                _dataTarget = null;

                // 终止目标进程
                if (_targetProcess != null && !_targetProcess.HasExited)
                {
                    _targetProcess.Kill();
                    _targetProcess.Dispose();
                }

                // 清理临时文件
                if (File.Exists(_dllPath))
                {
                    try
                    {
                        var tempDir = Path.GetDirectoryName(_dllPath);
                        Directory.Delete(tempDir, true);
                    }
                    catch { }
                }

                LogMessage("调试会话已停止");
                NotifyDebugSessionEnded();
            }
            catch (Exception ex)
            {
                LogMessage($"停止调试失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 单步执行 (ClrMD 不支持,需要 ICorDebug)
        /// </summary>
        public Task StepOverAsync()
        {
            LogMessage("警告: ClrMD 不支持单步执行,请使用 ICorDebug API");
            return Task.CompletedTask;
        }

        /// <summary>
        /// 继续执行 (ClrMD 不支持,需要 ICorDebug)
        /// </summary>
        public Task ContinueAsync()
        {
            LogMessage("警告: ClrMD 不支持继续执行,请使用 ICorDebug API");
            return Task.CompletedTask;
        }

        #region 事件通知

        private void LogMessage(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[PdbDebugger] {message}");
            NotifyOutputMessage(message);
        }

        private void NotifyCurrentLineChanged(int lineNumber)
        {
            if (_uiContext != null)
                _uiContext.Post(_ => CurrentLineChanged?.Invoke(lineNumber), null);
            else
                CurrentLineChanged?.Invoke(lineNumber);
        }

        private void NotifyBreakpointHit(int lineNumber)
        {
            if (_uiContext != null)
                _uiContext.Post(_ => BreakpointHit?.Invoke(lineNumber), null);
            else
                BreakpointHit?.Invoke(lineNumber);
        }

        private void NotifyDebugSessionEnded()
        {
            if (_uiContext != null)
                _uiContext.Post(_ => DebugSessionEnded?.Invoke(), null);
            else
                DebugSessionEnded?.Invoke();
        }

        private void NotifyVariablesUpdated(Dictionary<string, object> variables)
        {
            if (_uiContext != null)
                _uiContext.Post(_ => VariablesUpdated?.Invoke(variables), null);
            else
                VariablesUpdated?.Invoke(variables);
        }

        private void NotifyOutputMessage(string message)
        {
            if (_uiContext != null)
                _uiContext.Post(_ => OutputMessage?.Invoke(message), null);
            else
                OutputMessage?.Invoke(message);
        }

        #endregion
    }
}
