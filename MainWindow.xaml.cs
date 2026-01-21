using ActiproRoslynPOC.ViewModels;
using ActiproRoslynPOC.Themes;
using ActiproRoslynPOC.Debugging;
using ActiproRoslynPOC.Settings;
using ActiproRoslynPOC.Views;
using ActiproRoslynPOC.Services;
using ActiproSoftware.Text;
using ActiproSoftware.Text.Languages.CSharp.Implementation;
using ActiproSoftware.Text.Languages.DotNet;
using ActiproSoftware.Text.Languages.DotNet.Implementation;
using ActiproSoftware.Text.Languages.DotNet.Reflection;
using ActiproSoftware.Text.Languages.DotNet.Reflection.Implementation;
using ActiproSoftware.Text.Parsing;
using ActiproSoftware.Text.Parsing.LLParser;
using ActiproSoftware.Text.Searching;
using ActiproSoftware.Text.Tagging.Implementation;
using ActiproSoftware.Windows.Controls.Docking;
using ActiproSoftware.Windows.Controls.SyntaxEditor;
using ActiproSoftware.Windows.Controls.SyntaxEditor.Adornments.Implementation;
using ActiproSoftware.Windows.Controls.SyntaxEditor.Highlighting;
using ActiproSoftware.Windows.Controls.SyntaxEditor.Implementation;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
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

        // 新增: 高亮 Tagger Providers
        private HighlightSelectionMatchesTaggerProvider _selectionMatchesTaggerProvider;
        private HighlightReferencesTaggerProvider _referencesTaggerProvider;

        // 新增: Roslyn 语义分类 Tagger Provider（两种实现方式）
        private RoslynSemanticClassificationTaggerProvider _roslynSemanticTaggerProvider;
        private RoslynTokenTaggerProvider _roslynTokenTaggerProvider;  // 新方案：继承 TokenTagger

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
            _selectionMatchesTaggerProvider = new HighlightSelectionMatchesTaggerProvider();
            _referencesTaggerProvider = new HighlightReferencesTaggerProvider();
            _roslynSemanticTaggerProvider = new RoslynSemanticClassificationTaggerProvider();
            _roslynTokenTaggerProvider = new RoslynTokenTaggerProvider();  // 新方案

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

            // 注册选择匹配高亮 Tagger Provider
            if (_selectionMatchesTaggerProvider != null)
            {
                _csharpLanguage.RegisterService(_selectionMatchesTaggerProvider);
                System.Diagnostics.Debug.WriteLine("[MainWindow] 选择匹配高亮 Tagger 已注册");
            }

            // 注册引用高亮 Tagger Provider
            if (_referencesTaggerProvider != null)
            {
                _csharpLanguage.RegisterService(_referencesTaggerProvider);
                System.Diagnostics.Debug.WriteLine("[MainWindow] 引用高亮 Tagger 已注册");
            }

            // 【方案一】注册 Roslyn 语义分类 Tagger Provider（基于 SemanticModel，添加额外 Tagger 层）
            // 使用 OrderPlacement.Before 确保在 Token tagger 之前
            // 注意：这个方案生成的 tags 正确但颜色不显示
            //if (_roslynSemanticTaggerProvider != null)
            //{
            //    _csharpLanguage.RegisterService(_roslynSemanticTaggerProvider);
            //    System.Diagnostics.Debug.WriteLine("[MainWindow] Roslyn 语义分类 Tagger 已注册（基于 SemanticModel，OrderPlacement.Before）");
            //}

            // 【方案二】注册 Roslyn Token Tagger Provider（继承 TokenTagger，在 token 级别分类）
            // 这是 UiPath 使用的方式，直接在词法分析层面修改分类
            // 已修复：使用行/列位置匹配（参考 UiPath GetKey 算法）
            if (_roslynTokenTaggerProvider != null)
            {
                _csharpLanguage.RegisterService(_roslynTokenTaggerProvider);
                System.Diagnostics.Debug.WriteLine("[MainWindow] Roslyn Token Tagger 已注册（继承 TokenTagger，使用行/列位置匹配）");
            }

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
                // 检查文档是否仍在 DockSite 中（可能被用户关闭了）
                if (existingDoc.IsOpen)
                {
                    // 已打开，激活它
                    existingDoc.Activate();
                    AppendOutput($"[文档] 切换到: {Path.GetFileName(filePath)}");
                    return;
                }
                else
                {
                    // 文档已关闭，从字典中移除
                    _openDocuments.Remove(filePath);
                }
            }

            // 创建新的 SyntaxEditor
            var editor = new SyntaxEditor
            {
                IsLineNumberMarginVisible = true,
                IsCurrentLineHighlightingEnabled = true,
                IsSelectionMarginVisible = true,
                IsIndicatorMarginVisible = true,  // 启用断点指示器边栏
                AreIndentationGuidesVisible = true,
                AreWordWrapGlyphsVisible = true,

                BorderThickness = new Thickness(0)
            };

            // 应用编辑器设置
            CodeEditorSettingsService.Instance.ApplySettings(editor);

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

            // 监听光标位置变化和选择变化
            editor.ViewSelectionChanged += (s, e) =>
            {
                UpdateStatusBar(editor);

                // 处理选择匹配高亮
                HandleSelectionMatchHighlight(editor, e);

                // 处理引用高亮
                HandleReferenceHighlight(editor, e);
            };

            // 创建包含导航栏和编辑器的容器
            var containerGrid = new System.Windows.Controls.Grid();
            containerGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
            containerGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });

            // 创建导航符号选择器
            var symbolSelector = new NavigableSymbolSelector
            {
                Margin = new Thickness(0, 0, 0, 1)
            };
            symbolSelector.SyntaxEditor = editor;

            System.Windows.Controls.Grid.SetRow(symbolSelector, 0);
            System.Windows.Controls.Grid.SetRow(editor, 1);

            containerGrid.Children.Add(symbolSelector);
            containerGrid.Children.Add(editor);

            // 创建文档窗口（内容为容器 Grid）
            var docWindow = new DocumentWindow(dockSite, Path.GetFileName(filePath), Path.GetFileName(filePath), null, containerGrid)
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
        }

        /// <summary>
        /// 从文档窗口内容中获取 SyntaxEditor
        /// </summary>
        private SyntaxEditor GetEditorFromDocumentWindow(DocumentWindow docWindow)
        {
            if (docWindow?.Content == null)
                return null;

            // 如果内容直接是 SyntaxEditor
            if (docWindow.Content is SyntaxEditor editor)
                return editor;

            // 如果内容是 Grid 容器（包含导航栏和编辑器）
            if (docWindow.Content is System.Windows.Controls.Grid grid)
            {
                // 编辑器在 Grid 的第二行 (Row 1)
                foreach (var child in grid.Children)
                {
                    if (child is SyntaxEditor se && System.Windows.Controls.Grid.GetRow(se) == 1)
                        return se;
                }
            }

            return null;
        }

        /// <summary>
        /// 获取当前激活的编辑器
        /// </summary>
        private SyntaxEditor GetActiveEditor()
        {
            var activeDoc = dockSite.ActiveWindow as DocumentWindow;
            return GetEditorFromDocumentWindow(activeDoc);
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
                var editor = GetEditorFromDocumentWindow(docWindow);

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

            // 检查是否是工作流设计器
            if (docWindow.Tag is WorkflowDocumentInfo workflowInfo)
            {
                SaveWorkflowDesigner(workflowInfo);
                return;
            }

            // 处理普通文本编辑器
            var editor = GetEditorFromDocumentWindow(docWindow);
            if (editor == null) return;

            try
            {
                string content = editor.Document.CurrentSnapshot.Text;
                File.WriteAllText(filePath, content);

                // 关键：标记文档为未修改状态（清除行号旁的修改标记）
                editor.Document.IsModified = false;

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

        // 旧的 ListBox 方法已被 TreeView 项目资源管理器替代
        private void RefreshFileList()
        {
            // 已废弃：使用 _viewModel.RefreshProjectTree() 代替
        }

        private void InitializeFileList()
        {
            // 已废弃：文件树在打开项目时自动加载
        }

        private void OnFileListBoxMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // 已废弃：使用 OnTreeViewMouseDoubleClick 代替
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

        /// <summary>
        /// 设置按钮点击事件
        /// </summary>
        private void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            var dialog = new EditorSettingsDialog
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                // 保存设置
                CodeEditorSettingsService.Instance.Settings = dialog.Settings;
                CodeEditorSettingsService.Instance.SaveSettings();

                // 应用到所有打开的编辑器
                foreach (var kvp in _openDocuments)
                {
                    var editor = kvp.Value.Content as SyntaxEditor;
                    if (editor != null)
                    {
                        CodeEditorSettingsService.Instance.ApplySettings(editor);
                    }
                }

                AppendOutput("[设置] 编辑器设置已更新");
            }
        }

        /// <summary>
        /// 运行 XAML 工作流按钮点击事件
        /// </summary>
        private void OnRunXamlWorkflowClick(object sender, RoutedEventArgs e)
        {
            RunCurrentWorkflow();
        }

        /// <summary>
        /// 调用工作流按钮点击事件
        /// </summary>
        private void OnInvokeWorkflowClick(object sender, RoutedEventArgs e)
        {
            var projectDirectory = @"E:\ai_app\actipro_rpa\TestWorkflows";

            var dialog = new InvokeWorkflowDialog(projectDirectory)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                // 用户确认调用工作流
                var workflowPath = dialog.SelectedWorkflowPath;
                var arguments = dialog.Arguments;
                var outputVar = dialog.OutputVariableName;
                var entryInfo = dialog.SelectedEntryInfo;

                AppendOutput($"[调用工作流] 文件: {Path.GetFileName(workflowPath)}");
                AppendOutput($"[调用工作流] 入口方法: {entryInfo?.ClassName}.{entryInfo?.MethodName}");

                if (arguments != null && arguments.Count > 0)
                {
                    AppendOutput("[调用工作流] 参数:");
                    foreach (var kvp in arguments)
                    {
                        AppendOutput($"  - {kvp.Key} = {kvp.Value}");
                    }
                }

                // 执行工作流
                try
                {
                    var invocationService = new Services.WorkflowInvocationService(projectDirectory);
                    var result = invocationService.RunWorkflow(workflowPath, arguments);

                    if (result.TryGetValue("__success", out var success) && (bool)success)
                    {
                        AppendOutput("[调用工作流] 执行成功!");

                        if (result.TryGetValue("__result", out var returnValue) && returnValue != null)
                        {
                            AppendOutput($"[调用工作流] 返回值: {FormatResultValue(returnValue)}");

                            if (!string.IsNullOrEmpty(outputVar))
                            {
                                AppendOutput($"[调用工作流] 已保存到变量: {outputVar}");
                            }
                        }
                    }
                    else
                    {
                        var error = result.TryGetValue("__error", out var err) ? err?.ToString() : "未知错误";
                        AppendOutput($"[调用工作流] 执行失败: {error}");
                    }
                }
                catch (Exception ex)
                {
                    AppendOutput($"[调用工作流] 执行异常: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 格式化返回值（支持元组展开）
        /// </summary>
        private string FormatResultValue(object result)
        {
            if (result == null)
                return "null";

            var resultType = result.GetType();

            // 处理元组类型
            if (resultType.IsGenericType && resultType.Name.StartsWith("ValueTuple"))
            {
                var fields = resultType.GetFields();
                var values = new List<string>();
                foreach (var field in fields)
                {
                    values.Add($"{field.Name}: {field.GetValue(result)}");
                }
                return $"({string.Join(", ", values)})";
            }

            // 处理字典类型
            if (result is IDictionary<string, object> dict)
            {
                var items = dict.Select(kvp => $"{kvp.Key}: {kvp.Value}");
                return $"{{ {string.Join(", ", items)} }}";
            }

            return result.ToString();
        }

        #endregion

        #region 选择匹配高亮和引用高亮

        /// <summary>
        /// 处理选择匹配高亮
        /// </summary>
        private void HandleSelectionMatchHighlight(SyntaxEditor editor, EditorViewSelectionEventArgs e)
        {
            var settings = CodeEditorSettingsService.Instance.Settings;
            if (!settings.ShowSelectionMatches)
                return;

            // 获取或创建 Tagger
            if (!editor.Document.Properties.TryGetValue<HighlightSelectionMatchesTagger>(
                typeof(HighlightSelectionMatchesTagger), out var tagger))
            {
                return;
            }

            // 清除之前的高亮
            tagger.Clear();

            // 检查是否有选择
            if (e.View.Selection.IsZeroLength)
                return;

            // 获取选择的文本
            string selectedText = e.View.SelectedText?.Trim();
            if (string.IsNullOrEmpty(selectedText) || selectedText.Length < 2)
                return;

            // 避免选择整行或太长的文本
            if (selectedText.Contains('\n') || selectedText.Length > 100)
                return;

            try
            {
                // 使用搜索功能查找所有匹配
                var options = new EditorSearchOptions
                {
                    FindText = selectedText,
                    PatternProvider = SearchPatternProviders.Normal,
                    MatchWholeWord = false,
                    MatchCase = true
                };

                var searchResults = e.View.Searcher.FindAll(options);
                if (searchResults?.Results != null)
                {
                    foreach (var result in searchResults.Results)
                    {
                        // 跳过当前选择的范围
                        if (result.FindSnapshotRange.StartOffset == e.View.Selection.StartOffset)
                            continue;

                        tagger.HighlightRange(result.FindSnapshotRange, EditorThemeManager.IsDarkTheme);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] 选择匹配高亮失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理引用高亮
        /// </summary>
        private void HandleReferenceHighlight(SyntaxEditor editor, EditorViewSelectionEventArgs e)
        {
            var settings = CodeEditorSettingsService.Instance.Settings;
            if (!settings.HighlightReferences)
                return;

            // 获取或创建 Tagger
            if (!editor.Document.Properties.TryGetValue<HighlightReferencesTagger>(
                typeof(HighlightReferencesTagger), out var tagger))
            {
                return;
            }

            // 清除之前的高亮
            tagger.Clear();

            // 如果有选择文本，则不处理引用高亮（让选择匹配高亮处理）
            if (!e.View.Selection.IsZeroLength)
                return;

            try
            {
                // 获取光标所在的当前词
                var currentWordRange = e.View.GetCurrentWordTextRange();
                if (currentWordRange.IsZeroLength || currentWordRange.Length < 2)
                    return;

                string currentWord = e.View.GetCurrentWordText();
                if (string.IsNullOrEmpty(currentWord))
                    return;

                // 只处理标识符（以字母或下划线开头）
                if (!char.IsLetter(currentWord[0]) && currentWord[0] != '_')
                    return;

                // 使用正则表达式匹配完整单词
                var options = new EditorSearchOptions
                {
                    FindText = currentWord,
                    PatternProvider = SearchPatternProviders.Normal,
                    MatchWholeWord = true,
                    MatchCase = true
                };

                var searchResults = e.View.Searcher.FindAll(options);
                if (searchResults?.Results != null && searchResults.Results.Count > 1)
                {
                    foreach (var result in searchResults.Results)
                    {
                        tagger.HighlightRange(result.FindSnapshotRange, EditorThemeManager.IsDarkTheme);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] 引用高亮失败: {ex.Message}");
            }
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

        #region 项目管理事件处理

        /// <summary>
        /// 打开项目按钮点击
        /// </summary>
        private void OnOpenProjectClick(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择项目文件 (project.json)",
                Filter = "项目文件 (project.json)|project.json|所有文件 (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                var projectPath = Path.GetDirectoryName(dialog.FileName);
                _viewModel.LoadProject(projectPath);
            }
        }

        /// <summary>
        /// 创建项目按钮点击
        /// </summary>
        private void OnCreateProjectClick(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "新建项目",
                FileName = "project.json",
                Filter = "项目文件 (project.json)|project.json",
                CheckPathExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                var projectPath = Path.GetDirectoryName(dialog.FileName);
                var projectName = new DirectoryInfo(projectPath).Name;

                try
                {
                    var config = Services.ProjectService.CreateProject(projectPath, projectName, "新项目");
                    _viewModel.LoadProject(projectPath);
                    MessageBox.Show($"项目已创建: {projectName}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"创建项目失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// 刷新项目按钮点击
        /// </summary>
        private void OnRefreshProjectClick(object sender, RoutedEventArgs e)
        {
            _viewModel.RefreshProjectTree();
        }

        /// <summary>
        /// 文件树双击事件
        /// </summary>
        private void OnTreeViewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var node = _viewModel.GetSelectedNode();
            if (node == null) return;

            // 只打开文件，不打开文件夹
            if (node.NodeType == Models.FileTreeNodeType.CsFile)
            {
                OpenDocument(node.FullPath);
            }
            else if (node.NodeType == Models.FileTreeNodeType.XamlFile)
            {
                // XAML 文件用工作流设计器打开
                OpenWorkflowDesigner(node.FullPath);
            }
        }

        /// <summary>
        /// 新建 CS 工作流
        /// </summary>
        private void OnNewCsWorkflowClick(object sender, RoutedEventArgs e)
        {
            var node = _viewModel.GetSelectedNode();
            if (node == null) return;

            var targetDir = node.NodeType == Models.FileTreeNodeType.Folder
                ? node.FullPath
                : Path.GetDirectoryName(node.FullPath);

            var workflowName = PromptForInput("请输入工作流名称:", "新建 CS 工作流", "NewWorkflow");

            if (string.IsNullOrWhiteSpace(workflowName))
                return;

            try
            {
                var projectPath = _viewModel.GetProjectDirectory();
                Services.ProjectService.CreateCsWorkflow(projectPath, workflowName, targetDir);
                _viewModel.RefreshProjectTree();
                MessageBox.Show($"已创建工作流: {workflowName}.cs", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"创建失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 新建 XAML 工作流
        /// </summary>
        private void OnNewXamlWorkflowClick(object sender, RoutedEventArgs e)
        {
            var node = _viewModel.GetSelectedNode();
            if (node == null) return;

            var targetDir = node.NodeType == Models.FileTreeNodeType.Folder
                ? node.FullPath
                : Path.GetDirectoryName(node.FullPath);

            var workflowName = PromptForInput("请输入工作流名称:", "新建 XAML 工作流", "NewWorkflow");

            if (string.IsNullOrWhiteSpace(workflowName))
                return;

            try
            {
                var projectPath = _viewModel.GetProjectDirectory();
                Services.ProjectService.CreateXamlWorkflow(projectPath, workflowName, targetDir);
                _viewModel.RefreshProjectTree();
                MessageBox.Show($"已创建工作流: {workflowName}.xaml", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"创建失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 重命名文件
        /// </summary>
        private void OnRenameFileClick(object sender, RoutedEventArgs e)
        {
            var node = _viewModel.GetSelectedNode();
            if (node == null || node.NodeType == Models.FileTreeNodeType.Project) return;

            var newName = PromptForInput("请输入新名称:", "重命名", node.Name);

            if (string.IsNullOrWhiteSpace(newName) || newName == node.Name)
                return;

            try
            {
                var oldPath = node.FullPath;
                var newPath = Path.Combine(Path.GetDirectoryName(oldPath), newName);

                if (File.Exists(oldPath))
                    File.Move(oldPath, newPath);
                else if (Directory.Exists(oldPath))
                    Directory.Move(oldPath, newPath);

                _viewModel.RefreshProjectTree();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"重命名失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 删除文件
        /// </summary>
        private void OnDeleteFileClick(object sender, RoutedEventArgs e)
        {
            var node = _viewModel.GetSelectedNode();
            if (node == null || node.NodeType == Models.FileTreeNodeType.Project) return;

            var result = MessageBox.Show(
                $"确定要删除 {node.Name} 吗?",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                if (File.Exists(node.FullPath))
                    File.Delete(node.FullPath);
                else if (Directory.Exists(node.FullPath))
                    Directory.Delete(node.FullPath, true);

                _viewModel.RefreshProjectTree();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"删除失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 在文件资源管理器中显示
        /// </summary>
        private void OnShowInExplorerClick(object sender, RoutedEventArgs e)
        {
            var node = _viewModel.GetSelectedNode();
            if (node == null) return;

            try
            {
                if (File.Exists(node.FullPath))
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{node.FullPath}\"");
                else if (Directory.Exists(node.FullPath))
                    System.Diagnostics.Process.Start("explorer.exe", node.FullPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 简单的输入提示对话框
        /// </summary>
        private string PromptForInput(string message, string title, string defaultValue = "")
        {
            var dialog = new Window
            {
                Title = title,
                Width = 400,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize
            };

            var grid = new System.Windows.Controls.Grid();
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

            var stackPanel = new System.Windows.Controls.StackPanel { Margin = new Thickness(10) };
            var label = new System.Windows.Controls.Label { Content = message };
            var textBox = new System.Windows.Controls.TextBox { Text = defaultValue, Margin = new Thickness(0, 5, 0, 0) };

            stackPanel.Children.Add(label);
            stackPanel.Children.Add(textBox);
            System.Windows.Controls.Grid.SetRow(stackPanel, 0);
            grid.Children.Add(stackPanel);

            var buttonPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(10)
            };

            var okButton = new System.Windows.Controls.Button { Content = "确定", Width = 75, Margin = new Thickness(5, 0, 0, 0) };
            var cancelButton = new System.Windows.Controls.Button { Content = "取消", Width = 75, Margin = new Thickness(5, 0, 0, 0) };

            okButton.Click += (s, e) => { dialog.DialogResult = true; dialog.Close(); };
            cancelButton.Click += (s, e) => { dialog.DialogResult = false; dialog.Close(); };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            System.Windows.Controls.Grid.SetRow(buttonPanel, 1);
            grid.Children.Add(buttonPanel);

            dialog.Content = grid;
            textBox.Focus();
            textBox.SelectAll();

            return dialog.ShowDialog() == true ? textBox.Text : null;
        }

        #endregion

        #region 工作流设计器

        /// <summary>
        /// 打开 XAML 工作流设计器
        /// </summary>
        private void OpenWorkflowDesigner(string filePath)
        {
            if (!File.Exists(filePath))
            {
                AppendOutput($"[错误] 文件不存在: {filePath}");
                return;
            }

            // 检查文档是否已打开
            if (_openDocuments.TryGetValue(filePath, out DocumentWindow existingDoc))
            {
                if (existingDoc.IsOpen)
                {
                    existingDoc.Activate();
                    AppendOutput($"[工作流] 切换到: {Path.GetFileName(filePath)}");
                    return;
                }
                else
                {
                    _openDocuments.Remove(filePath);
                }
            }

            try
            {
                // 注册自定义 Activity 的元数据（必须在创建设计器之前调用）
                RegisterCustomActivities();

                // 创建 WorkflowDesigner
                var designer = new System.Activities.Presentation.WorkflowDesigner();

                // 检查文件是否为空或新创建的
                bool isNewFile = !File.Exists(filePath) || new FileInfo(filePath).Length == 0;

                if (isNewFile)
                {
                    // 创建一个新的 Sequence 作为根活动
                    var rootActivity = WorkflowDesignerHelper.CreateNewSequenceWorkflow();
                    designer.Load(rootActivity);
                }
                else
                {
                    // 加载 XAML 文件
                    designer.Load(filePath);
                }

                // 使用增强的布局（包含工具箱、设计器、属性面板、变量面板）
                var layout = WorkflowDesignerHelper.CreateEnhancedDesignerLayout(designer);

                // 创建文档窗口
                var docWindow = new DocumentWindow(dockSite, Path.GetFileName(filePath), Path.GetFileName(filePath), null, layout)
                {
                    Description = filePath,
                    Tag = filePath
                };

                // 监听设计器变化以标记文件为已修改
                designer.ModelChanged += (s, e) =>
                {
                    // 标记文档已修改（可以在标题显示 * 号）
                    if (!docWindow.Title.EndsWith("*"))
                    {
                        docWindow.Title = Path.GetFileName(filePath) + "*";
                    }
                };

                // 保存设计器引用到文档 Tag（用于保存时获取）
                docWindow.Tag = new WorkflowDocumentInfo
                {
                    FilePath = filePath,
                    Designer = designer
                };

                // 添加到字典
                _openDocuments[filePath] = docWindow;

                // 打开文档
                docWindow.Open();
                docWindow.Activate();

                AppendOutput($"[工作流] 已打开: {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                AppendOutput($"[错误] 打开工作流设计器失败: {ex.Message}");
                MessageBox.Show($"无法打开工作流设计器:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 保存工作流设计器内容
        /// </summary>
        private void SaveWorkflowDesigner(WorkflowDocumentInfo info)
        {
            try
            {
                info.Designer.Flush();
                info.Designer.Save(info.FilePath);
                AppendOutput($"[工作流] 已保存: {Path.GetFileName(info.FilePath)}");
            }
            catch (Exception ex)
            {
                AppendOutput($"[错误] 保存工作流失败: {ex.Message}");
                MessageBox.Show($"保存工作流失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 运行当前激活的 XAML 工作流
        /// </summary>
        private void RunCurrentWorkflow()
        {
            try
            {
                // 获取当前激活的文档窗口
                var activeDoc = dockSite.ActiveWindow as DocumentWindow;
                if (activeDoc == null)
                {
                    AppendOutput("[工作流] 没有打开的工作流");
                    MessageBox.Show("请先打开一个工作流文件", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 检查是否是工作流设计器
                if (activeDoc.Tag is WorkflowDocumentInfo workflowInfo)
                {
                    AppendOutput($"[工作流] 开始执行: {Path.GetFileName(workflowInfo.FilePath)}");

                    // 先保存工作流
                    SaveWorkflowDesigner(workflowInfo);

                    // 使用 XamlWorkflowService 执行工作流
                    var xamlService = new XamlWorkflowService();

                    // 订阅日志事件
                    xamlService.LogOutput += (msg) =>
                    {
                        AppendOutput(msg);
                    };

                    // 执行工作流
                    var result = xamlService.ExecuteWorkflow(workflowInfo.FilePath);

                    // 显示结果
                    if (result.ContainsKey("__success") && (bool)result["__success"])
                    {
                        AppendOutput("[工作流] 执行成功");
                        if (result.Count > 1) // 除了 __success 还有其他输出
                        {
                            AppendOutput("[工作流] 输出结果:");
                            foreach (var kvp in result)
                            {
                                if (!kvp.Key.StartsWith("__"))
                                {
                                    AppendOutput($"  {kvp.Key} = {kvp.Value}");
                                }
                            }
                        }
                    }
                    else
                    {
                        var error = result.ContainsKey("__error") ? result["__error"].ToString() : "未知错误";
                        AppendOutput($"[工作流] 执行失败: {error}");
                        MessageBox.Show($"工作流执行失败:\n{error}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    // 不是工作流设计器，可能是 C# 代码编辑器
                    AppendOutput("[工作流] 当前文档不是 XAML 工作流，请使用 '运行' 按钮执行 C# 代码");
                }
            }
            catch (Exception ex)
            {
                AppendOutput($"[错误] 运行工作流失败: {ex.Message}");
                MessageBox.Show($"运行工作流失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 注册自定义 Activity 的元数据（只注册一次）
        /// </summary>
        private static bool _designerMetadataRegistered = false;
        private void RegisterCustomActivities()
        {
            if (_designerMetadataRegistered)
                return;

            // 注册标准活动的设计器元数据（这是支持多组件的关键！）
            var dm = new System.Activities.Core.Presentation.DesignerMetadata();
            dm.Register();

            // 注册自定义元数据存储
            var metadataStore = new System.Activities.Presentation.Metadata.AttributeTableBuilder();

            // 为 InvokeCodedWorkflow 添加自定义设计器元数据
            metadataStore.AddCustomAttributes(
                typeof(Activities.InvokeCodedWorkflow),
                new System.ComponentModel.DesignerAttribute(typeof(Activities.InvokeCodedWorkflowDesigner)));

            // 为 InvokeWorkflow 添加自定义设计器元数据
            metadataStore.AddCustomAttributes(
                typeof(Activities.InvokeWorkflow),
                new System.ComponentModel.DesignerAttribute(typeof(Activities.InvokeWorkflowDesigner)));

            System.Activities.Presentation.Metadata.MetadataStore.AddAttributeTable(metadataStore.CreateTable());

            _designerMetadataRegistered = true;
        }

        /// <summary>
        /// 创建工具箱控件
        /// </summary>
        private System.Activities.Presentation.Toolbox.ToolboxControl CreateToolbox()
        {
            var toolbox = new System.Activities.Presentation.Toolbox.ToolboxControl();

            // 1. 控制流分类
            var controlFlowCategory = new System.Activities.Presentation.Toolbox.ToolboxCategory("控制流");
            controlFlowCategory.Add(new System.Activities.Presentation.Toolbox.ToolboxItemWrapper(typeof(System.Activities.Statements.Sequence), "Sequence"));
            controlFlowCategory.Add(new System.Activities.Presentation.Toolbox.ToolboxItemWrapper(typeof(System.Activities.Statements.Flowchart), "Flowchart"));
            controlFlowCategory.Add(new System.Activities.Presentation.Toolbox.ToolboxItemWrapper(typeof(System.Activities.Statements.If), "If"));
            controlFlowCategory.Add(new System.Activities.Presentation.Toolbox.ToolboxItemWrapper(typeof(System.Activities.Statements.While), "While"));
            controlFlowCategory.Add(new System.Activities.Presentation.Toolbox.ToolboxItemWrapper(typeof(System.Activities.Statements.DoWhile), "DoWhile"));
            controlFlowCategory.Add(new System.Activities.Presentation.Toolbox.ToolboxItemWrapper(typeof(System.Activities.Statements.ForEach<>), "ForEach<T>"));
            controlFlowCategory.Add(new System.Activities.Presentation.Toolbox.ToolboxItemWrapper(typeof(System.Activities.Statements.Switch<>), "Switch<T>"));
            controlFlowCategory.Add(new System.Activities.Presentation.Toolbox.ToolboxItemWrapper(typeof(System.Activities.Statements.Parallel), "Parallel"));
            controlFlowCategory.Add(new System.Activities.Presentation.Toolbox.ToolboxItemWrapper(typeof(System.Activities.Statements.Pick), "Pick"));
            toolbox.Categories.Add(controlFlowCategory);

            // 2. 基本活动分类
            var primitivesCategory = new System.Activities.Presentation.Toolbox.ToolboxCategory("基本活动");
            primitivesCategory.Add(new System.Activities.Presentation.Toolbox.ToolboxItemWrapper(typeof(System.Activities.Statements.Assign<>), "Assign<T>"));
            primitivesCategory.Add(new System.Activities.Presentation.Toolbox.ToolboxItemWrapper(typeof(System.Activities.Statements.Delay), "Delay"));
            primitivesCategory.Add(new System.Activities.Presentation.Toolbox.ToolboxItemWrapper(typeof(System.Activities.Statements.WriteLine), "WriteLine"));
            primitivesCategory.Add(new System.Activities.Presentation.Toolbox.ToolboxItemWrapper(typeof(System.Activities.Statements.InvokeMethod), "InvokeMethod"));
            toolbox.Categories.Add(primitivesCategory);

            // 3. 集合操作分类
            var collectionCategory = new System.Activities.Presentation.Toolbox.ToolboxCategory("集合操作");
            collectionCategory.Add(new System.Activities.Presentation.Toolbox.ToolboxItemWrapper(typeof(System.Activities.Statements.AddToCollection<>), "AddToCollection<T>"));
            collectionCategory.Add(new System.Activities.Presentation.Toolbox.ToolboxItemWrapper(typeof(System.Activities.Statements.RemoveFromCollection<>), "RemoveFromCollection<T>"));
            collectionCategory.Add(new System.Activities.Presentation.Toolbox.ToolboxItemWrapper(typeof(System.Activities.Statements.ExistsInCollection<>), "ExistsInCollection<T>"));
            collectionCategory.Add(new System.Activities.Presentation.Toolbox.ToolboxItemWrapper(typeof(System.Activities.Statements.ClearCollection<>), "ClearCollection<T>"));
            toolbox.Categories.Add(collectionCategory);

            // 4. 错误处理分类
            var errorHandlingCategory = new System.Activities.Presentation.Toolbox.ToolboxCategory("错误处理");
            errorHandlingCategory.Add(new System.Activities.Presentation.Toolbox.ToolboxItemWrapper(typeof(System.Activities.Statements.TryCatch), "TryCatch"));
            errorHandlingCategory.Add(new System.Activities.Presentation.Toolbox.ToolboxItemWrapper(typeof(System.Activities.Statements.Throw), "Throw"));
            errorHandlingCategory.Add(new System.Activities.Presentation.Toolbox.ToolboxItemWrapper(typeof(System.Activities.Statements.Rethrow), "Rethrow"));
            toolbox.Categories.Add(errorHandlingCategory);

            // 5. 自定义工作流活动分类
            var customCategory = new System.Activities.Presentation.Toolbox.ToolboxCategory("工作流调用");
            customCategory.Add(new System.Activities.Presentation.Toolbox.ToolboxItemWrapper(
                typeof(Activities.InvokeCodedWorkflow),
                "InvokeCodedWorkflow",
                "调用 C# 工作流"));
            customCategory.Add(new System.Activities.Presentation.Toolbox.ToolboxItemWrapper(
                typeof(Activities.InvokeWorkflow),
                "InvokeWorkflow",
                "调用 XAML 工作流"));
            toolbox.Categories.Add(customCategory);

            return toolbox;
        }

        /// <summary>
        /// 工作流文档信息（用于存储在 DocumentWindow.Tag 中）
        /// </summary>
        private class WorkflowDocumentInfo
        {
            public string FilePath { get; set; }
            public System.Activities.Presentation.WorkflowDesigner Designer { get; set; }
        }

        #endregion
    }
}
