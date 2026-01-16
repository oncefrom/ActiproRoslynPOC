using ActiproRoslynPOC.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ActiproRoslynPOC.Models
{
    /// <summary>
    /// 工作流基类
    /// 支持 [Workflow] 特性标记入口方法，支持动态参数和返回值
    /// </summary>
    public abstract class CodedWorkflowBase : IServicesProvider
    {
        public Dictionary<string, object> Arguments { get; set; } = new Dictionary<string, object>();
        public object Result { get; protected set; }
        public event EventHandler<string> LogEvent;

        /// <summary>
        /// 工作流服务集合（提供 WorkflowInvocationService 等服务）
        /// </summary>
        public WorkflowServices Services { get; set; }

        /// <summary>
        /// 简化访问：services（小写，兼容 UiPath 风格）
        /// </summary>
        protected WorkflowServices services => Services;

        /// <summary>
        /// 执行工作流入口
        /// 自动查找带 [Workflow] 特性的方法并调用
        /// </summary>
        public virtual void Execute()
        {
            // 查找带有 [Workflow] 特性的方法
            var workflowMethods = this.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<WorkflowAttribute>() != null)
                .ToList();

            if (workflowMethods.Count == 0)
            {
                throw new InvalidOperationException(
                    $"类 {this.GetType().Name} 中没有找到带 [Workflow] 特性的方法。" +
                    "请在入口方法上添加 [Workflow] 特性。");
            }

            if (workflowMethods.Count > 1)
            {
                throw new InvalidOperationException(
                    $"类 {this.GetType().Name} 中有多个带 [Workflow] 特性的方法: " +
                    $"{string.Join(", ", workflowMethods.Select(m => m.Name))}。" +
                    "每个文件只允许有一个 [Workflow] 入口方法。");
            }

            var entryMethod = workflowMethods[0];
            var parameters = entryMethod.GetParameters();

            // 构建参数数组
            var args = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                if (Arguments.TryGetValue(param.Name, out var value))
                {
                    args[i] = ConvertArgument(value, param.ParameterType);
                }
                else if (param.HasDefaultValue)
                {
                    args[i] = param.DefaultValue;
                }
                else
                {
                    args[i] = param.ParameterType.IsValueType
                        ? Activator.CreateInstance(param.ParameterType)
                        : null;
                }
            }

            // 调用入口方法并保存返回值
            Result = entryMethod.Invoke(this, args);
        }

        /// <summary>
        /// 转换参数类型
        /// </summary>
        private object ConvertArgument(object value, Type targetType)
        {
            if (value == null)
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

            if (targetType.IsAssignableFrom(value.GetType()))
                return value;

            // 尝试类型转换
            try
            {
                return Convert.ChangeType(value, targetType);
            }
            catch
            {
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
            }
        }

        protected void Log(string message)
        {
            // 优先使用全局事件 (避免重复输出)
            // 时间戳由 AppendOutput 统一添加
            GlobalLogManager.Log(message);

            // 保留实例级别事件 (向后兼容,但不添加时间戳,避免重复)
            // 如果有订阅者,也会收到通知
            LogEvent?.Invoke(this, message);
        }

        protected T GetArgument<T>(string name, T defaultValue = default)
        {
            if (Arguments.TryGetValue(name, out var value))
                return (T)value;
            return defaultValue;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class WorkflowAttribute : Attribute
    {
        public string Name { get; set; }
        public string Description { get; set; }
    }
}