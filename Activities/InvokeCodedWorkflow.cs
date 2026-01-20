using ActiproRoslynPOC.Models;
using ActiproRoslynPOC.Services;
using System;
using System.Activities;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace ActiproRoslynPOC.Activities
{
    /// <summary>
    /// 调用 C# 编写的工作流（CodedWorkflow）
    /// 用于在 XAML 工作流中调用 .cs 文件定义的工作流
    /// </summary>
    public class InvokeCodedWorkflow : CodeActivity
    {
        /// <summary>
        /// C# 工作流文件路径（相对于项目根目录）
        /// </summary>
        [Category("输入")]
        [Description("C# 工作流文件路径（相对路径或绝对路径）")]
        [RequiredArgument]
        public InArgument<string> WorkflowFilePath { get; set; }

        /// <summary>
        /// 工作流输入参数（字典格式）
        /// </summary>
        [Category("输入")]
        [Description("传递给工作流的参数（键值对）")]
        public InArgument<Dictionary<string, object>> Arguments { get; set; }

        /// <summary>
        /// 工作流执行结果
        /// </summary>
        [Category("输出")]
        [Description("工作流的执行结果")]
        public OutArgument<object> Result { get; set; }

        /// <summary>
        /// 执行活动
        /// </summary>
        protected override void Execute(CodeActivityContext context)
        {
            // 获取参数
            var filePath = WorkflowFilePath.Get(context);
            var arguments = Arguments.Get(context) ?? new Dictionary<string, object>();

            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("工作流文件路径不能为空", nameof(WorkflowFilePath));
            }

            // 转换为绝对路径
            if (!Path.IsPathRooted(filePath))
            {
                // 尝试从当前工作目录解析
                var projectDir = Directory.GetCurrentDirectory();
                filePath = Path.Combine(projectDir, filePath);
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"工作流文件不存在: {filePath}");
            }

            try
            {
                // 获取工作流目录（文件所在目录）
                var workflowDirectory = Path.GetDirectoryName(filePath);

                // 使用 WorkflowInvocationService 调用工作流
                var invocationService = new WorkflowInvocationService(workflowDirectory);

                // 执行工作流
                var result = invocationService.RunWorkflow(filePath, arguments);

                // 检查执行是否成功
                if (result.ContainsKey("__success") && !(bool)result["__success"])
                {
                    var errorMsg = result.ContainsKey("__error") ? result["__error"].ToString() : "未知错误";
                    throw new InvalidOperationException(errorMsg);
                }

                // 设置输出结果（从结果字典中提取）
                if (Result != null)
                {
                    // 尝试获取 "__result" 键的值，如果不存在则返回整个字典
                    var outputValue = result.ContainsKey("__result") ? result["__result"] : result;
                    Result.Set(context, outputValue);
                }

                Console.WriteLine($"[InvokeCodedWorkflow] 已执行: {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"调用工作流失败: {ex.Message}", ex);
            }
        }
    }
}
