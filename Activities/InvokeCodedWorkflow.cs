using ActiproRoslynPOC.Models;
using ActiproRoslynPOC.Services;
using Microsoft.VisualBasic.Activities;
using System;
using System.Activities;
using System.Activities.Statements;
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
    public class InvokeCodedWorkflow : NativeActivity
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
        [Description("传递给工作流的输入参数（键值对）")]
        public InArgument<Dictionary<string, object>> Arguments { get; set; }

        /// <summary>
        /// 工作流输出参数绑定（字典格式，Key 是工作流输出参数名，Value 是接收变量名）
        /// </summary>
        [Category("输出")]
        [Description("工作流的输出参数绑定")]
        public InArgument<Dictionary<string, string>> OutputBindings { get; set; }

        /// <summary>
        /// 工作流执行结果（保留，用于获取完整返回）
        /// </summary>
        [Category("输出")]
        [Description("工作流的执行结果（完整的输出字典）")]
        public OutArgument<object> Result { get; set; }

        /// <summary>
        /// 执行活动
        /// </summary>
        protected override void Execute(NativeActivityContext context)
        {
            // 获取参数
            var filePath = WorkflowFilePath.Get(context);
            var arguments = Arguments.Get(context) ?? new Dictionary<string, object>();
            var outputBindings = OutputBindings.Get(context);

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

                // 处理输出参数绑定
                Dictionary<string, object> outputValues = null;
                if (outputBindings != null && outputBindings.Count > 0)
                {
                    // 获取工作流返回的结果
                    object workflowResult = result.ContainsKey("__result") ? result["__result"] : null;

                    // 如果结果是元组，需要解包
                    outputValues = ExtractTupleValues(workflowResult, outputBindings.Keys.ToList());

                    // 执行变量赋值
                    foreach (var binding in outputBindings)
                    {
                        var outputName = binding.Key;
                        var variableName = binding.Value;
                        if (outputValues.ContainsKey(outputName))
                        {
                            var value = outputValues[outputName];
                            //Console.WriteLine($"  {outputName} -> {variableName} = {value}");

                            // 尝试通过反射找到并设置变量值
                            try
                            {
                                bool success = TrySetVariableValue(context, variableName, value);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"赋值失败 [{variableName}]: {ex.Message}");
                            }
                        }
                    }
                }

                // 设置输出结果（从结果字典中提取）
                if (Result != null)
                {
                    var outputValue = result.ContainsKey("__result") ? result["__result"] : result;
                    Result.Set(context, outputValue);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"调用工作流失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 从元组结果中提取值
        /// </summary>
        private Dictionary<string, object> ExtractTupleValues(object tupleResult, List<string> outputNames)
        {
            var values = new Dictionary<string, object>();

            if (tupleResult == null)
                return values;

            var tupleType = tupleResult.GetType();

            // 检查是否是 ValueTuple
            if (tupleType.IsValueType && tupleType.FullName?.StartsWith("System.ValueTuple") == true)
            {
                // 获取元组的字段 (Item1, Item2, ...)
                var fields = tupleType.GetFields();
                for (int i = 0; i < fields.Length && i < outputNames.Count; i++)
                {
                    var fieldValue = fields[i].GetValue(tupleResult);
                    values[outputNames[i]] = fieldValue;
                }
            }
            else
            {
                // 如果不是元组，且只有一个输出参数，直接使用结果
                if (outputNames.Count == 1)
                {
                    values[outputNames[0]] = tupleResult;
                }
            }

            return values;
        }

        /// <summary>
        /// 使用 DataContext 安全地设置变量值
        /// </summary>
        private bool TrySetVariableValue(NativeActivityContext context, string variableName, object value)
        {
            try
            {
                // 1. 获取当前上下文的属性调度器（包含变量和参数）
                var properties = context.DataContext.GetProperties();

                // 2. 查找匹配名称的属性
                var property = properties[variableName];

                if (property != null)
                {
                    // 3. 检查类型并尝试转换（处理基础类型转换，如 int64 转 int32）
                    object finalValue = value;
                    if (value != null && !property.PropertyType.IsAssignableFrom(value.GetType()))
                    {
                        try
                        {
                            finalValue = Convert.ChangeType(value, property.PropertyType);
                        }
                        catch
                        {
                            // 如果转换失败，尝试保持原样，让 SetValue 抛出更具体的错误
                        }
                    }

                    // 4. 写入值
                    property.SetValue(context.DataContext, finalValue);
                    return true;
                }
            }
            catch
            {
            }
            return false;
        }

    }
}
