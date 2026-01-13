using ActiproSoftware.Windows.Controls.SyntaxEditor;
using ActiproSoftware.Windows.Controls.SyntaxEditor.Highlighting;
using System.Windows.Media;

namespace ActiproRoslynPOC.Themes
{
    /// <summary>
    /// 编辑器主题管理器 - 使用 Actipro 官方方式管理主题
    /// </summary>
    public static class EditorThemeManager
    {
        private static bool _isDarkTheme = false;

        /// <summary>
        /// 切换主题
        /// </summary>
        public static void ToggleTheme()
        {
            _isDarkTheme = !_isDarkTheme;

            // 调用 RoslynStyleConfigurator 切换语法高亮颜色
            if (_isDarkTheme)
            {
                RoslynStyleConfigurator.SwitchToDarkTheme();
            }
            else
            {
                RoslynStyleConfigurator.SwitchToLightTheme();
            }

            System.Diagnostics.Debug.WriteLine($"[EditorThemeManager] 已切换到 {(_isDarkTheme ? "深色" : "浅色")} 主题");
        }

        /// <summary>
        /// 为编辑器设置背景色
        /// </summary>
        public static void ApplyEditorBackground(SyntaxEditor editor)
        {
            if (_isDarkTheme)
            {
                editor.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
                editor.Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220));
            }
            else
            {
                editor.Background = new SolidColorBrush(Color.FromRgb(255, 255, 255));
                editor.Foreground = new SolidColorBrush(Color.FromRgb(0, 0, 0));
            }
        }

        /// <summary>
        /// 获取当前主题状态
        /// </summary>
        public static bool IsDarkTheme => _isDarkTheme;
    }
}
