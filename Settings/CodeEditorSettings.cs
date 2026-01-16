using System;

namespace ActiproRoslynPOC.Settings
{
    /// <summary>
    /// 代码编辑器设置模型
    /// 参考 UiPath Studio 的 CodeEditorSettings 实现
    /// </summary>
    public class CodeEditorSettings
    {
        #region 字体设置

        /// <summary>
        /// 字体名称
        /// </summary>
        public string FontName { get; set; } = "Consolas";

        /// <summary>
        /// 字体大小
        /// </summary>
        public double FontSize { get; set; } = 14;

        #endregion

        #region 编辑行为

        /// <summary>
        /// 自动将 Tab 转换为空格
        /// </summary>
        public bool AutoConvertTabToSpace { get; set; } = true;

        /// <summary>
        /// Tab 宽度（空格数）
        /// </summary>
        public int TabSize { get; set; } = 4;

        #endregion

        #region 显示设置

        /// <summary>
        /// 显示空白字符
        /// </summary>
        public bool ViewWhiteSpace { get; set; } = false;

        /// <summary>
        /// 高亮当前行
        /// </summary>
        public bool HighlightCurrentLine { get; set; } = true;

        /// <summary>
        /// 显示缩进参考线
        /// </summary>
        public bool ShowStructureGuideLines { get; set; } = true;

        /// <summary>
        /// 显示行号
        /// </summary>
        public bool AreLineNumbersVisible { get; set; } = true;

        /// <summary>
        /// 显示选择边距
        /// </summary>
        public bool IsSelectionMarginVisible { get; set; } = true;

        /// <summary>
        /// 显示指示器边距（断点等）
        /// </summary>
        public bool IsIndicatorMarginVisible { get; set; } = true;

        #endregion

        #region 高级功能

        /// <summary>
        /// 显示选择匹配（选中文本时高亮所有相同文本）
        /// </summary>
        public bool ShowSelectionMatches { get; set; } = true;

        /// <summary>
        /// 高亮引用（光标所在标识符的所有引用）
        /// </summary>
        public bool HighlightReferences { get; set; } = true;

        /// <summary>
        /// 显示错误波浪线
        /// </summary>
        public bool AreErrorSquigglesVisible { get; set; } = true;

        /// <summary>
        /// 启用词补全（IntelliSense）
        /// </summary>
        public bool EnableWordCompletion { get; set; } = true;

        /// <summary>
        /// 自动显示参数信息
        /// </summary>
        public bool AutoShowParameterInfo { get; set; } = true;

        #endregion

        #region 克隆

        /// <summary>
        /// 创建设置的深拷贝
        /// </summary>
        public CodeEditorSettings Clone()
        {
            return new CodeEditorSettings
            {
                FontName = this.FontName,
                FontSize = this.FontSize,
                AutoConvertTabToSpace = this.AutoConvertTabToSpace,
                TabSize = this.TabSize,
                ViewWhiteSpace = this.ViewWhiteSpace,
                HighlightCurrentLine = this.HighlightCurrentLine,
                ShowStructureGuideLines = this.ShowStructureGuideLines,
                AreLineNumbersVisible = this.AreLineNumbersVisible,
                IsSelectionMarginVisible = this.IsSelectionMarginVisible,
                IsIndicatorMarginVisible = this.IsIndicatorMarginVisible,
                ShowSelectionMatches = this.ShowSelectionMatches,
                HighlightReferences = this.HighlightReferences,
                AreErrorSquigglesVisible = this.AreErrorSquigglesVisible,
                EnableWordCompletion = this.EnableWordCompletion,
                AutoShowParameterInfo = this.AutoShowParameterInfo
            };
        }

        #endregion
    }
}
