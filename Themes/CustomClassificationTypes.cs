using ActiproSoftware.Text;
using ActiproSoftware.Text.Implementation;

namespace ActiproRoslynPOC.Themes
{
    /// <summary>
    /// 自定义分类类型 - 用于 using 引用等特殊高亮
    /// </summary>
    public static class CustomClassificationTypes
    {
        private static IClassificationType _unnecessaryCode;
        private static IClassificationType _selectionMatchHighlight;
        private static IClassificationType _referenceHighlight;

        /// <summary>
        /// 未使用的代码（如未使用的 using 引用）
        /// </summary>
        public static IClassificationType UnnecessaryCode
        {
            get
            {
                if (_unnecessaryCode == null)
                {
                    _unnecessaryCode = new ClassificationType("UnnecessaryCode", "Unnecessary Code");
                }
                return _unnecessaryCode;
            }
        }

        /// <summary>
        /// 选择匹配高亮（选中文本时高亮所有相同文本）
        /// </summary>
        public static IClassificationType SelectionMatchHighlight
        {
            get
            {
                if (_selectionMatchHighlight == null)
                {
                    _selectionMatchHighlight = new ClassificationType("SelectionMatchHighlight", "Selection Match Highlight");
                }
                return _selectionMatchHighlight;
            }
        }

        /// <summary>
        /// 引用高亮（光标所在标识符的所有引用）
        /// </summary>
        public static IClassificationType ReferenceHighlight
        {
            get
            {
                if (_referenceHighlight == null)
                {
                    _referenceHighlight = new ClassificationType("ReferenceHighlight", "Reference Highlight");
                }
                return _referenceHighlight;
            }
        }
    }
}
