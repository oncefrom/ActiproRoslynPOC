using ActiproRoslynPOC.Services;
using GalaSoft.MvvmLight.Command;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace ActiproRoslynPOC.ViewModels
{
    /// <summary>
    /// 文件管理 ViewModel
    /// 负责文件的创建、打开、保存等操作
    /// </summary>
    public class FileManagementViewModel : INotifyPropertyChanged
    {
        private string _currentFilePath;
        private bool _isModified;
        private bool _isLoadingFile;
        private string _code;

        public event PropertyChangedEventHandler PropertyChanged;
        public event Action<string> OutputMessageReceived;
        public event Action<string> FileLoaded;
        public event Action<string> FileSaved;

        #region 属性

        /// <summary>
        /// 当前文件路径
        /// </summary>
        public string CurrentFilePath
        {
            get => _currentFilePath;
            set
            {
                if (_currentFilePath != value)
                {
                    _currentFilePath = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentFileName));
                    OnPropertyChanged(nameof(WindowTitle));
                }
            }
        }

        /// <summary>
        /// 当前文件名
        /// </summary>
        public string CurrentFileName =>
            string.IsNullOrEmpty(CurrentFilePath) ? "未命名.cs" : Path.GetFileName(CurrentFilePath);

        /// <summary>
        /// 文件是否已修改
        /// </summary>
        public bool IsModified
        {
            get => _isModified;
            set
            {
                if (_isModified != value)
                {
                    _isModified = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(WindowTitle));
                }
            }
        }

        /// <summary>
        /// 窗口标题
        /// </summary>
        public string WindowTitle =>
            $"{CurrentFileName}{(IsModified ? "*" : "")} - Actipro Roslyn 工作流编辑器";

        /// <summary>
        /// 代码内容
        /// </summary>
        public string Code
        {
            get => _code;
            set
            {
                if (_code != value)
                {
                    _code = value;
                    OnPropertyChanged();

                    // 如果不是正在加载文件，则标记为已修改
                    if (!_isLoadingFile)
                    {
                        IsModified = true;
                    }
                }
            }
        }

        #endregion

        #region 命令

        public ICommand NewFileCommand { get; }
        public ICommand OpenFileCommand { get; }
        public ICommand SaveCommand { get; }

        #endregion

        public FileManagementViewModel()
        {
            NewFileCommand = new RelayCommand(ExecuteNewFile);
            OpenFileCommand = new RelayCommand(ExecuteOpenFile);
            SaveCommand = new RelayCommand(ExecuteSave, CanExecuteSave);
        }

        #region 文件操作

        /// <summary>
        /// 创建新文件
        /// </summary>
        private void ExecuteNewFile()
        {
            try
            {
                // 如果当前文件已修改，提示保存
                if (IsModified)
                {
                    var result = System.Windows.MessageBox.Show(
                        "当前文件已修改，是否保存？",
                        "提示",
                        System.Windows.MessageBoxButton.YesNoCancel
                    );

                    if (result == System.Windows.MessageBoxResult.Yes)
                    {
                        ExecuteSave();
                    }
                    else if (result == System.Windows.MessageBoxResult.Cancel)
                    {
                        return;
                    }
                }

                // 创建新文件
                CurrentFilePath = null;
                Code = GetDefaultTemplate();
                IsModified = false;

                LoggingService.Instance.Info("FileManagement", "创建新文件");
                OutputMessageReceived?.Invoke("✓ 已创建新文件");
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("FileManagement", "创建新文件失败", ex);
                OutputMessageReceived?.Invoke($"[错误] 创建新文件失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 打开文件
        /// </summary>
        private void ExecuteOpenFile()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "C# 文件 (*.cs)|*.cs|所有文件 (*.*)|*.*",
                    DefaultExt = ".cs"
                };

                if (dialog.ShowDialog() == true)
                {
                    LoadFile(dialog.FileName);
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("FileManagement", "打开文件失败", ex);
                OutputMessageReceived?.Invoke($"[错误] 打开文件失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 加载文件
        /// </summary>
        public void LoadFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                OutputMessageReceived?.Invoke($"[错误] 文件不存在: {filePath}");
                return;
            }

            try
            {
                _isLoadingFile = true;

                Code = File.ReadAllText(filePath);
                CurrentFilePath = filePath;
                IsModified = false;

                LoggingService.Instance.Info("FileManagement", $"已加载文件: {filePath}");
                OutputMessageReceived?.Invoke($"✓ 已加载: {Path.GetFileName(filePath)}");

                // 触发文件加载事件
                FileLoaded?.Invoke(filePath);
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("FileManagement", $"加载文件失败: {filePath}", ex);
                OutputMessageReceived?.Invoke($"[错误] 加载文件失败: {ex.Message}");
            }
            finally
            {
                _isLoadingFile = false;
            }
        }

        /// <summary>
        /// 保存文件
        /// </summary>
        private void ExecuteSave()
        {
            try
            {
                // 如果没有文件路径，弹出另存为对话框
                if (string.IsNullOrEmpty(CurrentFilePath))
                {
                    var dialog = new Microsoft.Win32.SaveFileDialog
                    {
                        Filter = "C# 文件 (*.cs)|*.cs|所有文件 (*.*)|*.*",
                        DefaultExt = ".cs",
                        FileName = "NewWorkflow.cs"
                    };

                    if (dialog.ShowDialog() != true)
                    {
                        return;
                    }

                    CurrentFilePath = dialog.FileName;
                }

                // 保存文件
                File.WriteAllText(CurrentFilePath, Code);
                IsModified = false;

                LoggingService.Instance.Info("FileManagement", $"已保存文件: {CurrentFilePath}");
                OutputMessageReceived?.Invoke($"✓ 已保存: {CurrentFileName}");

                // 触发文件保存事件
                FileSaved?.Invoke(CurrentFilePath);
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("FileManagement", "保存文件失败", ex);
                OutputMessageReceived?.Invoke($"[错误] 保存文件失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 判断是否可以保存
        /// </summary>
        private bool CanExecuteSave()
        {
            return IsModified && !string.IsNullOrWhiteSpace(Code);
        }

        /// <summary>
        /// 触发文件保存（由外部调用，例如快捷键）
        /// </summary>
        public void TriggerFileSaved(string filePath)
        {
            if (filePath == CurrentFilePath)
            {
                IsModified = false;
                LoggingService.Instance.Info("FileManagement", $"文件已通过外部保存: {filePath}");
            }
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 获取默认模板
        /// </summary>
        private string GetDefaultTemplate()
        {
            return @"using System;
using System.Threading.Tasks;

namespace TestWorkflows
{
    public class NewWorkflow : CodedWorkflowBase
    {
        [Workflow(Name = ""新工作流"", Description = ""工作流描述"")]
        public async Task Execute()
        {
            Log(""开始执行工作流..."");

            // TODO: 在这里编写你的代码

            Log(""工作流执行完成"");
        }
    }
}
";
        }

        #endregion

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
