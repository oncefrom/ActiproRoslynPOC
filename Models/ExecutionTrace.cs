using System;
using System.Collections.Generic;

namespace ActiproRoslynPOC.Models
{
    /// <summary>
    /// 执行跟踪记录 - 记录某一行执行时的快照
    /// </summary>
    public class ExecutionTrace
    {
        /// <summary>
        /// 执行的行号
        /// </summary>
        public int LineNumber { get; set; }

        /// <summary>
        /// 执行时的变量快照
        /// </summary>
        public Dictionary<string, object> VariableSnapshot { get; set; }

        /// <summary>
        /// 执行时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 跟踪索引（第几步）
        /// </summary>
        public int TraceIndex { get; set; }

        public ExecutionTrace()
        {
            VariableSnapshot = new Dictionary<string, object>();
            Timestamp = DateTime.Now;
        }
    }
}
