using ActiproSoftware.Text;
using ActiproSoftware.Text.Tagging;
using ActiproSoftware.Text.Tagging.Implementation;
using System;
using System.Collections.Generic;

namespace ActiproRoslynPOC.Themes
{
    /// <summary>
    /// Tagger Provider - 为文档提供 UsingDirectiveTagger
    /// </summary>
    public class UsingDirectiveTaggerProvider : ICodeDocumentTaggerProvider
    {
        /// <summary>
        /// 获取支持的标签类型
        /// </summary>
        public IEnumerable<Type> TagTypes
        {
            get { yield return typeof(IClassificationTag); }
        }

        /// <summary>
        /// 获取 Tagger (泛型方法)
        /// </summary>
        public ITagger<T> GetTagger<T>(ICodeDocument document) where T : ITag
        {
            if (document == null)
                return null;

            // 只返回 IClassificationTag 类型的 Tagger
            if (typeof(T) == typeof(IClassificationTag))
            {
                return new UsingDirectiveTagger(document) as ITagger<T>;
            }

            return null;
        }
    }
}
