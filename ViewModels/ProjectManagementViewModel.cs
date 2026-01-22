using ActiproRoslynPOC.Models;
using ActiproRoslynPOC.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ActiproRoslynPOC.ViewModels
{
    /// <summary>
    /// 项目管理 ViewModel
    /// 负责项目的加载、刷新和文件树管理
    /// </summary>
    public class ProjectManagementViewModel : INotifyPropertyChanged
    {
        private string _currentProjectPath;
        private ObservableCollection<FileTreeNode> _projectTree;
        private FileTreeNode _selectedNode;

        public event PropertyChangedEventHandler PropertyChanged;
        public event Action<string> OutputMessageReceived;
        public event Action<string> FileSelected;

        #region 属性

        /// <summary>
        /// 当前项目路径
        /// </summary>
        public string CurrentProjectPath
        {
            get => _currentProjectPath;
            set
            {
                if (_currentProjectPath != value)
                {
                    _currentProjectPath = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 项目文件树
        /// </summary>
        public ObservableCollection<FileTreeNode> ProjectTree
        {
            get => _projectTree;
            set
            {
                if (_projectTree != value)
                {
                    _projectTree = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 选中的节点
        /// </summary>
        public FileTreeNode SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (_selectedNode != value)
                {
                    _selectedNode = value;
                    OnPropertyChanged();

                    // 如果是文件节点，触发文件选中事件
                    if (_selectedNode != null && IsFileNode(_selectedNode))
                    {
                        FileSelected?.Invoke(_selectedNode.FullPath);
                    }
                }
            }
        }

        #endregion

        public ProjectManagementViewModel()
        {
            ProjectTree = new ObservableCollection<FileTreeNode>();
        }

        #region 项目操作

        /// <summary>
        /// 加载项目
        /// </summary>
        public void LoadProject(string projectPath)
        {
            if (string.IsNullOrEmpty(projectPath))
            {
                LoggingService.Instance.Warning("ProjectManagement", "项目路径为空");
                return;
            }

            try
            {
                // 如果路径是文件，获取其目录
                if (File.Exists(projectPath))
                {
                    projectPath = Path.GetDirectoryName(projectPath);
                }

                if (!Directory.Exists(projectPath))
                {
                    LoggingService.Instance.Error("ProjectManagement", $"项目目录不存在: {projectPath}");
                    OutputMessageReceived?.Invoke($"[错误] 项目目录不存在: {projectPath}");
                    return;
                }

                CurrentProjectPath = projectPath;
                RefreshProjectTree();

                LoggingService.Instance.Info("ProjectManagement", $"已加载项目: {projectPath}");
                OutputMessageReceived?.Invoke($"✓ 已加载项目: {Path.GetFileName(projectPath)}");
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("ProjectManagement", "加载项目失败", ex);
                OutputMessageReceived?.Invoke($"[错误] 加载项目失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 刷新项目树
        /// </summary>
        public void RefreshProjectTree()
        {
            if (string.IsNullOrEmpty(CurrentProjectPath))
            {
                return;
            }

            try
            {
                // 保存当前展开的节点路径
                var expandedPaths = new HashSet<string>();
                CollectExpandedPaths(ProjectTree, expandedPaths);

                // 重新加载项目树
                ProjectTree.Clear();
                var rootNode = FileTreeNode.FromPath(CurrentProjectPath);
                ProjectTree.Add(rootNode);

                // 恢复展开状态
                RestoreExpandedPaths(ProjectTree, expandedPaths);

                LoggingService.Instance.Debug("ProjectManagement", "项目树已刷新");
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("ProjectManagement", "刷新项目树失败", ex);
                OutputMessageReceived?.Invoke($"[错误] 刷新项目树失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取项目目录
        /// </summary>
        public string GetProjectDirectory()
        {
            return ConfigurationService.Instance.GetProjectWorkflowDirectory(CurrentProjectPath);
        }

        /// <summary>
        /// 获取所有 C# 文件路径
        /// </summary>
        public List<string> GetAllCSharpFiles()
        {
            if (string.IsNullOrEmpty(CurrentProjectPath) || !Directory.Exists(CurrentProjectPath))
            {
                return new List<string>();
            }

            try
            {
                return Directory.GetFiles(CurrentProjectPath, "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains("\\obj\\") && !f.Contains("\\bin\\"))
                    .ToList();
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("ProjectManagement", "获取C#文件列表失败", ex);
                return new List<string>();
            }
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 收集展开的节点路径
        /// </summary>
        private void CollectExpandedPaths(ObservableCollection<FileTreeNode> nodes, HashSet<string> expandedPaths)
        {
            if (nodes == null) return;

            foreach (var node in nodes)
            {
                if (node.IsExpanded)
                {
                    expandedPaths.Add(node.FullPath);
                }

                if (node.Children != null && node.Children.Count > 0)
                {
                    CollectExpandedPaths(node.Children, expandedPaths);
                }
            }
        }

        /// <summary>
        /// 恢复展开状态
        /// </summary>
        private void RestoreExpandedPaths(ObservableCollection<FileTreeNode> nodes, HashSet<string> expandedPaths)
        {
            if (nodes == null) return;

            foreach (var node in nodes)
            {
                if (expandedPaths.Contains(node.FullPath))
                {
                    node.IsExpanded = true;
                }

                if (node.Children != null && node.Children.Count > 0)
                {
                    RestoreExpandedPaths(node.Children, expandedPaths);
                }
            }
        }

        /// <summary>
        /// 判断节点是否为文件节点
        /// </summary>
        private bool IsFileNode(FileTreeNode node)
        {
            return node.NodeType == FileTreeNodeType.CsFile ||
                   node.NodeType == FileTreeNodeType.XamlFile ||
                   node.NodeType == FileTreeNodeType.JsonFile ||
                   node.NodeType == FileTreeNodeType.DllFile ||
                   node.NodeType == FileTreeNodeType.File;
        }

        #endregion

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
