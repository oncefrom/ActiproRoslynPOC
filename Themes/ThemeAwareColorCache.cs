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

                // 方法名 (深黄色/棕色 - UiPath 方法名)
                { "UsedMethodName", Color.FromArgb(255, 128, 128, 0) },

                // 类引用 (青绿色 - UiPath 类型名称)
                { "ClassObjectReference", Color.FromArgb(255, 43, 145, 175) },

                // 接口引用 (青绿色 - 与类相同)
                { "InterfaceObjectReference", Color.FromArgb(255, 43, 145, 175) },

                // 结构体引用 (青绿色)
                { "StructureObjectReference", Color.FromArgb(255, 43, 145, 175) },

                // 参数/局部变量 (黑色)
                { "ParametersObjectReference", Color.FromArgb(255, 0, 0, 0) },

                // 一般关键字 (红色 - UiPath 标准)
                { "Keyword", Color.FromArgb(255, 255, 128, 0) },

                // 字符串 (棕红色 - UiPath 字符串颜色)
                { "String", Color.FromArgb(255, 163, 21, 21) },

                // 注释 (绿色 - UiPath 注释颜色)
                { "Comment", Color.FromArgb(255, 0, 128, 0) },

                // 数字 (黑色)
                { "Number", Color.FromArgb(255, 0, 0, 0) },

                // 操作符 (黑色)
                { "Operator", Color.FromArgb(255, 0, 0, 0) },

                // 预处理指令 (灰色)
                { "PreprocessorKeyword", Color.FromArgb(255, 128, 128, 128) },

                // 未使用的代码 (浅灰色，带透明度)
                { "UnnecessaryCode", Color.FromArgb(100, 128, 128, 128) }
            };
        }

        /// <summary>
        /// 初始化深色主题 - 风格：Dracula/Cyberpunk (霓虹赛博朋克)
        /// 建议编辑器背景色设置为: #282A36 (深蓝紫) 或 #1E1E1E (深黑)
        /// </summary>
        private void InitializeDarkTheme()
        {
            _darkThemeColors = new Dictionary<string, Color>
            {
                // 标识符 (亮白色 - 强对比)
                { "Identifier", Color.FromArgb(255, 248, 248, 242) },

                // 控制关键字 (霓虹粉)
                { "KeywordControl", Color.FromArgb(255, 255, 121, 198) },

                // 方法名 (亮绿色)
                { "UsedMethodName", Color.FromArgb(255, 80, 250, 123) },

                // 类引用 (青蓝色 - 像冰一样)
                { "ClassObjectReference", Color.FromArgb(255, 139, 233, 253) },
                
                // 接口引用 (淡紫色 - 区分于类)
                { "InterfaceObjectReference", Color.FromArgb(255, 189, 147, 249) },
                
                // 结构体引用 (青蓝色)
                { "StructureObjectReference", Color.FromArgb(255, 139, 233, 253) },

                // 参数 (橘色 - 非常醒目)
                { "ParametersObjectReference", Color.FromArgb(255, 255, 184, 108) },

                // 一般关键字 (霓虹粉)
                { "Keyword", Color.FromArgb(255, 255, 121, 198) },

                // 字符串 (亮黄色)
                { "String", Color.FromArgb(255, 241, 250, 140) },

                // 注释 (灰紫色 - 低调融入背景)
                { "Comment", Color.FromArgb(255, 98, 114, 164) },

                // 数字 (淡紫色)
                { "Number", Color.FromArgb(255, 189, 147, 249) },

                // 操作符 (霓虹粉)
                { "Operator", Color.FromArgb(255, 255, 121, 198) },

                // 预处理指令 (灰蓝)
                { "PreprocessorKeyword", Color.FromArgb(255, 98, 114, 164) },

                // 未使用的代码 (深灰色，带透明度，制造"隐形"效果)
                { "UnnecessaryCode", Color.FromArgb(80, 98, 114, 164) }
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
