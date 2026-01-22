using System;
using System.Configuration;
using System.IO;

namespace ActiproRoslynPOC.Services
{
    /// <summary>
    /// 统一的配置管理服务
    /// 提供类型安全的配置访问接口
    /// </summary>
    public class ConfigurationService
    {
        private static ConfigurationService _instance;
        private static readonly object _lock = new object();

        /// <summary>
        /// 获取单例实例
        /// </summary>
        public static ConfigurationService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new ConfigurationService();
                        }
                    }
                }
                return _instance;
            }
        }

        private ConfigurationService()
        {
            // 初始化配置
            LoadConfiguration();
        }

        #region 配置属性

        /// <summary>
        /// 默认工作流项目目录
        /// </summary>
        public string DefaultWorkflowDirectory { get; private set; }

        /// <summary>
        /// 应用程序根目录
        /// </summary>
        public string ApplicationRootDirectory { get; private set; }

        /// <summary>
        /// 是否启用详细日志
        /// </summary>
        public bool EnableVerboseLogging { get; private set; }

        /// <summary>
        /// 编译超时时间（毫秒）
        /// </summary>
        public int CompilationTimeoutMs { get; private set; }

        /// <summary>
        /// 是否启用调试符号（PDB）
        /// </summary>
        public bool EnableDebugSymbols { get; private set; }

        #endregion

        /// <summary>
        /// 加载配置
        /// </summary>
        private void LoadConfiguration()
        {
            // 设置应用程序根目录
            ApplicationRootDirectory = AppDomain.CurrentDomain.BaseDirectory;

            // 读取默认工作流目录
            // 优先级：环境变量 > App.config > 计算的默认值
            DefaultWorkflowDirectory = GetWorkflowDirectory();

            // 读取其他配置
            EnableVerboseLogging = GetConfigBool("EnableVerboseLogging", false);
            CompilationTimeoutMs = GetConfigInt("CompilationTimeoutMs", 30000);
            EnableDebugSymbols = GetConfigBool("EnableDebugSymbols", true);
        }

        /// <summary>
        /// 获取工作流目录
        /// </summary>
        private string GetWorkflowDirectory()
        {
            // 1. 尝试从环境变量读取
            var envPath = Environment.GetEnvironmentVariable("ACTIPRO_WORKFLOW_DIR");
            if (!string.IsNullOrEmpty(envPath) && Directory.Exists(envPath))
            {
                return envPath;
            }

            // 2. 尝试从 App.config 读取
            var configPath = ConfigurationManager.AppSettings["WorkflowDirectory"];
            if (!string.IsNullOrEmpty(configPath))
            {
                // 支持相对路径
                if (!Path.IsPathRooted(configPath))
                {
                    configPath = Path.Combine(ApplicationRootDirectory, configPath);
                }

                if (Directory.Exists(configPath))
                {
                    return Path.GetFullPath(configPath);
                }
            }

            // 3. 使用默认计算路径：应用程序所在目录的上级目录的 TestWorkflows
            var defaultPath = Path.Combine(
                Path.GetDirectoryName(Path.GetDirectoryName(ApplicationRootDirectory)),
                "TestWorkflows"
            );

            // 4. 如果计算的路径不存在，尝试在应用程序目录下创建
            if (!Directory.Exists(defaultPath))
            {
                // 尝试在应用程序目录下的 TestWorkflows
                defaultPath = Path.Combine(ApplicationRootDirectory, "TestWorkflows");

                // 如果还不存在，创建它
                if (!Directory.Exists(defaultPath))
                {
                    try
                    {
                        Directory.CreateDirectory(defaultPath);
                    }
                    catch (Exception ex)
                    {
                        // 记录错误但不抛出异常
                        System.Diagnostics.Debug.WriteLine($"无法创建工作流目录: {ex.Message}");

                        // 返回临时目录作为最后的备选
                        defaultPath = Path.Combine(Path.GetTempPath(), "ActiproWorkflows");
                        Directory.CreateDirectory(defaultPath);
                    }
                }
            }

            return Path.GetFullPath(defaultPath);
        }

        /// <summary>
        /// 从配置中读取布尔值
        /// </summary>
        private bool GetConfigBool(string key, bool defaultValue)
        {
            var value = ConfigurationManager.AppSettings[key];
            if (string.IsNullOrEmpty(value))
            {
                return defaultValue;
            }

            if (bool.TryParse(value, out bool result))
            {
                return result;
            }

            return defaultValue;
        }

        /// <summary>
        /// 从配置中读取整数值
        /// </summary>
        private int GetConfigInt(string key, int defaultValue)
        {
            var value = ConfigurationManager.AppSettings[key];
            if (string.IsNullOrEmpty(value))
            {
                return defaultValue;
            }

            if (int.TryParse(value, out int result))
            {
                return result;
            }

            return defaultValue;
        }

        /// <summary>
        /// 从配置中读取字符串值
        /// </summary>
        private string GetConfigString(string key, string defaultValue)
        {
            var value = ConfigurationManager.AppSettings[key];
            return string.IsNullOrEmpty(value) ? defaultValue : value;
        }

        /// <summary>
        /// 获取指定项目的工作流目录
        /// </summary>
        /// <param name="projectPath">项目路径</param>
        /// <returns>工作流目录</returns>
        public string GetProjectWorkflowDirectory(string projectPath)
        {
            if (string.IsNullOrEmpty(projectPath))
            {
                return DefaultWorkflowDirectory;
            }

            // 如果项目路径是文件，获取其目录
            if (File.Exists(projectPath))
            {
                return Path.GetDirectoryName(projectPath);
            }

            // 如果是目录，直接返回
            if (Directory.Exists(projectPath))
            {
                return projectPath;
            }

            // 否则返回默认目录
            return DefaultWorkflowDirectory;
        }

        /// <summary>
        /// 验证并获取工作流目录
        /// 如果目录不存在，会尝试创建
        /// </summary>
        public string EnsureWorkflowDirectory(string projectPath = null)
        {
            var directory = GetProjectWorkflowDirectory(projectPath);

            if (!Directory.Exists(directory))
            {
                try
                {
                    Directory.CreateDirectory(directory);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"无法创建工作流目录: {directory}",
                        ex
                    );
                }
            }

            return directory;
        }
    }
}
