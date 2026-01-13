using ActiproSoftware.Text;
using ActiproSoftware.Text.Tagging;
using ActiproSoftware.Text.Utility;
using ActiproSoftware.Windows.Controls.SyntaxEditor;
using ActiproSoftware.Windows.Controls.SyntaxEditor.Adornments;
using ActiproSoftware.Windows.Controls.SyntaxEditor.Adornments.Implementation;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ActiproRoslynPOC.Debugging
{
    /// <summary>
    /// Represents an adornment manager for a view that renders elapsed times.
    /// </summary>
    public class ElapsedTimeAdornmentManager : IntraTextAdornmentManagerBase<IEditorView, ElapsedTimeTag>
    {
        private static AdornmentLayerDefinition layerDefinition =
            new AdornmentLayerDefinition("ElapsedTime", new Ordering(AdornmentLayerDefinitions.TextForeground.Key, OrderPlacement.Before));

        private const double FontSizeAdjustment = 0.9;

        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        public ElapsedTimeAdornmentManager(IEditorView view) : base(view, layerDefinition) { }

        /// <summary>
        /// Adds an adornment to the AdornmentLayer.
        /// </summary>
        protected override void AddAdornment(AdornmentChangeReason reason, ITextViewLine viewLine, TagSnapshotRange<ElapsedTimeTag> tagRange, TextBounds bounds)
        {
            var boundsList = viewLine.GetTextBounds(new TextRange(tagRange.SnapshotRange.StartOffset)).ToArray();
            if ((boundsList != null) && (boundsList.Length == 1))
            {
                // Create a text block
                var adornment = new TextBlock()
                {
                    FontFamily = SystemFonts.MessageFontFamily,
                    FontSize = Math.Round(this.View.DefaultFontSize * FontSizeAdjustment, MidpointRounding.AwayFromZero),
                    Foreground = Brushes.Green,
                    Opacity = 0.8,
                    Text = tagRange.Tag.TimeSpanText
                };

                // Measure the adornment and determine its display location
                adornment.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var adornmentLocation = new Point(boundsList[0].Left + this.View.DefaultCharacterWidth, boundsList[0].Top + ((bounds.Height - adornment.DesiredSize.Height) / 2.0));

                // Add the adornment to the layer
                this.AdornmentLayer.AddAdornment(reason, adornment, adornmentLocation, tagRange.Tag.Key, removedCallback: null);
            }
        }

        /// <summary>
        /// Occurs when the manager is closed and detached from the view.
        /// </summary>
        protected override void OnClosed()
        {
            // Remove any remaining adornments
            this.AdornmentLayer.RemoveAllAdornments(AdornmentChangeReason.ManagerClosed);

            // Call the base method
            base.OnClosed();
        }
    }
}
