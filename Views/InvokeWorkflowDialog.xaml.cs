using ActiproRoslynPOC.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;

namespace ActiproRoslynPOC.Views
{
    /// <summary>
    /// 参数输入项 ViewModel
    /// </summary>
    public class ParameterInputItem : INotifyPropertyChanged
    {
        private string _value;

        public string Name { get; set; }
        public string TypeName { get; set; }
        public string DisplayTypeName { get; set; }
        public bool HasDefaultValue { get; set; }
        public string DefaultValueText { get; set; }
        public Type ClrType { get; set; }

        public string Value
        {
            get => _value;
            set
            {
                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }

        public string TooltipText
        {
            get
            {
                var tip = $"类型: {DisplayTypeName}";
                if (HasDefaultValue)
                {
                    tip += $"\n默认值: {DefaultValueText}";
                }
                return tip;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>
    /// 工作流文件项
    /// </summary>
    public class WorkflowFileItem
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public string DisplayName { get; set; }
        public CSharpEntryMethodInfo EntryInfo { get; set; }
    }

    /// <summary>
    /// 调用工作流对话框
    /// </summary>
    public partial class InvokeWorkflowDialog : Window
    {
        private readonly CSharpFileAnalyzer _analyzer;
        private readonly string _workflowDirectory;
        private ObservableCollection<ParameterInputItem> _parameters;
        private CSharpEntryMethodInfo _selectedEntryInfo;

        /// <summary>
        /// 选中的工作流文件路径
        /// </summary>
        public string SelectedWorkflowPath { get; private set; }

        /// <summary>
        /// 输入的参数值
        /// </summary>
        public Dictionary<string, object> Arguments { get; private set; }

        /// <summary>
        /// 输出变量名（可选）
        /// </summary>
        public string OutputVariableName { get; private set; }

        /// <summary>
        /// 选中的入口方法信息
        /// </summary>
        public CSharpEntryMethodInfo SelectedEntryInfo => _selectedEntryInfo;

        public InvokeWorkflowDialog(string workflowDirectory)
        {
            InitializeComponent();

            _analyzer = new CSharpFileAnalyzer();
            _workflowDirectory = workflowDirectory;
            _parameters = new ObservableCollection<ParameterInputItem>();
            parametersPanel.ItemsSource = _parameters;

            // 加载工作流列表
            LoadWorkflowFiles();
        }

        /// <summary>
        /// 加载工作流目录下的所有 .cs 文件
        /// </summary>
        private void LoadWorkflowFiles()
        {
            var items = new List<WorkflowFileItem>();

            if (Directory.Exists(_workflowDirectory))
            {
                var csFiles = Directory.GetFiles(_workflowDirectory, "*.cs", SearchOption.TopDirectoryOnly);

                foreach (var filePath in csFiles)
                {
                    try
                    {
                        var entryInfo = _analyzer.AnalyzeFile(filePath);
                        if (entryInfo != null)
                        {
                            items.Add(new WorkflowFileItem
                            {
                                FilePath = filePath,
                                FileName = Path.GetFileName(filePath),
                                DisplayName = $"{entryInfo.WorkflowName} ({Path.GetFileName(filePath)})",
                                EntryInfo = entryInfo
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[InvokeWorkflowDialog] 分析文件失败: {filePath}, {ex.Message}");
                    }
                }
            }

            cmbWorkflowFiles.ItemsSource = items.OrderBy(x => x.DisplayName).ToList();

            if (items.Count > 0)
            {
                cmbWorkflowFiles.SelectedIndex = 0;
            }
        }

        /// <summary>
        /// 工作流文件选择变化
        /// </summary>
        private void OnWorkflowFileSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var selectedItem = cmbWorkflowFiles.SelectedItem as WorkflowFileItem;
            if (selectedItem == null)
            {
                ClearInfo();
                return;
            }

            UpdateWorkflowInfo(selectedItem.EntryInfo);
            SelectedWorkflowPath = selectedItem.FilePath;
        }

        /// <summary>
        /// 更新工作流信息显示
        /// </summary>
        private void UpdateWorkflowInfo(CSharpEntryMethodInfo info)
        {
            _selectedEntryInfo = info;

            // 更新基本信息
            txtWorkflowName.Text = info.WorkflowName ?? info.MethodName;
            txtClassName.Text = info.ClassName;
            txtReturnType.Text = info.ReturnTypeName;

            // 清空并重新填充参数
            _parameters.Clear();

            if (info.HasParameters)
            {
                foreach (var param in info.Parameters)
                {
                    _parameters.Add(new ParameterInputItem
                    {
                        Name = param.Name,
                        TypeName = param.TypeName,
                        DisplayTypeName = param.DisplayTypeName,
                        HasDefaultValue = param.HasDefaultValue,
                        DefaultValueText = param.DefaultValueText,
                        ClrType = param.GetClrType(),
                        Value = param.DefaultValueText ?? ""
                    });
                }

                parametersPanel.Visibility = Visibility.Visible;
                txtNoParameters.Visibility = Visibility.Collapsed;
            }
            else
            {
                parametersPanel.Visibility = Visibility.Collapsed;
                txtNoParameters.Visibility = Visibility.Visible;
            }

            // 显示输出映射（如果有返回值）
            if (info.HasReturnValue)
            {
                grpOutputMapping.Visibility = Visibility.Visible;
            }
            else
            {
                grpOutputMapping.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 清空信息
        /// </summary>
        private void ClearInfo()
        {
            _selectedEntryInfo = null;
            txtWorkflowName.Text = "";
            txtClassName.Text = "";
            txtReturnType.Text = "";
            _parameters.Clear();
            parametersPanel.Visibility = Visibility.Collapsed;
            txtNoParameters.Visibility = Visibility.Visible;
            grpOutputMapping.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 浏览按钮点击
        /// </summary>
        private void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择工作流文件",
                Filter = "C# 文件 (*.cs)|*.cs",
                InitialDirectory = _workflowDirectory
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var entryInfo = _analyzer.AnalyzeFile(dialog.FileName);
                    if (entryInfo != null)
                    {
                        UpdateWorkflowInfo(entryInfo);
                        SelectedWorkflowPath = dialog.FileName;

                        // 添加到下拉列表并选中
                        var newItem = new WorkflowFileItem
                        {
                            FilePath = dialog.FileName,
                            FileName = Path.GetFileName(dialog.FileName),
                            DisplayName = $"{entryInfo.WorkflowName} ({Path.GetFileName(dialog.FileName)})",
                            EntryInfo = entryInfo
                        };

                        var existingItems = cmbWorkflowFiles.ItemsSource as List<WorkflowFileItem>;
                        if (existingItems != null && !existingItems.Any(x => x.FilePath == dialog.FileName))
                        {
                            existingItems.Add(newItem);
                            cmbWorkflowFiles.ItemsSource = null;
                            cmbWorkflowFiles.ItemsSource = existingItems;
                        }

                        cmbWorkflowFiles.SelectedItem = newItem;
                    }
                    else
                    {
                        MessageBox.Show("该文件中没有找到有效的工作流入口方法。\n请确保类继承自 CodedWorkflowBase 并使用 [Workflow] 特性标记入口方法。",
                            "无效的工作流文件", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"分析文件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// 确定按钮点击
        /// </summary>
        private void OnOKClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(SelectedWorkflowPath))
            {
                MessageBox.Show("请选择一个工作流文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 收集参数值
            Arguments = new Dictionary<string, object>();
            var converter = new WorkflowParameterService();

            foreach (var param in _parameters)
            {
                try
                {
                    var value = converter.ConvertValue(param.Value, param.ClrType);
                    Arguments[param.Name] = value;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"参数 '{param.Name}' 转换失败: {ex.Message}",
                        "参数错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            // 获取输出变量名
            OutputVariableName = txtOutputVariable.Text?.Trim();

            DialogResult = true;
            Close();
        }

        /// <summary>
        /// 取消按钮点击
        /// </summary>
        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
