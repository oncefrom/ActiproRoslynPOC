using ActiproRoslynPOC.Services;
using System;
using System.Activities;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

namespace ActiproRoslynPOC.Activities
{
    /// <summary>
    /// 调用 XAML 工作流
    /// 用于在 XAML 工作流中调用其他 .xaml 工作流文件
    /// </summary>
    public class InvokeWorkflow : CodeActivity
    {
        /// <summary>
        /// XAML 工作流文件路径（相对于项目根目录）
        /// </summary>
        [Category("输入")]
        [Description("XAML 工作流文件路径（相对路径或绝对路径）")]
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
                var projectDir = Directory.GetCurrentDirectory();
                filePath = Path.Combine(projectDir, filePath);
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"工作流文件不存在: {filePath}");
            }

            try
            {
                // 使用 XamlWorkflowService 执行 XAML 工作流
                var xamlService = new XamlWorkflowService();
                var result = xamlService.ExecuteWorkflow(filePath, arguments);

                // 设置输出结果
                if (Result != null)
                {
                    Result.Set(context, result);
                }

                Console.WriteLine($"[InvokeWorkflow] 已执行 XAML 工作流: {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"调用 XAML 工作流失败: {ex.Message}", ex);
            }
        }
    }
}
