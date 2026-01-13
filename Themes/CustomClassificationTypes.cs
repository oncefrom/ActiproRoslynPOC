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
    }
}
