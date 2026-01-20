using ActiproRoslynPOC.Models;
using System;
using System.IO;
using System.Text.Json;

namespace ActiproRoslynPOC.Services
{
    /// <summary>
    /// 项目管理服务 - 类似 UiPath Studio 的项目管理
    /// </summary>
    public class ProjectService
    {
        private const string PROJECT_FILE_NAME = "project.json";
        private const string WORKFLOWS_FOLDER = "Workflows";
        private const string DEPENDENCIES_FOLDER = "Dependencies";

        /// <summary>
        /// 创建新项目
        /// </summary>
        public static ProjectConfig CreateProject(string projectPath, string projectName, string description = "")
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                throw new ArgumentException("项目路径不能为空", nameof(projectPath));

            if (string.IsNullOrWhiteSpace(projectName))
                throw new ArgumentException("项目名称不能为空", nameof(projectName));

            // 创建项目目录
            Directory.CreateDirectory(projectPath);

            // 创建子目录
            Directory.CreateDirectory(Path.Combine(projectPath, WORKFLOWS_FOLDER));
            Directory.CreateDirectory(Path.Combine(projectPath, DEPENDENCIES_FOLDER));

            // 创建项目配置
            var config = new ProjectConfig
            {
                Name = projectName,
                Description = description,
                Version = "1.0.0",
                CreatedDate = DateTime.Now,
                LastModifiedDate = DateTime.Now
            };

            // 保存项目配置
            SaveProjectConfig(projectPath, config);

            return config;
        }

        /// <summary>
        /// 打开现有项目
        /// </summary>
        public static ProjectConfig OpenProject(string projectPath)
        {
            if (!Directory.Exists(projectPath))
                throw new DirectoryNotFoundException($"项目目录不存在: {projectPath}");

            var configPath = Path.Combine(projectPath, PROJECT_FILE_NAME);
            if (!File.Exists(configPath))
                throw new FileNotFoundException($"项目配置文件不存在: {configPath}");

            var json = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<ProjectConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            });

            return config;
        }

        /// <summary>
        /// 保存项目配置
        /// </summary>
        public static void SaveProjectConfig(string projectPath, ProjectConfig config)
        {
            config.LastModifiedDate = DateTime.Now;

            var configPath = Path.Combine(projectPath, PROJECT_FILE_NAME);
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            File.WriteAllText(configPath, json);
        }

        /// <summary>
        /// 检查是否是有效的项目目录
        /// </summary>
        public static bool IsValidProject(string projectPath)
        {
            if (!Directory.Exists(projectPath))
                return false;

            var configPath = Path.Combine(projectPath, PROJECT_FILE_NAME);
            return File.Exists(configPath);
        }

        /// <summary>
        /// 获取工作流目录路径
        /// </summary>
        public static string GetWorkflowsDirectory(string projectPath)
        {
            return Path.Combine(projectPath, WORKFLOWS_FOLDER);
        }

        /// <summary>
        /// 获取依赖目录路径
        /// </summary>
        public static string GetDependenciesDirectory(string projectPath)
        {
            return Path.Combine(projectPath, DEPENDENCIES_FOLDER);
        }

        /// <summary>
        /// 创建新的 CS 工作流文件
        /// </summary>
        public static void CreateCsWorkflow(string projectPath, string workflowName, string targetDirectory = null)
        {
            var workflowsDir = string.IsNullOrEmpty(targetDirectory)
                ? GetWorkflowsDirectory(projectPath)
                : targetDirectory;
            var filePath = Path.Combine(workflowsDir, $"{workflowName}.cs");

            if (File.Exists(filePath))
                throw new InvalidOperationException($"工作流文件已存在: {workflowName}.cs");

            var template = @"using System;
using ActiproRoslynPOC.Models;

public class " + workflowName + @" : CodedWorkflowBase
{
    [Workflow(Name = """ + workflowName + @""", Description = ""工作流描述"")]
    public override void Execute()
    {
        // 在这里编写工作流逻辑
        Log(""工作流开始执行..."");

        // TODO: 实现您的业务逻辑

        Result = ""执行完成"";
    }
}
";

            File.WriteAllText(filePath, template);
        }

        /// <summary>
        /// 创建新的 XAML 工作流文件
        /// </summary>
        public static void CreateXamlWorkflow(string projectPath, string workflowName, string targetDirectory = null)
        {
            var workflowsDir = string.IsNullOrEmpty(targetDirectory)
                ? GetWorkflowsDirectory(projectPath)
                : targetDirectory;
            var filePath = Path.Combine(workflowsDir, $"{workflowName}.xaml");

            if (File.Exists(filePath))
                throw new InvalidOperationException($"工作流文件已存在: {workflowName}.xaml");

            var template = @"<Activity x:Class=""" + workflowName + @"""
 xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities""
 xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
    <Sequence DisplayName=""" + workflowName + @""">
        <Sequence.Variables>
            <Variable x:TypeArguments=""x:String"" Name=""message"" />
        </Sequence.Variables>
        <!-- 在这里添加活动 -->
    </Sequence>
</Activity>";

            File.WriteAllText(filePath, template);
        }
    }
}
