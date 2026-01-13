using ActiproSoftware.Text;
using ActiproSoftware.Text.Tagging;
using ActiproSoftware.Text.Tagging.Implementation;
using System;
using System.Collections.Generic;

namespace ActiproRoslynPOC.Themes
{
    /// <summary>
    /// 语义分类 Tagger Provider - 提供语义级别的语法高亮
    /// </summary>
    public class SemanticClassificationTaggerProvider : ICodeDocumentTaggerProvider
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
                return new SemanticClassificationTagger(document) as ITagger<T>;
            }

            return null;
        }
    }
}
