using ActiproSoftware.Text;
using ActiproSoftware.Text.Tagging;
using ActiproSoftware.Text.Tagging.Implementation;
using System;
using System.Collections.Generic;

namespace ActiproRoslynPOC.Themes
{
    /// <summary>
    /// 引用高亮 Tagger Provider
    /// 为文档提供 HighlightReferencesTagger
    /// </summary>
    public class HighlightReferencesTaggerProvider : ICodeDocumentTaggerProvider
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
                if (!document.Properties.TryGetValue<HighlightReferencesTagger>(
                    typeof(HighlightReferencesTagger), out var tagger))
                {
                    tagger = new HighlightReferencesTagger(document);
                    document.Properties[typeof(HighlightReferencesTagger)] = tagger;
                }
                return tagger as ITagger<T>;
            }

            return null;
        }
    }
}
