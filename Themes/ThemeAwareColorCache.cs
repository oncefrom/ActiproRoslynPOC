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
        /// 初始化浅色主题 - 风格：VS Code Light+ Theme
        /// 参考 VS Code light_plus.json 配置
        /// 建议编辑器背景色设置为: #FFFFFF (白色)
        /// </summary>
        private void InitializeLightTheme()
        {
            _lightThemeColors = new Dictionary<string, Color>
            {
                // 标识符 (深蓝色 - VS Code 变量颜色 #001080)
                { "Identifier", Color.FromArgb(255, 0, 16, 128) },

                // 控制关键字 (紫色 - VS Code 控制关键字 #AF00DB)
                { "KeywordControl", Color.FromArgb(255, 175, 0, 219) },

                // 方法名 (深棕色 - VS Code 函数声明 #795E26)
                { "UsedMethodName", Color.FromArgb(255, 121, 94, 38) },

                // 类引用 (青色 - VS Code 类型 #267F99)
                { "ClassObjectReference", Color.FromArgb(255, 38, 127, 153) },

                // 接口引用 (青色 - VS Code 类型 #267F99)
                { "InterfaceObjectReference", Color.FromArgb(255, 38, 127, 153) },

                // 结构体引用 (青色 - VS Code 类型 #267F99)
                { "StructureObjectReference", Color.FromArgb(255, 38, 127, 153) },

                // 参数/局部变量 (深蓝色 - VS Code 变量 #001080)
                { "ParametersObjectReference", Color.FromArgb(255, 0, 16, 128) },

                // 一般关键字 (蓝色 - VS Code 关键字 #0000FF)
                { "Keyword", Color.FromArgb(255, 0, 0, 255) },

                // 字符串 (棕红色 - VS Code 字符串 #A31515)
                { "String", Color.FromArgb(255, 163, 21, 21) },

                // 注释 (绿色 - VS Code 注释 #008000)
                { "Comment", Color.FromArgb(255, 0, 128, 0) },

                // 数字 (绿色 - VS Code 数字 #098658)
                { "Number", Color.FromArgb(255, 9, 134, 88) },

                // 操作符 (黑色)
                { "Operator", Color.FromArgb(255, 0, 0, 0) },

                // 预处理指令 (灰色 #808080)
                { "PreprocessorKeyword", Color.FromArgb(255, 128, 128, 128) },

                // 未使用的代码 (浅灰色，带透明度)
                { "UnnecessaryCode", Color.FromArgb(100, 128, 128, 128) },

                // 选择匹配高亮背景 (淡黄色 - VS Code 风格)
                { "SelectionMatchHighlight", Color.FromArgb(80, 255, 235, 59) },

                // 引用高亮背景 (淡蓝色 - VS Code 风格)
                { "ReferenceHighlight", Color.FromArgb(80, 173, 214, 255) }
            };
        }

        /// <summary>
        /// 初始化深色主题 - 风格：VS Code Dark+ Theme
        /// 参考 VS Code dark_plus.json 配置
        /// 建议编辑器背景色设置为: #1E1E1E (VS Code 深色)
        /// </summary>
        private void InitializeDarkTheme()
        {
            _darkThemeColors = new Dictionary<string, Color>
            {
                // 标识符 (浅蓝色 - VS Code 变量 #9CDCFE)
                { "Identifier", Color.FromArgb(255, 156, 220, 254) },

                // 控制关键字 (粉紫色 - VS Code 控制关键字 #C586C0)
                { "KeywordControl", Color.FromArgb(255, 197, 134, 192) },

                // 方法名 (淡黄色 - VS Code 函数声明 #DCDCAA)
                { "UsedMethodName", Color.FromArgb(255, 220, 220, 170) },

                // 类引用 (青绿色 - VS Code 类型 #4EC9B0)
                { "ClassObjectReference", Color.FromArgb(255, 78, 201, 176) },

                // 接口引用 (浅绿色 - VS Code 接口 #B8D7A3)
                { "InterfaceObjectReference", Color.FromArgb(255, 184, 215, 163) },

                // 结构体引用 (青绿色 - VS Code 类型 #4EC9B0)
                { "StructureObjectReference", Color.FromArgb(255, 78, 201, 176) },

                // 参数/局部变量 (浅蓝色 - VS Code 变量 #9CDCFE)
                { "ParametersObjectReference", Color.FromArgb(255, 156, 220, 254) },

                // 一般关键字 (蓝色 - VS Code 关键字 #569CD6)
                { "Keyword", Color.FromArgb(255, 86, 156, 214) },

                // 字符串 (橙色 - VS Code 字符串 #CE9178)
                { "String", Color.FromArgb(255, 206, 145, 120) },

                // 注释 (绿色 - VS Code 注释 #6A9955)
                { "Comment", Color.FromArgb(255, 106, 153, 85) },

                // 数字 (浅绿色 - VS Code 数字 #B5CEA8)
                { "Number", Color.FromArgb(255, 181, 206, 168) },

                // 操作符 (白色 #D4D4D4)
                { "Operator", Color.FromArgb(255, 212, 212, 212) },

                // 预处理指令 (灰色 #9B9B9B)
                { "PreprocessorKeyword", Color.FromArgb(255, 155, 155, 155) },

                // 未使用的代码 (深灰色，带透明度)
                { "UnnecessaryCode", Color.FromArgb(80, 100, 100, 100) },

                // 选择匹配高亮背景 (深金色 - VS Code 风格)
                { "SelectionMatchHighlight", Color.FromArgb(100, 255, 200, 50) },

                // 引用高亮背景 (深紫蓝色 - VS Code 风格)
                { "ReferenceHighlight", Color.FromArgb(80, 81, 92, 106) }
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
