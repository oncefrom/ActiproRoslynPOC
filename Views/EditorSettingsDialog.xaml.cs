using ActiproRoslynPOC.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace ActiproRoslynPOC.Views
{
    /// <summary>
    /// 编辑器设置对话框
    /// </summary>
    public partial class EditorSettingsDialog : Window
    {
        private CodeEditorSettings _originalSettings;
        private CodeEditorSettings _editingSettings;

        /// <summary>
        /// 获取编辑后的设置
        /// </summary>
        public CodeEditorSettings Settings => _editingSettings;

        public EditorSettingsDialog()
        {
            InitializeComponent();

            // 获取当前设置的副本
            _originalSettings = CodeEditorSettingsService.Instance.Settings;
            _editingSettings = _originalSettings.Clone();

            // 初始化字体列表
            InitializeFontList();

            // 加载设置到 UI
            LoadSettingsToUI();
        }

        /// <summary>
        /// 初始化字体列表
        /// </summary>
        private void InitializeFontList()
        {
            // 常用编程字体
            var commonFonts = new List<string>
            {
                "Consolas",
                "Cascadia Code",
                "Cascadia Mono",
                "Fira Code",
                "JetBrains Mono",
                "Source Code Pro",
                "Monaco",
                "Menlo",
                "Courier New",
                "DejaVu Sans Mono",
                "Ubuntu Mono"
            };

            // 获取系统已安装的等宽字体
            var installedFonts = Fonts.SystemFontFamilies
                .Select(f => f.Source)
                .Where(name => commonFonts.Contains(name) || name.Contains("Mono") || name.Contains("Code"))
                .OrderBy(name => name)
                .ToList();

            // 合并并去重
            var allFonts = commonFonts
                .Union(installedFonts)
                .Distinct()
                .OrderBy(name => name)
                .ToList();

            fontComboBox.ItemsSource = allFonts;
        }

        /// <summary>
        /// 将设置加载到 UI 控件
        /// </summary>
        private void LoadSettingsToUI()
        {
            // 字体设置
            fontComboBox.Text = _editingSettings.FontName;
            fontSizeSlider.Value = _editingSettings.FontSize;

            // Tab 设置
            autoConvertTabCheckBox.IsChecked = _editingSettings.AutoConvertTabToSpace;
            tabSizeSlider.Value = _editingSettings.TabSize;

            // 显示设置
            showWhitespaceCheckBox.IsChecked = _editingSettings.ViewWhiteSpace;
            highlightCurrentLineCheckBox.IsChecked = _editingSettings.HighlightCurrentLine;
            showGuideLinesCheckBox.IsChecked = _editingSettings.ShowStructureGuideLines;
            showLineNumbersCheckBox.IsChecked = _editingSettings.AreLineNumbersVisible;
            showSelectionMarginCheckBox.IsChecked = _editingSettings.IsSelectionMarginVisible;
            showIndicatorMarginCheckBox.IsChecked = _editingSettings.IsIndicatorMarginVisible;

            // 高级功能
            showSelectionMatchesCheckBox.IsChecked = _editingSettings.ShowSelectionMatches;
            highlightReferencesCheckBox.IsChecked = _editingSettings.HighlightReferences;
            showErrorSquigglesCheckBox.IsChecked = _editingSettings.AreErrorSquigglesVisible;
            enableWordCompletionCheckBox.IsChecked = _editingSettings.EnableWordCompletion;
            autoShowParameterInfoCheckBox.IsChecked = _editingSettings.AutoShowParameterInfo;
        }

        /// <summary>
        /// 从 UI 控件读取设置
        /// </summary>
        private void ReadSettingsFromUI()
        {
            // 字体设置
            _editingSettings.FontName = fontComboBox.Text;
            _editingSettings.FontSize = fontSizeSlider.Value;

            // Tab 设置
            _editingSettings.AutoConvertTabToSpace = autoConvertTabCheckBox.IsChecked ?? true;
            _editingSettings.TabSize = (int)tabSizeSlider.Value;

            // 显示设置
            _editingSettings.ViewWhiteSpace = showWhitespaceCheckBox.IsChecked ?? false;
            _editingSettings.HighlightCurrentLine = highlightCurrentLineCheckBox.IsChecked ?? true;
            _editingSettings.ShowStructureGuideLines = showGuideLinesCheckBox.IsChecked ?? true;
            _editingSettings.AreLineNumbersVisible = showLineNumbersCheckBox.IsChecked ?? true;
            _editingSettings.IsSelectionMarginVisible = showSelectionMarginCheckBox.IsChecked ?? true;
            _editingSettings.IsIndicatorMarginVisible = showIndicatorMarginCheckBox.IsChecked ?? true;

            // 高级功能
            _editingSettings.ShowSelectionMatches = showSelectionMatchesCheckBox.IsChecked ?? true;
            _editingSettings.HighlightReferences = highlightReferencesCheckBox.IsChecked ?? true;
            _editingSettings.AreErrorSquigglesVisible = showErrorSquigglesCheckBox.IsChecked ?? true;
            _editingSettings.EnableWordCompletion = enableWordCompletionCheckBox.IsChecked ?? true;
            _editingSettings.AutoShowParameterInfo = autoShowParameterInfoCheckBox.IsChecked ?? true;
        }

        /// <summary>
        /// 确定按钮点击
        /// </summary>
        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            ReadSettingsFromUI();
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

        /// <summary>
        /// 重置默认按钮点击
        /// </summary>
        private void OnResetClick(object sender, RoutedEventArgs e)
        {
            _editingSettings = new CodeEditorSettings();
            LoadSettingsToUI();
        }
    }
}
