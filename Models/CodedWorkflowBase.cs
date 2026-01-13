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
            LogEvent?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] {message}");
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