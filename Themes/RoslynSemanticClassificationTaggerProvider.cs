using ActiproSoftware.Text;
using ActiproSoftware.Text.Tagging;
using System;
using System.Collections.Generic;

namespace ActiproRoslynPOC.Themes
{
    /// <summary>
    /// Roslyn 语义分类 Tagger Provider
    /// 实现 ICodeDocumentTaggerProvider 接口
    /// </summary>
    public class RoslynSemanticClassificationTaggerProvider : ICodeDocumentTaggerProvider
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
                if (!document.Properties.TryGetValue<RoslynSemanticClassificationTagger>(
                    typeof(RoslynSemanticClassificationTagger), out var tagger))
                {
                    tagger = new RoslynSemanticClassificationTagger(document);
                    document.Properties[typeof(RoslynSemanticClassificationTagger)] = tagger;
                    System.Diagnostics.Debug.WriteLine($"[RoslynSemanticTaggerProvider] 为文档创建 Tagger: {document.FileName}");
                }
                return tagger as ITagger<T>;
            }

            return null;
        }
    }
}
