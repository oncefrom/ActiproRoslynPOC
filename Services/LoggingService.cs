using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ActiproRoslynPOC.Services
{
    /// <summary>
    /// 日志级别
    /// </summary>
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        Fatal = 4
    }

    /// <summary>
    /// 统一的日志记录服务
    /// 提供多级别日志记录，支持控制台、文件和事件输出
    /// </summary>
    public class LoggingService
    {
        private static LoggingService _instance;
        private static readonly object _lock = new object();

        private readonly StringBuilder _logBuffer = new StringBuilder();
        private readonly object _logLock = new object();
        private LogLevel _minimumLogLevel;
        private bool _enableFileLogging;
        private string _logFilePath;

        /// <summary>
        /// 获取单例实例
        /// </summary>
        public static LoggingService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new LoggingService();
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// 日志消息事件（用于UI显示）
        /// </summary>
        public event Action<string, LogLevel> LogMessageReceived;

        private LoggingService()
        {
            // 从配置读取日志级别
            _minimumLogLevel = ConfigurationService.Instance.EnableVerboseLogging
                ? LogLevel.Debug
                : LogLevel.Info;

            // 初始化文件日志
            _enableFileLogging = false; // 默认关闭文件日志
            InitializeFileLogging();
        }

        /// <summary>
        /// 初始化文件日志
        /// </summary>
        private void InitializeFileLogging()
        {
            if (!_enableFileLogging)
                return;

            try
            {
                var logDirectory = Path.Combine(
                    ConfigurationService.Instance.ApplicationRootDirectory,
                    "Logs"
                );

                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                _logFilePath = Path.Combine(logDirectory, $"ActiproRPA_{timestamp}.log");

                Log(LogLevel.Info, "LoggingService", "日志服务已初始化");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LoggingService] 初始化文件日志失败: {ex.Message}");
                _enableFileLogging = false;
            }
        }

        /// <summary>
        /// 设置最小日志级别
        /// </summary>
        public void SetMinimumLogLevel(LogLevel level)
        {
            _minimumLogLevel = level;
        }

        /// <summary>
        /// 启用或禁用文件日志
        /// </summary>
        public void SetFileLogging(bool enabled)
        {
            _enableFileLogging = enabled;
            if (enabled && string.IsNullOrEmpty(_logFilePath))
            {
                InitializeFileLogging();
            }
        }

        /// <summary>
        /// 记录日志
        /// </summary>
        public void Log(LogLevel level, string category, string message)
        {
            if (level < _minimumLogLevel)
                return;

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var logEntry = $"[{timestamp}] [{level,-7}] [{category}] {message}";

            lock (_logLock)
            {
                // 写入缓冲区
                _logBuffer.AppendLine(logEntry);

                // 输出到调试窗口
                Console.WriteLine(logEntry);

                // 写入文件
                if (_enableFileLogging && !string.IsNullOrEmpty(_logFilePath))
                {
                    try
                    {
                        File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[LoggingService] 写入日志文件失败: {ex.Message}");
                    }
                }

                // 触发事件（用于UI显示）
                LogMessageReceived?.Invoke(GetFormattedMessage(level, category, message), level);
            }
        }

        /// <summary>
        /// 记录异常
        /// </summary>
        public void LogException(LogLevel level, string category, string message, Exception exception)
        {
            var exceptionDetails = FormatException(exception);
            Log(level, category, $"{message}\n{exceptionDetails}");
        }

        /// <summary>
        /// 格式化异常信息
        /// </summary>
        private string FormatException(Exception exception)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"异常类型: {exception.GetType().FullName}");
            sb.AppendLine($"异常消息: {exception.Message}");

            if (!string.IsNullOrEmpty(exception.StackTrace))
            {
                sb.AppendLine("堆栈跟踪:");
                sb.AppendLine(exception.StackTrace);
            }

            // 处理内部异常
            if (exception.InnerException != null)
            {
                sb.AppendLine("\n内部异常:");
                sb.AppendLine(FormatException(exception.InnerException));
            }

            return sb.ToString();
        }

        /// <summary>
        /// 获取格式化的消息（用于UI显示）
        /// </summary>
        private string GetFormattedMessage(LogLevel level, string category, string message)
        {
            var prefix = GetLevelPrefix(level);
            return $"{prefix} [{category}] {message}";
        }

        /// <summary>
        /// 获取日志级别前缀
        /// </summary>
        private string GetLevelPrefix(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Debug:
                    return "[调试]";
                case LogLevel.Info:
                    return "[信息]";
                case LogLevel.Warning:
                    return "[警告]";
                case LogLevel.Error:
                    return "[错误]";
                case LogLevel.Fatal:
                    return "[严重]";
                default:
                    return "[未知]";
            }
        }

        #region 便捷方法

        /// <summary>
        /// 记录调试信息
        /// </summary>
        public void Debug(string category, string message)
        {
            Log(LogLevel.Debug, category, message);
        }

        /// <summary>
        /// 记录一般信息
        /// </summary>
        public void Info(string category, string message)
        {
            Log(LogLevel.Info, category, message);
        }

        /// <summary>
        /// 记录警告信息
        /// </summary>
        public void Warning(string category, string message)
        {
            Log(LogLevel.Warning, category, message);
        }

        /// <summary>
        /// 记录错误信息
        /// </summary>
        public void Error(string category, string message)
        {
            Log(LogLevel.Error, category, message);
        }

        /// <summary>
        /// 记录错误信息（带异常）
        /// </summary>
        public void Error(string category, string message, Exception exception)
        {
            LogException(LogLevel.Error, category, message, exception);
        }

        /// <summary>
        /// 记录严重错误
        /// </summary>
        public void Fatal(string category, string message)
        {
            Log(LogLevel.Fatal, category, message);
        }

        /// <summary>
        /// 记录严重错误（带异常）
        /// </summary>
        public void Fatal(string category, string message, Exception exception)
        {
            LogException(LogLevel.Fatal, category, message, exception);
        }

        #endregion

        /// <summary>
        /// 获取所有日志内容
        /// </summary>
        public string GetAllLogs()
        {
            lock (_logLock)
            {
                return _logBuffer.ToString();
            }
        }

        /// <summary>
        /// 清空日志缓冲区
        /// </summary>
        public void ClearLogs()
        {
            lock (_logLock)
            {
                _logBuffer.Clear();
            }
        }

        /// <summary>
        /// 获取日志文件路径
        /// </summary>
        public string GetLogFilePath()
        {
            return _logFilePath;
        }
    }
}
