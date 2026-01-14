using ActiproRoslynPOC.ViewModels;
using ActiproRoslynPOC.Themes;
using ActiproRoslynPOC.Debugging;
using ActiproSoftware.Text;
using ActiproSoftware.Text.Languages.CSharp.Implementation;
using ActiproSoftware.Text.Languages.DotNet;
using ActiproSoftware.Text.Languages.DotNet.Implementation;
using ActiproSoftware.Text.Languages.DotNet.Reflection;
using ActiproSoftware.Text.Languages.DotNet.Reflection.Implementation;
using ActiproSoftware.Text.Parsing;
using ActiproSoftware.Text.Parsing.LLParser;
using ActiproSoftware.Text.Tagging.Implementation;
using ActiproSoftware.Windows.Controls.Docking;
using ActiproSoftware.Windows.Controls.SyntaxEditor;
using ActiproSoftware.Windows.Controls.SyntaxEditor.Adornments.Implementation;
using ActiproSoftware.Windows.Controls.SyntaxEditor.Highlighting;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ActiproRoslynPOC
{
    public partial class MainWindow : Window
    {
        private MainViewModel _viewModel;
        private IProjectAssembly _projectAssembly;
        private CSharpSyntaxLanguage _csharpLanguage;
        private UsingDirectiveTaggerProvider _usingTaggerProvider;

        // 多文档管理：存储打开的文档 <文件路径, DocumentWindow>
        private Dictionary<string, DocumentWindow> _openDocuments = new Dictionary<string, DocumentWindow>(StringComparer.OrdinalIgnoreCase);

        public MainWindow()
        {
            InitializeComponent();

            // --- 0. 预先加载外部程序集到 AppDomain ---
            // 必须在创建 ViewModel 之前执行，因为 ViewModel 会初始化 RoslynCompilerService
            PreloadExternalAssemblies();

            // 显式打开工具窗口
            // --- 1. 初始化 ViewModel ---
            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            // --- 2. 初始化自定义 Tagger Provider ---
            _usingTaggerProvider = new UsingDirectiveTaggerProvider();

            // --- 2.1 初始化 Roslyn 样式配置（使用 VS Settings 导入方式）---
            RoslynStyleConfigurator.Initialize();

            // --- 2.2 配置 Actipro v25.1 核心环境 ---
            ConfigureIntelliSense();

            // --- 3. 订阅 ViewModel 事件 ---
            _viewModel.FileCreated += OnFileCreated;
            _viewModel.FileSaved += OnFileSaved;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.DebugLineChanged += OnDebugLineChanged;

            // --- 3.1 设置断点获取回调 ---
            _viewModel.GetBreakpointsFromUI = GetCurrentEditorBreakpoints;

            // --- 3.2 订阅断点切换事件（调试过程中动态修改断点）---
            DebuggingPointerInputEventSink.BreakpointToggled += OnBreakpointToggled;

            // --- 4. 初始化文件列表 ---
            InitializeFileList();

            // --- 5. 快捷键绑定 ---
            SetupInputBindings();

            // --- 6. 打开默认文件 ---
            OpenDefaultFile();
        }

        /// <summary>
        /// 启动时打开默认文件
        /// </summary>
        private void OpenDefaultFile()
        {
            var projectDirectory = @"E:\ai_app\actipro_rpa\TestWorkflows";
            var defaultFilePath = Path.Combine(projectDirectory, "MainWorkflow.cs");

            if (File.Exists(defaultFilePath))
            {
                OpenDocument(defaultFilePath);
            }
            else
            {
                // 如果默认文件不存在，打开目录中的第一个 .cs 文件
                var csFiles = Directory.GetFiles(projectDirectory, "*.cs");
                if (csFiles.Length > 0)
                {
                    OpenDocument(csFiles[0]);
                }
            }
        }

        #region IntelliSense 配置

        /// <summary>
        /// 预先加载外部程序集到 AppDomain
        /// 必须在创建 ViewModel/RoslynCompilerService 之前执行
        /// </summary>
        private void PreloadExternalAssemblies()
        {
            // 手动加载 TestDLL.dll（位于程序目录下）
            var appPath = AppDomain.CurrentDomain.BaseDirectory;
            var testDllPath = Path.Combine(appPath, "TestDLL.dll");
            if (File.Exists(testDllPath))
            {
                // 加载到当前 AppDomain（这样 RoslynCompilerService 初始化时能找到）
                var loadedAssembly = Assembly.LoadFrom(testDllPath);
                System.Diagnostics.Debug.WriteLine($"[MainWindow] 预加载 TestDLL.dll 到 AppDomain: {loadedAssembly.FullName}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] TestDLL.dll 不存在: {testDllPath}");
            }
        }

        private void ConfigureIntelliSense()
        {
            // 官方新写法：创建项目程序集
            _projectAssembly = new CSharpProjectAssembly("DynamicProject");

            // 加载当前 AppDomain 中已加载的所有程序集（包括预加载的 TestDLL.dll）
            LoadAppDomainAssemblies();

            // 创建语言
            _csharpLanguage = new CSharpSyntaxLanguage();

            // 注册基础的 DotNetClassificationTypeProvider（用于基础语法高亮）
            _csharpLanguage.RegisterService(new DotNetClassificationTypeProvider());

            System.Diagnostics.Debug.WriteLine("[MainWindow] DotNetClassificationTypeProvider 已注册");

            // 注册项目程序集（这会触发 IntelliSense 和语法分析）
            _csharpLanguage.RegisterProjectAssembly(_projectAssembly);

            // 注册自定义 Tagger Provider 到语言
            if (_usingTaggerProvider != null)
            {
                _csharpLanguage.RegisterService(_usingTaggerProvider);
            }

            // 【已禁用】语义分类 Tagger Provider（用于方法名、类名等细粒度高亮）
            // 效果不理想，暂时禁用
            // var semanticTaggerProvider = new SemanticClassificationTaggerProvider();
            // _csharpLanguage.RegisterService(semanticTaggerProvider);

            System.Diagnostics.Debug.WriteLine("[MainWindow] 语义分类 Tagger 已禁用，仅使用基础词法高亮");

            // --- 注册调试功能服务 ---
            // 注册调试指针输入事件处理（点击左侧边栏切换断点）
            _csharpLanguage.RegisterService(new DebuggingPointerInputEventSink());

            // 注册执行时间显示的 Adornment Manager 和 Tagger
            _csharpLanguage.RegisterService(new AdornmentManagerProvider<ElapsedTimeAdornmentManager>(typeof(ElapsedTimeAdornmentManager)));
            _csharpLanguage.RegisterService(new CodeDocumentTaggerProvider<ElapsedTimeTagger>(typeof(ElapsedTimeTagger)));

            System.Diagnostics.Debug.WriteLine("[MainWindow] 调试功能服务已注册");

            // 加载目录下的所有 .cs 文件
            LoadDirectorySourceFiles(@"E:\ai_app\actipro_rpa\TestWorkflows");
        }

        /// <summary>
        /// 加载当前 AppDomain 中已加载的所有程序集到项目引用
        /// 这样在产品迁移时，主程序已加载的程序集都能被 IntelliSense 和编译使用
        /// </summary>
        private void LoadAppDomainAssemblies()
        {
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            var loadedCount = 0;
            var skippedCount = 0;

            System.Diagnostics.Debug.WriteLine($"[MainWindow] 开始加载 AppDomain 程序集，共 {loadedAssemblies.Length} 个");

            foreach (var assembly in loadedAssemblies)
            {
                try
                {
                    // 跳过动态程序集（没有物理文件）
                    if (assembly.IsDynamic)
                    {
                        skippedCount++;
                        continue;
                    }

                    // 跳过没有位置的程序集
                    if (string.IsNullOrEmpty(assembly.Location))
                    {
                        skippedCount++;
                        continue;
                    }

                    // 跳过系统程序集中不需要的（可选，减少加载时间）
                    var assemblyName = assembly.GetName().Name;

                    // 加载程序集
                    _projectAssembly.AssemblyReferences.AddFrom(assembly.Location);
                    loadedCount++;

                    System.Diagnostics.Debug.WriteLine($"[MainWindow] 已加载程序集: {assemblyName}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MainWindow] 加载程序集失败: {assembly.FullName}, 错误: {ex.Message}");
                    skippedCount++;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[MainWindow] AppDomain 程序集加载完成: 成功 {loadedCount} 个, 跳过 {skippedCount} 个");
        }

        /// <summary>
        /// 手动添加额外的程序集引用（供产品迁移时使用）
        /// </summary>
        /// <param name="assemblyPath">程序集文件路径</param>
        public void AddAssemblyReference(string assemblyPath)
        {
            if (_projectAssembly == null)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] 项目程序集未初始化，无法添加引用: {assemblyPath}");
                return;
            }

            if (!File.Exists(assemblyPath))
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] 程序集文件不存在: {assemblyPath}");
                return;
            }

            try
            {
                _projectAssembly.AssemblyReferences.AddFrom(assemblyPath);
                System.Diagnostics.Debug.WriteLine($"[MainWindow] 已手动添加程序集引用: {assemblyPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] 添加程序集引用失败: {assemblyPath}, 错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 手动添加已加载的程序集引用（供产品迁移时使用）
        /// </summary>
        /// <param name="assembly">已加载的程序集</param>
        public void AddAssemblyReference(Assembly assembly)
        {
            if (_projectAssembly == null)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] 项目程序集未初始化，无法添加引用: {assembly?.FullName}");
                return;
            }

            if (assembly == null || assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location))
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] 程序集无效或为动态程序集: {assembly?.FullName}");
                return;
            }

            try
            {
                _projectAssembly.AssemblyReferences.AddFrom(assembly.Location);
                System.Diagnostics.Debug.WriteLine($"[MainWindow] 已手动添加程序集引用: {assembly.GetName().Name}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] 添加程序集引用失败: {assembly.FullName}, 错误: {ex.Message}");
            }
        }

        private void LoadDirectorySourceFiles(string directoryPath)
        {
            if (!Directory.Exists(directoryPath)) return;

            foreach (var file in Directory.GetFiles(directoryPath, "*.cs"))
            {
                _projectAssembly.SourceFiles.QueueFile(_csharpLanguage, file);
            }
        }

        #endregion

        #region 多文档管理

        /// <summary>
        /// 打开或切换到指定文件
        /// </summary>
        private void OpenDocument(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                AppendOutput($"[错误] 文件不存在: {filePath}");
                return;
            }

            // 检查文档是否已打开
            if (_openDocuments.TryGetValue(filePath, out DocumentWindow existingDoc))
            {
                // 已打开，激活它
                existingDoc.Activate();
                AppendOutput($"[文档] 切换到: {Path.GetFileName(filePath)}");
                return;
            }

            // 创建新的 SyntaxEditor
            var editor = new SyntaxEditor
            {
                IsLineNumberMarginVisible = true,
                IsCurrentLineHighlightingEnabled = true,
                IsSelectionMarginVisible = true,
                IsIndicatorMarginVisible = true,  // 启用断点指示器边栏
                BorderThickness = new Thickness(0)
            };

            // 应用编辑器主题背景
            EditorThemeManager.ApplyEditorBackground(editor);

            // 设置语言和文件名
            editor.Document.Language = _csharpLanguage;
            editor.Document.FileName = filePath;

            // 加载文件内容
            string content = File.ReadAllText(filePath);
            editor.Document.SetText(content);

            // 监听文本变化
            editor.Document.TextChanged += (s, e) =>
            {
                OnDocumentTextChanged(filePath, editor);
            };

            // 监听光标位置变化
            editor.ViewSelectionChanged += (s, e) =>
            {
                UpdateStatusBar(editor);
            };

            // 创建文档窗口
            var docWindow = new DocumentWindow(dockSite, Path.GetFileName(filePath), Path.GetFileName(filePath), null, editor)
            {
                Description = filePath,
                Tag = filePath  // 存储完整路径
            };

            // 添加到字典
            _openDocuments[filePath] = docWindow;

            // 打开文档
            docWindow.Open();
            docWindow.Activate();

            // 同步到 ViewModel
            _viewModel.CurrentFilePath = filePath;
            _viewModel.Code = content;

            AppendOutput($"[文档] 已打开: {Path.GetFileName(filePath)}");
        }

        /// <summary>
        /// 文档内容变化时的处理
        /// </summary>
        private void OnDocumentTextChanged(string filePath, SyntaxEditor editor)
        {
            // 更新 ViewModel（仅当是当前激活的文档时）
            if (_viewModel.CurrentFilePath == filePath)
            {
                _viewModel.Code = editor.Document.CurrentSnapshot.Text;
            }

            // 标记文档为已修改
            if (_openDocuments.TryGetValue(filePath, out DocumentWindow docWindow))
            {
                string fileName = Path.GetFileName(filePath);
                if (!docWindow.Title.StartsWith("*"))
                {
                    docWindow.Title = "*" + fileName;
                }
            }
        }

        /// <summary>
        /// 获取当前激活的编辑器
        /// </summary>
        private SyntaxEditor GetActiveEditor()
        {
            var activeDoc = dockSite.ActiveWindow as DocumentWindow;
            return activeDoc?.Content as SyntaxEditor;
        }

        /// <summary>
        /// 获取当前激活文档的文件路径
        /// </summary>
        private string GetActiveFilePath()
        {
            var activeDoc = dockSite.ActiveWindow as DocumentWindow;
            return activeDoc?.Tag as string;
        }

        #endregion

        #region DockSite 事件处理


        /// <summary>
        /// 文档窗口激活时
        /// </summary>
        private void DockSite_WindowActivated(object sender, DockingWindowEventArgs e)
        {
            if (e.Window is DocumentWindow docWindow)
            {
                string filePath = docWindow.Tag as string;
                var editor = docWindow.Content as SyntaxEditor;

                if (!string.IsNullOrEmpty(filePath) && editor != null)
                {
                    // 更新 ViewModel
                    _viewModel.CurrentFilePath = filePath;
                    _viewModel.Code = editor.Document.CurrentSnapshot.Text;

                    // 更新状态栏
                    UpdateStatusBar(editor);

                    AppendOutput($"[文档] 激活: {Path.GetFileName(filePath)}");
                }
            }
        }

        #endregion

        #region 文件操作

        /// <summary>
        /// 保存指定文档
        /// </summary>
        private void SaveDocument(string filePath)
        {
            if (!_openDocuments.TryGetValue(filePath, out DocumentWindow docWindow))
                return;

            var editor = docWindow.Content as SyntaxEditor;
            if (editor == null) return;

            try
            {
                string content = editor.Document.CurrentSnapshot.Text;
                File.WriteAllText(filePath, content);

                // 更新标题（移除 * 标记）
                docWindow.Title = Path.GetFileName(filePath);

                // 同步到 SourceFile
                _projectAssembly.SourceFiles.QueueCode(_csharpLanguage, filePath, content);

                AppendOutput($"[文档] 已保存: {Path.GetFileName(filePath)}");

                // 触发 FileSaved 事件刷新其他文件的 IntelliSense
                //_viewModel.TriggerFileSaved(filePath);
            }
            catch (Exception ex)
            {
                AppendOutput($"[错误] 保存失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 保存当前激活的文档
        /// </summary>
        private void SaveCurrentDocument()
        {
            string filePath = GetActiveFilePath();
            if (!string.IsNullOrEmpty(filePath))
            {
                SaveDocument(filePath);
            }
        }

        #endregion

        #region ViewModel 事件处理

        private void OnFileCreated(string newPath)
        {
            if (_projectAssembly != null)
            {
                _projectAssembly.SourceFiles.QueueFile(_csharpLanguage, newPath);
            }

            RefreshFileList();
            OpenDocument(newPath);
        }

        private void OnFileSaved(string savedPath)
        {
            // 保存当前激活的文档
            SaveCurrentDocument();
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // 如果需要同步 ViewModel.Code 到编辑器
            // （一般不需要，因为编辑器是主数据源）
        }

        #endregion

        #region 文件列表

        private void RefreshFileList()
        {
            var projectDirectory = @"E:\ai_app\actipro_rpa\TestWorkflows";
            if (Directory.Exists(projectDirectory))
            {
                var csFiles = Directory.GetFiles(projectDirectory, "*.cs")
                    .Select(f => Path.GetFileName(f))
                    .OrderBy(f => f).ToList();
                fileListBox.ItemsSource = csFiles;
            }
        }

        private void InitializeFileList()
        {
            RefreshFileList();

            // 双击打开文件已在 XAML 中绑定
        }

        private void OnFileListBoxMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (fileListBox.SelectedItem is string fileName)
            {
                var projectDirectory = @"E:\ai_app\actipro_rpa\TestWorkflows";
                var fullPath = Path.Combine(projectDirectory, fileName);
                OpenDocument(fullPath);
            }
        }

        #endregion

        #region 快捷键和状态栏

        private void SetupInputBindings()
        {
            // F5 - 调试模式下是继续执行，非调试模式是运行
            InputBindings.Add(new KeyBinding(
                new RelayCommand(() =>
                {
                    if (_viewModel.IsDebugging)
                        _viewModel.ContinueCommand.Execute(null);
                    else
                        _viewModel.RunCommand.Execute(null);
                }),
                Key.F5,
                ModifierKeys.None));

            // Shift+F5 停止调试
            InputBindings.Add(new KeyBinding(_viewModel.StopDebugCommand, Key.F5, ModifierKeys.Shift));

            // F8 语法检查
            InputBindings.Add(new KeyBinding(_viewModel.CheckSyntaxCommand, Key.F8, ModifierKeys.None));

            // F9 开始调试
            InputBindings.Add(new KeyBinding(_viewModel.StartDebugCommand, Key.F9, ModifierKeys.None));

            // F10 单步执行
            InputBindings.Add(new KeyBinding(_viewModel.StepOverCommand, Key.F10, ModifierKeys.None));

            // Ctrl+S 保存
            InputBindings.Add(new KeyBinding(
                new RelayCommand(() => SaveCurrentDocument()),
                Key.S,
                ModifierKeys.Control));

            // Ctrl+W 关闭当前文档
            InputBindings.Add(new KeyBinding(
                new RelayCommand(() =>
                {
                    var activeDoc = dockSite.ActiveWindow as DocumentWindow;
                    activeDoc?.Close();
                }),
                Key.W,
                ModifierKeys.Control));
        }

        private void UpdateStatusBar(SyntaxEditor editor)
        {
            if (editor?.ActiveView != null)
            {
                var position = editor.ActiveView.Selection.EndPosition;
                linePanel.Text = $"Ln {position.DisplayLine}";
                columnPanel.Text = $"Col {position.DisplayCharacter}";
            }
        }

        #endregion

        #region 辅助方法

        private void AppendOutput(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _viewModel.Output += message + "\n";
            });
        }

        #endregion

        #region 主题和语法高亮

        /// <summary>
        /// 切换主题按钮点击事件
        /// </summary>
        private void OnToggleThemeClick(object sender, RoutedEventArgs e)
        {
            // 切换主题
            EditorThemeManager.ToggleTheme();

            // 更新按钮文本
            themeButtonText.Text = EditorThemeManager.IsDarkTheme ? "切换浅色主题" : "切换深色主题";

            // 更新所有打开的编辑器背景并强制刷新
            foreach (var kvp in _openDocuments)
            {
                var docWindow = kvp.Value;
                var editor = docWindow.Content as SyntaxEditor;
                if (editor != null)
                {
                    EditorThemeManager.ApplyEditorBackground(editor);

                    // 强制刷新编辑器文档以重新应用高亮样式
                    var currentText = editor.Document.CurrentSnapshot.Text;
                    editor.Document.SetText(currentText);
                }
            }

            AppendOutput($"[主题] 已切换到{(EditorThemeManager.IsDarkTheme ? "深色" : "浅色")}主题");
        }

        #endregion

        private void dockSite_WindowsClosing(object sender, DockingWindowsEventArgs e)
        {
            if (e.Source is DocumentWindow docWindow)
            {
                string filePath = docWindow.Tag as string;
                if (!string.IsNullOrEmpty(filePath))
                {
                    // 检查是否有未保存的更改
                    if (docWindow.Title.StartsWith("*"))
                    {
                        var result = MessageBox.Show(
                            $"文件 {Path.GetFileName(filePath)} 有未保存的更改，是否保存？",
                            "保存确认",
                            MessageBoxButton.YesNoCancel,
                            MessageBoxImage.Question);

                        if (result == MessageBoxResult.Yes)
                        {
                            SaveDocument(filePath);
                        }
                        else if (result == MessageBoxResult.Cancel)
                        {
                            e.Cancel = true;
                            return;
                        }
                    }

                    // 保存当前编辑器内容到 SourceFile（切换前）
                    var editor = docWindow.Content as SyntaxEditor;
                    if (editor != null)
                    {
                        string currentContent = editor.Document.CurrentSnapshot.Text;
                        _projectAssembly.SourceFiles.QueueCode(_csharpLanguage, filePath, currentContent);
                    }

                    // 从字典中移除
                    _openDocuments.Remove(filePath);
                    AppendOutput($"[文档] 已关闭: {Path.GetFileName(filePath)}");
                }
            }
        }

        #region 调试功能

        /// <summary>
        /// 获取当前编辑器的所有断点行号
        /// </summary>
        private List<int> GetCurrentEditorBreakpoints()
        {
            var breakpoints = new List<int>();
            var editor = GetActiveEditor();

            if (editor?.Document != null)
            {
                var breakpointTags = editor.Document.IndicatorManager.Breakpoints.GetInstances();
                foreach (var tagRange in breakpointTags)
                {
                    if (tagRange.Tag.IsEnabled)
                    {
                        var snapshotRange = tagRange.VersionRange.Translate(editor.Document.CurrentSnapshot);
                        int lineIndex = snapshotRange.StartPosition.Line + 1; // +1 因为行号从1开始
                        breakpoints.Add(lineIndex);
                    }
                }
            }

            return breakpoints.OrderBy(l => l).ToList();
        }

        /// <summary>
        /// 调试行变化事件处理 - 更新当前语句指示器
        /// </summary>
        private void OnDebugLineChanged(int line)
        {
            var editor = GetActiveEditor();
            if (editor?.Document == null) return;

            var document = editor.Document;

            if (line <= 0)
            {
                // 清除当前语句指示器
                document.IndicatorManager.CurrentStatement.Clear();
            }
            else
            {
                // 设置当前语句指示器
                var snapshot = document.CurrentSnapshot;
                if (line > 0 && line <= snapshot.Lines.Count)
                {
                    var lineObj = snapshot.Lines[line - 1]; // -1 因为行索引从0开始
                    var snapshotRange = new ActiproSoftware.Text.TextSnapshotRange(
                        snapshot,
                        lineObj.StartOffset,
                        lineObj.EndOffset
                    );
                    document.IndicatorManager.CurrentStatement.SetInstance(snapshotRange);

                    // 确保当前行可见
                    editor.ActiveView.Scroller.ScrollLineToVisibleMiddle();
                }
            }
        }

        /// <summary>
        /// 断点切换事件处理（调试过程中动态修改断点）
        /// </summary>
        private void OnBreakpointToggled(IEditorDocument document, int lineIndex)
        {
            // 只在调试过程中处理
            if (!_viewModel.IsDebugging)
                return;

            // 检查当前编辑器是否是被切换断点的文档
            var editor = GetActiveEditor();
            if (editor?.Document != document)
                return;

            // 获取更新后的所有断点
            var updatedBreakpoints = GetCurrentEditorBreakpoints();

            // 通知 ViewModel 更新调试器中的断点
            _viewModel.UpdateBreakpoints(updatedBreakpoints);
        }

        #endregion
    }
}
