using ActiproSoftware.Text;
using ActiproSoftware.Text.Tagging;
using ActiproSoftware.Text.Tagging.Implementation;
using ActiproSoftware.Text.Utility;
using ActiproSoftware.Windows.Controls.SyntaxEditor;
using ActiproSoftware.Windows.Controls.SyntaxEditor.Highlighting;
using ActiproSoftware.Windows.Controls.SyntaxEditor.Highlighting.Implementation;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Windows.Media;

namespace ActiproRoslynPOC.Themes
{
    /// <summary>
    /// 选择匹配高亮 Tagger
    /// 当用户选择文本时，高亮文档中所有相同的文本
    /// 参考 UiPath Studio 的 HighlightSelectionMatchesTagger 实现
    /// </summary>
    public class HighlightSelectionMatchesTagger : TaggerBase<IClassificationTag>
    {
        private ICodeDocument _document;
        private readonly ConcurrentBag<TextRange> _highlightedRanges = new ConcurrentBag<TextRange>();
        private readonly object _lock = new object();
        private bool _isDarkTheme = false;

        /// <summary>
        /// 构造函数
        /// </summary>
        public HighlightSelectionMatchesTagger(ICodeDocument document)
            : base("HighlightSelectionMatchesTagger", null, document, true)
        {
            _document = document;
        }

        /// <summary>
        /// 高亮指定范围
        /// </summary>
        public void HighlightRange(TextSnapshotRange snapshotRange, bool isDarkTheme)
        {
            lock (_lock)
            {
                _isDarkTheme = isDarkTheme;
                _highlightedRanges.Add(snapshotRange.TextRange);
            }

            // 通知标签已更改
            OnTagsChanged(new TagsChangedEventArgs(snapshotRange));
        }

        /// <summary>
        /// 清除所有高亮
        /// </summary>
        public void Clear()
        {
            TextRange fullRange;
            lock (_lock)
            {
                if (_document?.CurrentSnapshot == null)
                    return;

                fullRange = _document.CurrentSnapshot.TextRange;

                // 清空集合
                while (_highlightedRanges.TryTake(out _)) { }
            }

            // 通知整个文档的标签已更改
            if (_document?.CurrentSnapshot != null)
            {
                OnTagsChanged(new TagsChangedEventArgs(
                    new TextSnapshotRange(_document.CurrentSnapshot, fullRange)));
            }
        }

        /// <summary>
        /// 获取指定范围内的标签
        /// </summary>
        public override IEnumerable<TagSnapshotRange<IClassificationTag>> GetTags(
            NormalizedTextSnapshotRangeCollection snapshotRanges, object parameter)
        {
            if (snapshotRanges == null || snapshotRanges.Count == 0)
                yield break;

            var snapshot = snapshotRanges[0].Snapshot;
            if (snapshot == null)
                yield break;

            lock (_lock)
            {
                foreach (var range in _highlightedRanges)
                {
                    // 确保范围有效
                    if (range.StartOffset >= 0 &&
                        range.EndOffset <= snapshot.Length &&
                        range.StartOffset < range.EndOffset)
                    {
                        var snapshotRange = new TextSnapshotRange(snapshot, range);

                        // 检查是否与请求的范围有交集
                        foreach (var requestedRange in snapshotRanges)
                        {
                            if (snapshotRange.IntersectsWith(requestedRange))
                            {
                                var tag = new ClassificationTag(CustomClassificationTypes.SelectionMatchHighlight);
                                yield return new TagSnapshotRange<IClassificationTag>(snapshotRange, tag);
                                break;
                            }
                        }
                    }
                }
            }
        }
    }
}
