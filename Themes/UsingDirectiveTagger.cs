using ActiproSoftware.Text;
using ActiproSoftware.Text.Tagging;
using ActiproSoftware.Text.Tagging.Implementation;
using ActiproSoftware.Text.Utility;
using System;
using System.Collections.Generic;

namespace ActiproRoslynPOC.Themes
{
    /// <summary>
    /// 自定义 Tagger - 标记未使用的 using 指令
    /// </summary>
    public class UsingDirectiveTagger : TaggerBase<IClassificationTag>
    {
        private ICodeDocument _document;
        private bool _isUpdateScheduled;

        /// <summary>
        /// 构造函数
        /// </summary>
        public UsingDirectiveTagger(ICodeDocument document) : base("UsingDirectiveTagger", null, document, true)
        {
            _document = document;

            // 监听文档变化
            if (document != null)
            {
                document.TextChanged += OnDocumentTextChanged;
            }
        }

        /// <summary>
        /// 文档内容变化时
        /// </summary>
        private void OnDocumentTextChanged(object sender, TextSnapshotChangedEventArgs e)
        {
            if (!_isUpdateScheduled)
            {
                _isUpdateScheduled = true;

                // 延迟更新，避免频繁重新计算
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        _isUpdateScheduled = false;
                        UpdateTags();
                    }),
                    System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        /// <summary>
        /// 更新标签
        /// </summary>
        private void UpdateTags()
        {
            if (_document == null)
                return;

            // 通知所有标签可能已更改
            var snapshot = _document.CurrentSnapshot;
            if (snapshot != null)
            {
                this.OnTagsChanged(new TagsChangedEventArgs(new TextSnapshotRange(snapshot, snapshot.TextRange)));
            }
        }

        /// <summary>
        /// 获取指定范围内的标签 (带参数)
        /// </summary>
        public override IEnumerable<TagSnapshotRange<IClassificationTag>> GetTags(NormalizedTextSnapshotRangeCollection snapshotRanges, object parameter)
        {
            if (snapshotRanges == null || snapshotRanges.Count == 0)
                yield break;

            var snapshot = snapshotRanges[0].Snapshot;
            if (snapshot == null)
                yield break;

            // 获取完整代码
            string code = snapshot.Text;

            // 分析未使用的 using
            var unusedRanges = UsingDirectiveAnalyzer.AnalyzeUnusedUsings(code);

            // 为每个未使用的 using 创建分类标签
            foreach (var range in unusedRanges)
            {
                // 确保范围有效
                if (range.StartOffset >= 0 && range.EndOffset <= snapshot.Length && range.StartOffset < range.EndOffset)
                {
                    var textRange = new TextRange(range.StartOffset, range.EndOffset);
                    var snapshotRange = new TextSnapshotRange(snapshot, textRange);

                    // 创建分类标签
                    var tag = new ClassificationTag(CustomClassificationTypes.UnnecessaryCode);

                    yield return new TagSnapshotRange<IClassificationTag>(snapshotRange, tag);
                }
            }
        }
    }
}
