using ActiproRoslynPOC.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ActiproRoslynPOC.Services
{
    /// <summary>
    /// 工作流调用服务 - 支持在代码中调用其他工作流
    /// 类似 UiPath 的 WorkflowInvocationService
    /// </summary>
    public class WorkflowInvocationService
    {
        private readonly string _workflowDirectory;
        private readonly RoslynCompilerService _compiler;
        private readonly Dictionary<string, Assembly> _compiledAssemblies = new Dictionary<string, Assembly>();

        public WorkflowInvocationService(string workflowDirectory)
        {
            _workflowDirectory = workflowDirectory;
            _compiler = new RoslynCompilerService();
        }

        /// <summary>
        /// 运行指定的工作流文件
        /// </summary>
        /// <param name="workflowPath">工作流文件路径（相对于工作流目录，如 "SubWorkflow.cs"）</param>
        /// <param name="arguments">传递给工作流的参数</param>
        /// <returns>工作流的返回结果</returns>
        public Dictionary<string, object> RunWorkflow(string workflowPath, Dictionary<string, object> arguments = null)
        {
            var result = new Dictionary<string, object>();

            try
            {
                // 解析完整路径
                var fullPath = Path.IsPathRooted(workflowPath)
                    ? workflowPath
                    : Path.Combine(_workflowDirectory, workflowPath);

                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException($"工作流文件不存在: {fullPath}");
                }

                var extension = Path.GetExtension(fullPath).ToLowerInvariant();

                switch (extension)
                {
                    case ".cs":
                        result = RunCodedWorkflow(fullPath, arguments);
                        break;

                    case ".xaml":
                        result = RunXamlWorkflow(fullPath, arguments);
                        break;

                    default:
                        throw new NotSupportedException($"不支持的工作流类型: {extension}");
                }
            }
            catch (Exception ex)
            {
                result["__error"] = ex.Message;
                result["__success"] = false;
            }

            return result;
        }

        /// <summary>
        /// 运行 C# 编码工作流
        /// </summary>
        private Dictionary<string, object> RunCodedWorkflow(string csFilePath, Dictionary<string, object> arguments)
        {
            var result = new Dictionary<string, object>();

            // 获取所有依赖文件
            var codeFiles = new Dictionary<string, string>();
            var csFiles = Directory.GetFiles(_workflowDirectory, "*.cs", SearchOption.AllDirectories);

            foreach (var filePath in csFiles)
            {
                var fileName = Path.GetFileName(filePath);
                codeFiles[fileName] = File.ReadAllText(filePath);
            }

            // 编译
            var compileResult = _compiler.CompileMultiple(codeFiles);
            if (!compileResult.Success)
            {
                // 构建详细的错误信息
                var errorDetails = new System.Text.StringBuilder();
                errorDetails.AppendLine($"编译失败: {compileResult.ErrorSummary}");

                if (compileResult.Diagnostics != null && compileResult.Diagnostics.Any(d => d.IsError))
                {
                    errorDetails.AppendLine("详细错误:");
                    foreach (var diagnostic in compileResult.Diagnostics.Where(d => d.IsError))
                    {
                        errorDetails.AppendLine($"  [{diagnostic.FileName}:{diagnostic.Line},{diagnostic.Column}] {diagnostic.Message}");
                    }
                }

                throw new InvalidOperationException(errorDetails.ToString());
            }

            // 查找目标工作流类型
            var targetFileName = Path.GetFileNameWithoutExtension(csFilePath);
            var workflowType = compileResult.Assembly.GetTypes()
                .FirstOrDefault(t => t.Name == targetFileName &&
                                    t.IsSubclassOf(typeof(CodedWorkflowBase)) &&
                                    !t.IsAbstract);

            if (workflowType == null)
            {
                // 尝试查找任何带有 [Workflow] 特性的类
                workflowType = compileResult.Assembly.GetTypes()
                    .FirstOrDefault(t => t.IsSubclassOf(typeof(CodedWorkflowBase)) &&
                                        !t.IsAbstract &&
                                        t.GetMethods().Any(m => m.GetCustomAttribute<WorkflowAttribute>() != null));
            }

            if (workflowType == null)
            {
                throw new InvalidOperationException($"在 {csFilePath} 中找不到有效的工作流类");
            }

            // 创建实例
            var workflow = Activator.CreateInstance(workflowType) as CodedWorkflowBase;

            // 设置参数
            if (arguments != null)
            {
                foreach (var kvp in arguments)
                {
                    workflow.Arguments[kvp.Key] = kvp.Value;
                }
            }

            // 设置 Services（递归支持）
            if (workflow is IServicesProvider servicesProvider)
            {
                servicesProvider.Services = new WorkflowServices(_workflowDirectory);
            }

            // 执行
            workflow.Execute();

            // 收集结果
            result["__success"] = true;
            result["__result"] = workflow.Result;

            // 尝试解析元组结果
            if (workflow.Result != null)
            {
                var resultType = workflow.Result.GetType();
                if (resultType.IsGenericType && resultType.Name.StartsWith("ValueTuple"))
                {
                    var fields = resultType.GetFields();
                    foreach (var field in fields)
                    {
                        result[field.Name] = field.GetValue(workflow.Result);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 运行 XAML 工作流 (使用 Windows Workflow Foundation)
        /// </summary>
        private Dictionary<string, object> RunXamlWorkflow(string xamlFilePath, Dictionary<string, object> arguments)
        {
            var xamlService = new XamlWorkflowService();

            // 订阅日志事件
            xamlService.LogOutput += (msg) =>
            {
                GlobalLogManager.Log(msg);
            };

            // 执行 XAML 工作流
            return xamlService.ExecuteWorkflow(xamlFilePath, arguments);
        }

        /// <summary>
        /// 验证 XAML 工作流
        /// </summary>
        public ValidationResults ValidateXamlWorkflow(string workflowPath)
        {
            var fullPath = Path.IsPathRooted(workflowPath)
                ? workflowPath
                : Path.Combine(_workflowDirectory, workflowPath);

            var xamlService = new XamlWorkflowService();
            return xamlService.ValidateWorkflow(fullPath);
        }

        /// <summary>
        /// 获取 XAML 工作流的参数信息
        /// </summary>
        public WorkflowArgumentsInfo GetXamlWorkflowArguments(string workflowPath)
        {
            var fullPath = Path.IsPathRooted(workflowPath)
                ? workflowPath
                : Path.Combine(_workflowDirectory, workflowPath);

            var xamlService = new XamlWorkflowService();
            return xamlService.GetWorkflowArguments(fullPath);
        }
    }

    /// <summary>
    /// 工作流服务集合
    /// </summary>
    public class WorkflowServices
    {
        public WorkflowInvocationService WorkflowInvocationService { get; }

        public WorkflowServices(string workflowDirectory)
        {
            WorkflowInvocationService = new WorkflowInvocationService(workflowDirectory);
        }
    }

    /// <summary>
    /// 服务提供者接口
    /// </summary>
    public interface IServicesProvider
    {
        WorkflowServices Services { get; set; }
    }
}
