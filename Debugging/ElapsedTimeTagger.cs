using ActiproSoftware.Text;
using ActiproSoftware.Text.Tagging;
using ActiproSoftware.Text.Tagging.Implementation;

namespace ActiproRoslynPOC.Debugging
{
    /// <summary>
    /// Provides ElapsedTimeTag objects over text ranges.
    /// </summary>
    public class ElapsedTimeTagger : CollectionTagger<IIntraTextSpacerTag>
    {
        /// <summary>
        /// Initializes an instance of the class.
        /// </summary>
        public ElapsedTimeTagger(ICodeDocument document) : base(nameof(ElapsedTimeTagger), null, document, true) { }

        /// <summary>
        /// Returns whether the specified tag's snapshot range intersects with the requested snapshot range.
        /// </summary>
        protected override bool IntersectsWith(TextSnapshotRange requestedSnapshotRange, IIntraTextSpacerTag tag, TextSnapshotRange tagSnapshotRange)
        {
            // If the tag's spacer is after the text range, also allow intersection at the end offset
            return base.IntersectsWith(requestedSnapshotRange, tag, tagSnapshotRange)
                || (!tag.IsSpacerBefore && (tagSnapshotRange.EndOffset == requestedSnapshotRange.StartOffset));
        }
    }
}
