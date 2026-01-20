using ActiproSoftware.Text;
using ActiproSoftware.Text.Tagging;
using ActiproSoftware.Text.Tagging.Implementation;
using System;
using System.Collections.Generic;

namespace ActiproRoslynPOC.Themes
{
    /// <summary>
    /// Roslyn Token Tagger Provider
    /// 提供基于 Roslyn 语义分析的 TokenTagger
    /// 参考 UiPath ExtendedCSharpTaggerProvider 实现
    /// </summary>
    public class RoslynTokenTaggerProvider : ICodeDocumentTaggerProvider
    {
        /// <summary>
        /// 获取支持的标签类型
        /// TokenTagger 提供 ITokenTag 和 IClassificationTag
        /// </summary>
        public IEnumerable<Type> TagTypes
        {
            get
            {
                yield return typeof(ITokenTag);
                yield return typeof(IClassificationTag);
            }
        }

        /// <summary>
        /// 获取 Tagger
        /// </summary>
        public ITagger<T> GetTagger<T>(ICodeDocument document) where T : ITag
        {
            if (document == null)
                return null;

            // TokenTagger 同时实现 ITagger<ITokenTag> 和 ITagger<IClassificationTag>
            if (typeof(T) == typeof(ITokenTag) || typeof(T) == typeof(IClassificationTag))
            {
                // 从文档属性中获取或创建 Tagger
                if (!document.Properties.TryGetValue<RoslynTokenTagger>(
                    typeof(RoslynTokenTagger), out var tagger))
                {
                    tagger = new RoslynTokenTagger(document);
                    document.Properties[typeof(RoslynTokenTagger)] = tagger;
                    System.Diagnostics.Debug.WriteLine($"[RoslynTokenTaggerProvider] 为文档创建 TokenTagger: {document.FileName}");
                }
                return tagger as ITagger<T>;
            }

            return null;
        }
    }
}
