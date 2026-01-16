using ActiproSoftware.Text;
using ActiproSoftware.Text.Tagging;
using ActiproSoftware.Text.Tagging.Implementation;
using System;
using System.Collections.Generic;

namespace ActiproRoslynPOC.Themes
{
    /// <summary>
    /// 选择匹配高亮 Tagger Provider
    /// 为文档提供 HighlightSelectionMatchesTagger
    /// </summary>
    public class HighlightSelectionMatchesTaggerProvider : ICodeDocumentTaggerProvider
    {
        /// <summary>
        /// 获取支持的标签类型
        /// </summary>
        public IEnumerable<Type> TagTypes
        {
            get { yield return typeof(IClassificationTag); }
        }

        /// <summary>
        /// 获取 Tagger
        /// </summary>
        public ITagger<T> GetTagger<T>(ICodeDocument document) where T : ITag
        {
            if (document == null)
                return null;

            // 只返回 IClassificationTag 类型的 Tagger
            if (typeof(T) == typeof(IClassificationTag))
            {
                // 从文档属性中获取或创建 Tagger
                if (!document.Properties.TryGetValue<HighlightSelectionMatchesTagger>(
                    typeof(HighlightSelectionMatchesTagger), out var tagger))
                {
                    tagger = new HighlightSelectionMatchesTagger(document);
                    document.Properties[typeof(HighlightSelectionMatchesTagger)] = tagger;
                }
                return tagger as ITagger<T>;
            }

            return null;
        }
    }
}
