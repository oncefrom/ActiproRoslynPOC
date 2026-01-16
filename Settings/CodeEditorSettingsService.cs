using ActiproSoftware.Windows.Controls.SyntaxEditor;
using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace ActiproRoslynPOC.Settings
{
    /// <summary>
    /// 代码编辑器设置服务
    /// 参考 UiPath Studio 的 CodeEditorSettingsService 实现
    /// 提供设置的加载、保存和应用功能
    /// </summary>
    public class CodeEditorSettingsService
    {
        #region 单例

        private static CodeEditorSettingsService _instance;
        private static readonly object _lock = new object();

        /// <summary>
        /// 获取单例实例
        /// </summary>
        public static CodeEditorSettingsService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new CodeEditorSettingsService();
                        }
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region 字段和属性

        private CodeEditorSettings _settings;
        private readonly string _settingsFilePath;

        /// <summary>
        /// 当前设置
        /// </summary>
        public CodeEditorSettings Settings
        {
            get => _settings;
            set
            {
                _settings = value;
                OnSettingsChanged();
            }
        }

        /// <summary>
        /// 设置变更事件
        /// </summary>
        public event EventHandler SettingsChanged;

        #endregion

        #region 构造函数

        private CodeEditorSettingsService()
        {
            // 设置文件路径
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(appDataPath, "ActiproRoslynPOC");

            if (!Directory.Exists(appFolder))
            {
                Directory.CreateDirectory(appFolder);
            }

            _settingsFilePath = Path.Combine(appFolder, "editor_settings.json");

            // 加载设置
            LoadSettings();
        }

        #endregion

        #region 设置应用

        /// <summary>
        /// 将设置应用到编辑器
        /// </summary>
        public void ApplySettings(SyntaxEditor editor)
        {
            if (editor == null || _settings == null)
                return;

            try
            {
                // 字体设置
                if (IsFontInstalled(_settings.FontName))
                {
                    editor.FontFamily = new FontFamily(_settings.FontName);
                }
                else
                {
                    editor.FontFamily = new FontFamily("Consolas");
                }
                editor.FontSize = _settings.FontSize;

                // 文档设置
                if (editor.Document != null)
                {
                    editor.Document.AutoConvertTabsToSpaces = _settings.AutoConvertTabToSpace;
                    editor.Document.TabSize = _settings.TabSize;
                }

                // 显示设置
                editor.IsWhitespaceVisible = _settings.ViewWhiteSpace;
                editor.IsCurrentLineHighlightingEnabled = _settings.HighlightCurrentLine;
                editor.AreIndentationGuidesVisible = _settings.ShowStructureGuideLines;
                editor.IsLineNumberMarginVisible = _settings.AreLineNumbersVisible;
                editor.IsSelectionMarginVisible = _settings.IsSelectionMarginVisible;
                editor.IsIndicatorMarginVisible = _settings.IsIndicatorMarginVisible;

                // 禁用水平分割
                editor.CanSplitHorizontally = false;

                System.Diagnostics.Debug.WriteLine($"[CodeEditorSettingsService] 设置已应用到编辑器");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CodeEditorSettingsService] 应用设置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 检查字体是否已安装
        /// </summary>
        private bool IsFontInstalled(string fontName)
        {
            if (string.IsNullOrEmpty(fontName))
                return false;

            try
            {
                var testFont = new FontFamily(fontName);
                return testFont.FamilyNames.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 设置持久化

        /// <summary>
        /// 保存设置到文件
        /// </summary>
        public void SaveSettings()
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                var json = JsonSerializer.Serialize(_settings, options);
                File.WriteAllText(_settingsFilePath, json);

                System.Diagnostics.Debug.WriteLine($"[CodeEditorSettingsService] 设置已保存到: {_settingsFilePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CodeEditorSettingsService] 保存设置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 从文件加载设置
        /// </summary>
        public void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    _settings = JsonSerializer.Deserialize<CodeEditorSettings>(json);
                    System.Diagnostics.Debug.WriteLine($"[CodeEditorSettingsService] 设置已从文件加载");
                }
                else
                {
                    _settings = new CodeEditorSettings();
                    System.Diagnostics.Debug.WriteLine($"[CodeEditorSettingsService] 使用默认设置");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CodeEditorSettingsService] 加载设置失败: {ex.Message}");
                _settings = new CodeEditorSettings();
            }
        }

        /// <summary>
        /// 重置为默认设置
        /// </summary>
        public void ResetToDefault()
        {
            _settings = new CodeEditorSettings();
            SaveSettings();
            OnSettingsChanged();
        }

        #endregion

        #region 事件

        /// <summary>
        /// 触发设置变更事件
        /// </summary>
        protected virtual void OnSettingsChanged()
        {
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        #endregion
    }
}
