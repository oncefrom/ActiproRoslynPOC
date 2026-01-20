using System;
using System.Activities;
using System.Activities.Validation;
using System.Activities.XamlIntegration;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Xaml;

namespace ActiproRoslynPOC.Services
{
    /// <summary>
    /// XAML 工作流执行服务
    /// 基于 Windows Workflow Foundation (WF4)
    /// </summary>
    public class XamlWorkflowService
    {
        /// <summary>
        /// 工作流执行完成事件
        /// </summary>
        public event Action<Dictionary<string, object>> WorkflowCompleted;

        /// <summary>
        /// 工作流执行错误事件
        /// </summary>
        public event Action<Exception> WorkflowError;

        /// <summary>
        /// 日志输出事件
        /// </summary>
        public event Action<string> LogOutput;

        /// <summary>
        /// 同步执行 XAML 工作流
        /// </summary>
        /// <param name="xamlFilePath">XAML 文件路径</param>
        /// <param name="inputArguments">输入参数</param>
        /// <returns>输出结果</returns>
        public Dictionary<string, object> ExecuteWorkflow(string xamlFilePath, Dictionary<string, object> inputArguments = null)
        {
            var result = new Dictionary<string, object>();

            try
            {
                if (!File.Exists(xamlFilePath))
                {
                    throw new FileNotFoundException($"XAML 文件不存在: {xamlFilePath}");
                }

                LogOutput?.Invoke($"[XAML] 加载工作流: {Path.GetFileName(xamlFilePath)}");

                // 加载 XAML 工作流
                Activity workflow = LoadWorkflowFromXaml(xamlFilePath);

                if (workflow == null)
                {
                    throw new InvalidOperationException("无法加载工作流定义");
                }

                LogOutput?.Invoke($"[XAML] 工作流类型: {workflow.GetType().Name}");

                // 准备输入参数
                var inputs = inputArguments ?? new Dictionary<string, object>();

                // 创建工作流应用程序
                var workflowApp = new WorkflowApplication(workflow, inputs);

                // 设置完成回调
                var completedEvent = new ManualResetEvent(false);
                IDictionary<string, object> outputs = null;
                Exception workflowException = null;

                workflowApp.Completed = (e) =>
                {
                    completedEvent.Set();
                    if (e.CompletionState == ActivityInstanceState.Closed)
                    {
                        outputs = e.Outputs;
                        LogOutput?.Invoke("[XAML] 工作流执行完成");
                    }
                    else if (e.CompletionState == ActivityInstanceState.Canceled)
                    {
                        LogOutput?.Invoke("[XAML] 工作流已取消");
                    }
                    else if (e.CompletionState == ActivityInstanceState.Faulted)
                    {
                        workflowException = e.TerminationException;
                        LogOutput?.Invoke($"[XAML] 工作流出错: {e.TerminationException?.Message}");
                    }
                };

                workflowApp.Aborted = (e) =>
                {
                    completedEvent.Set();
                    workflowException = e.Reason;
                    LogOutput?.Invoke($"[XAML] 工作流中止: {e.Reason?.Message}");
                };

                workflowApp.OnUnhandledException = (e) =>
                {
                    workflowException = e.UnhandledException;
                    LogOutput?.Invoke($"[XAML] 未处理异常: {e.UnhandledException?.Message}");
                    return UnhandledExceptionAction.Terminate;
                };

                // 运行工作流
                LogOutput?.Invoke("[XAML] 开始执行...");
                workflowApp.Run();

                // 等待完成 (添加超时防止永久阻塞)
                bool completed = completedEvent.WaitOne(TimeSpan.FromSeconds(30));

                if (!completed)
                {
                    LogOutput?.Invoke("[XAML] 工作流执行超时 (30秒)");
                    workflowApp.Abort("执行超时");
                    throw new TimeoutException("工作流执行超时");
                }

                if (workflowException != null)
                {
                    throw workflowException;
                }

                // 收集输出结果
                result["__success"] = true;
                if (outputs != null)
                {
                    foreach (var kvp in outputs)
                    {
                        result[kvp.Key] = kvp.Value;
                        LogOutput?.Invoke($"[XAML] 输出: {kvp.Key} = {kvp.Value}");
                    }
                }

                WorkflowCompleted?.Invoke(result);
            }
            catch (Exception ex)
            {
                result["__success"] = false;
                result["__error"] = ex.Message;
                result["__stackTrace"] = ex.StackTrace;
                LogOutput?.Invoke($"[XAML] 错误: {ex.Message}");
                WorkflowError?.Invoke(ex);
            }

            return result;
        }

