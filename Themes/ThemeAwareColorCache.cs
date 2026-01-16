using System.Collections.Generic;
using System.Windows.Media;

namespace ActiproRoslynPOC.Themes
{
    /// <summary>
    /// 主题感知的颜色缓存 - 存储浅色和深色主题的颜色映射
    /// </summary>
    public class ThemeAwareColorCache
    {
        private Dictionary<string, Color> _lightThemeColors;
        private Dictionary<string, Color> _darkThemeColors;

        public ThemeAwareColorCache()
        {
            InitializeLightTheme();
            InitializeDarkTheme();
        }

        /// <summary>
        /// 初始化浅色主题 - 风格：UiPath Studio Light Theme
        /// 参考 UiPath ThemeAwareColorCache 实现
        /// 建议编辑器背景色设置为: #FFFFFF (白色)
        /// </summary>
        private void InitializeLightTheme()
        {
            _lightThemeColors = new Dictionary<string, Color>
            {
                // 标识符 (黑色 - UiPath 默认文本)
                { "Identifier", Color.FromArgb(255, 0, 0, 0) },

                // 控制关键字 (蓝色 - UiPath 关键字颜色)
                { "KeywordControl", Color.FromArgb(255, 0, 0, 255) },

                // 方法名 (棕色 - UiPath 方法名 RGB: 116, 83, 31)
                { "UsedMethodName", Color.FromArgb(255, 116, 83, 31) },

                // 类引用 (青绿色 - UiPath 类型名称 RGB: 43, 145, 175)
                { "ClassObjectReference", Color.FromArgb(255, 43, 145, 175) },

                // 接口引用 (浅青色 - 略微区分于类)
                { "InterfaceObjectReference", Color.FromArgb(255, 43, 145, 175) },

                // 结构体引用 (青绿色 - 与类相同)
                { "StructureObjectReference", Color.FromArgb(255, 43, 145, 175) },

                // 参数/局部变量 (深蓝色 - UiPath 参数颜色 RGB: 31, 55, 127)
                { "ParametersObjectReference", Color.FromArgb(255, 31, 55, 127) },

                // 一般关键字 (蓝色 - UiPath 标准)
                { "Keyword", Color.FromArgb(255, 0, 0, 255) },

                // 字符串 (棕红色 - UiPath 字符串颜色 RGB: 163, 21, 21)
                { "String", Color.FromArgb(255, 163, 21, 21) },

                // 注释 (绿色 - UiPath 注释颜色 RGB: 0, 128, 0)
                { "Comment", Color.FromArgb(255, 0, 128, 0) },

                // 数字 (黑色)
                { "Number", Color.FromArgb(255, 0, 0, 0) },

                // 操作符 (黑色)
                { "Operator", Color.FromArgb(255, 0, 0, 0) },

                // 预处理指令 (灰色)
                { "PreprocessorKeyword", Color.FromArgb(255, 128, 128, 128) },

                // 未使用的代码 (浅灰色，带透明度)
                { "UnnecessaryCode", Color.FromArgb(100, 128, 128, 128) },

                // 选择匹配高亮背景 (淡黄色)
                { "SelectionMatchHighlight", Color.FromArgb(80, 255, 255, 0) },

                // 引用高亮背景 (淡蓝色)
                { "ReferenceHighlight", Color.FromArgb(80, 173, 214, 255) }
            };
        }

        /// <summary>
        /// 初始化深色主题 - 风格：UiPath Dark Theme
        /// 参考 UiPath ThemeAwareColorCache 实现
        /// 建议编辑器背景色设置为: #1E1E1E (VS Code 深色)
        /// </summary>
        private void InitializeDarkTheme()
        {
            _darkThemeColors = new Dictionary<string, Color>
            {
                // 标识符 (浅灰白色 - UiPath 深色主题默认文本)
                { "Identifier", Color.FromArgb(255, 220, 220, 220) },

                // 控制关键字 (淡紫色 - UiPath 深色主题关键字)
                { "KeywordControl", Color.FromArgb(255, 86, 156, 214) },

                // 方法名 (淡黄色 - UiPath 深色主题方法名 RGB: 220, 220, 170)
                { "UsedMethodName", Color.FromArgb(255, 220, 220, 170) },

                // 类引用 (亮青色 - UiPath 深色主题类名 RGB: 78, 201, 176)
                { "ClassObjectReference", Color.FromArgb(255, 78, 201, 176) },

                // 接口引用 (浅绿色 - 略微区分于类 RGB: 184, 215, 163)
                { "InterfaceObjectReference", Color.FromArgb(255, 184, 215, 163) },

                // 结构体引用 (亮青色 - 与类相同)
                { "StructureObjectReference", Color.FromArgb(255, 78, 201, 176) },

                // 参数/局部变量 (浅蓝色 - UiPath 深色主题参数 RGB: 156, 220, 254)
                { "ParametersObjectReference", Color.FromArgb(255, 156, 220, 254) },

                // 一般关键字 (蓝色)
                { "Keyword", Color.FromArgb(255, 86, 156, 214) },

                // 字符串 (橙色 - UiPath 深色主题字符串 RGB: 214, 157, 133)
                { "String", Color.FromArgb(255, 214, 157, 133) },

                // 注释 (绿色 - UiPath 深色主题注释 RGB: 87, 166, 74)
                { "Comment", Color.FromArgb(255, 87, 166, 74) },

                // 数字 (浅绿色 RGB: 181, 206, 168)
                { "Number", Color.FromArgb(255, 181, 206, 168) },

                // 操作符 (白色)
                { "Operator", Color.FromArgb(255, 220, 220, 220) },

                // 预处理指令 (灰色)
                { "PreprocessorKeyword", Color.FromArgb(255, 155, 155, 155) },

                // 未使用的代码 (深灰色，带透明度)
                { "UnnecessaryCode", Color.FromArgb(80, 100, 100, 100) },

                // 选择匹配高亮背景 (深金色)
                { "SelectionMatchHighlight", Color.FromArgb(100, 255, 200, 50) },

                // 引用高亮背景 (深紫色)
                { "ReferenceHighlight", Color.FromArgb(100, 139, 100, 200) }
            };
        }

        /// <summary>
        /// 获取指定分类类型在当前主题下的颜色
        /// </summary>
        public Color GetColor(string classificationKey, bool isDarkTheme)
        {
            var dictionary = isDarkTheme ? _darkThemeColors : _lightThemeColors;

            if (dictionary.TryGetValue(classificationKey, out Color color))
            {
                return color;
            }

            // 默认颜色
            return isDarkTheme ? Color.FromArgb(255, 220, 220, 220) : Color.FromArgb(255, 0, 0, 0);
        }

        /// <summary>
        /// 获取所有浅色主题颜色
        /// </summary>
        public Dictionary<string, Color> GetLightThemeColors()
        {
            return new Dictionary<string, Color>(_lightThemeColors);
        }

        /// <summary>
        /// 获取所有深色主题颜色
        /// </summary>
        public Dictionary<string, Color> GetDarkThemeColors()
        {
            return new Dictionary<string, Color>(_darkThemeColors);
        }
    }
}
