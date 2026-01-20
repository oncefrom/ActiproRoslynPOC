using System;
using System.Collections.Generic;

namespace ActiproRoslynPOC.Models
{
    /// <summary>
    /// 项目配置 - 类似 UiPath 的 project.json
    /// </summary>
    public class ProjectConfig
    {
        /// <summary>
        /// 项目名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 项目描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 项目版本
        /// </summary>
        public string Version { get; set; } = "1.0.0";

        /// <summary>
        /// 主工作流入口（可选，.cs 或 .xaml 文件路径）
        /// </summary>
        public string Main { get; set; }

        /// <summary>
        /// 项目依赖的 DLL 列表（相对路径）
        /// </summary>
        public List<string> Dependencies { get; set; } = new List<string>();

        /// <summary>
        /// 项目创建时间
        /// </summary>
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        /// <summary>
        /// 最后修改时间
        /// </summary>
        public DateTime LastModifiedDate { get; set; } = DateTime.Now;

        /// <summary>
        /// 项目类型（CS/XAML/Mixed）
        /// </summary>
        public string ProjectType { get; set; } = "Mixed";

        /// <summary>
        /// 自定义元数据
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }
}