        /// <summary>
        /// 异步执行 XAML 工作流
        /// </summary>
        public void ExecuteWorkflowAsync(string xamlFilePath, Dictionary<string, object> inputArguments = null)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                ExecuteWorkflow(xamlFilePath, inputArguments);
            });
        }

        /// <summary>
        /// 从 XAML 文件加载工作流
        /// </summary>
        private Activity LoadWorkflowFromXaml(string xamlFilePath)
        {
            try
            {
                // 方法 1: 使用 ActivityXamlServices
                using (var reader = new StreamReader(xamlFilePath))
                {
                    var activity = ActivityXamlServices.Load(reader.BaseStream);
                    return activity;
                }
            }
            catch (Exception ex)
            {
                LogOutput?.Invoke($"[XAML] ActivityXamlServices 加载失败: {ex.Message}");

                // 方法 2: 尝试使用 XamlServices
                try
                {
                    var obj = XamlServices.Load(xamlFilePath);
                    if (obj is Activity activity)
                    {
                        return activity;
                    }

                    LogOutput?.Invoke($"[XAML] 加载的对象类型: {obj?.GetType().FullName}");
                }
                catch (Exception ex2)
                {
                    LogOutput?.Invoke($"[XAML] XamlServices 加载也失败: {ex2.Message}");
                }

                throw;
            }
        }

        /// <summary>
        /// 验证 XAML 工作流
        /// </summary>
        public ValidationResults ValidateWorkflow(string xamlFilePath)
        {
            var results = new ValidationResults();

            try
            {
                var workflow = LoadWorkflowFromXaml(xamlFilePath);
                if (workflow != null)
                {
                    var validationResults = ActivityValidationServices.Validate(workflow);

                    results.IsValid = validationResults.Errors.Count == 0;

                    foreach (var error in validationResults.Errors)
                    {
                        results.Errors.Add($"{error.PropertyName}: {error.Message}");
                    }

                    foreach (var warning in validationResults.Warnings)
                    {
                        results.Warnings.Add($"{warning.PropertyName}: {warning.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                results.IsValid = false;
                results.Errors.Add($"加载失败: {ex.Message}");
            }

            return results;
        }

        /// <summary>
        /// 获取工作流的参数信息
        /// </summary>
        public WorkflowArgumentsInfo GetWorkflowArguments(string xamlFilePath)
        {
            var info = new WorkflowArgumentsInfo();

            try
            {
                var workflow = LoadWorkflowFromXaml(xamlFilePath);
                if (workflow is DynamicActivity dynamicActivity)
                {
                    foreach (var prop in dynamicActivity.Properties)
                    {
                        var argInfo = new WorkflowArgumentInfo
                        {
                            Name = prop.Name,
                            Type = prop.Type,
                            Direction = GetArgumentDirection(prop.Type)
                        };
                        info.Arguments.Add(argInfo);
                    }
                }
            }
            catch (Exception ex)
            {
                info.Error = ex.Message;
            }

            return info;
        }

        private ArgumentDirection GetArgumentDirection(Type type)
        {
            if (type.IsGenericType)
            {
                var genericDef = type.GetGenericTypeDefinition();
                if (genericDef == typeof(InArgument<>))
                    return ArgumentDirection.In;
                if (genericDef == typeof(OutArgument<>))
                    return ArgumentDirection.Out;
                if (genericDef == typeof(InOutArgument<>))
                    return ArgumentDirection.InOut;
            }
            return ArgumentDirection.In;
        }
    }

    /// <summary>
    /// 工作流验证结果
    /// </summary>
    public class ValidationResults
    {
        public bool IsValid { get; set; } = true;
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// 工作流参数信息
    /// </summary>
    public class WorkflowArgumentsInfo
    {
        public List<WorkflowArgumentInfo> Arguments { get; set; } = new List<WorkflowArgumentInfo>();
        public string Error { get; set; }
    }

    /// <summary>
    /// 单个参数信息
    /// </summary>
    public class WorkflowArgumentInfo
    {
        public string Name { get; set; }
        public Type Type { get; set; }
        public ArgumentDirection Direction { get; set; }

        public string TypeDisplayName
        {
            get
            {
                if (Type == null) return "object";

                if (Type.IsGenericType)
                {
                    var genericArgs = Type.GetGenericArguments();
                    if (genericArgs.Length > 0)
                    {
                        return GetSimpleTypeName(genericArgs[0]);
                    }
                }

                return GetSimpleTypeName(Type);
            }
        }

        private string GetSimpleTypeName(Type type)
        {
            if (type == typeof(string)) return "string";
            if (type == typeof(int)) return "int";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(double)) return "double";
            if (type == typeof(decimal)) return "decimal";
            if (type == typeof(DateTime)) return "DateTime";
            return type.Name;
        }
    }

    /// <summary>
    /// 参数方向
    /// </summary>
    public enum ArgumentDirection
    {
        In,
        Out,
        InOut
    }
}
