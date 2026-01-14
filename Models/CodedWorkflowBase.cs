using ActiproRoslynPOC.Services;
using System;
using System.Collections.Generic;

namespace ActiproRoslynPOC.Models
{
    public abstract class CodedWorkflowBase
    {
        public Dictionary<string, object> Arguments { get; set; } = new Dictionary<string, object>();
        public object Result { get; protected set; }
        public event EventHandler<string> LogEvent;

        public abstract void Execute();

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