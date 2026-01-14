using System;

namespace ActiproRoslynPOC.Services
{
    /// <summary>
    /// 全局日志管理器 - 解决跨实例 Log 输出问题
    /// 所有 CodedWorkflowBase 实例共享此管理器
    /// </summary>
    public static class GlobalLogManager
    {
        /// <summary>
        /// 全局日志事件 - 所有 Log 调用都会触发此事件
        /// </summary>
        public static event Action<string> LogReceived;

        /// <summary>
        /// 记录日志
        /// </summary>
        public static void Log(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            // 只触发全局事件,不再调用 Console.WriteLine
            // 因为 Console 已经被重定向,会导致重复输出
            LogReceived?.Invoke(message);
        }

        /// <summary>
        /// 清除所有订阅者 (调试结束时调用)
        /// </summary>
        public static void ClearSubscribers()
        {
            LogReceived = null;
        }
    }
}
