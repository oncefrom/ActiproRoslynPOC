using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;

namespace ActiproRoslynPOC.Models
{
    /// <summary>
    /// 文件树节点 - 用于项目资源管理器
    /// </summary>
    public class FileTreeNode : INotifyPropertyChanged
    {
        private bool _isExpanded;
        private bool _isSelected;

        public string Name { get; set; }
        public string FullPath { get; set; }
        public FileTreeNodeType NodeType { get; set; }
        public ObservableCollection<FileTreeNode> Children { get; set; } = new ObservableCollection<FileTreeNode>();

        public FileTreeNode Parent { get; set; }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged(nameof(IsExpanded));
                }
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        /// <summary>
        /// 获取图标（根据文件类型）
        /// </summary>
        public string Icon
        {
            get
            {
                switch (NodeType)
                {
                    case FileTreeNodeType.Project:
                        return "📦";
                    case FileTreeNodeType.Folder:
                        return IsExpanded ? "📂" : "📁";
                    case FileTreeNodeType.CsFile:
                        return "📄";
                    case FileTreeNodeType.XamlFile:
                        return "📋";
                    case FileTreeNodeType.JsonFile:
                        return "⚙️";
                    case FileTreeNodeType.DllFile:
                        return "📚";
                    default:
                        return "📄";
                }
            }
        }

        /// <summary>
        /// 显示名称（带图标）
        /// </summary>
        public string DisplayName => $"{Icon} {Name}";

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// 从文件系统路径创建树节点
        /// </summary>
        public static FileTreeNode FromPath(string path, FileTreeNode parent = null)
        {
            var node = new FileTreeNode
            {
                FullPath = path,
                Name = Path.GetFileName(path) ?? path,
                Parent = parent
            };

            if (Directory.Exists(path))
            {
                node.NodeType = parent == null ? FileTreeNodeType.Project : FileTreeNodeType.Folder;

                // 加载子目录和文件
                try
                {
                    foreach (var dir in Directory.GetDirectories(path))
                    {
                        node.Children.Add(FromPath(dir, node));
                    }

                    foreach (var file in Directory.GetFiles(path))
                    {
                        node.Children.Add(FromPath(file, node));
                    }
                }
                catch
                {
                    // 忽略权限错误
                }
            }
            else if (File.Exists(path))
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();

                // 使用 if-else 代替 switch expression（兼容 C# 7.3）
                if (ext == ".cs")
                    node.NodeType = FileTreeNodeType.CsFile;
                else if (ext == ".xaml")
                    node.NodeType = FileTreeNodeType.XamlFile;
                else if (ext == ".json")
                    node.NodeType = FileTreeNodeType.JsonFile;
                else if (ext == ".dll")
                    node.NodeType = FileTreeNodeType.DllFile;
                else
                    node.NodeType = FileTreeNodeType.File;
            }

            return node;
        }
    }

    /// <summary>
    /// 文件树节点类型
    /// </summary>
    public enum FileTreeNodeType
    {
        Project,
        Folder,
        File,
        CsFile,
        XamlFile,
        JsonFile,
        DllFile
    }
}
