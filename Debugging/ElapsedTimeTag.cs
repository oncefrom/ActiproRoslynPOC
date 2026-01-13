using ActiproSoftware.Text.Tagging;
using System;
using System.Windows;

namespace ActiproRoslynPOC.Debugging
{
    /// <summary>
    /// Provides an IIntraTextSpacerTag implementation that reserves intra-text space for elapsed time display.
    /// </summary>
    public class ElapsedTimeTag : IIntraTextSpacerTag
    {
        /// <summary>
        /// Initializes an instance of the class.
        /// </summary>
        public ElapsedTimeTag(TimeSpan timeSpan)
        {
            this.TimeSpan = timeSpan;
        }

        /// <summary>
        /// Gets the spacer baseline.
        /// </summary>
        public double Baseline => 0.0;

        /// <summary>
        /// Gets whether the spacer appears before the tagged range.
        /// </summary>
        public bool IsSpacerBefore => false;

        /// <summary>
        /// Gets an object that can be used to uniquely identify the spacer.
        /// </summary>
        public object Key => this;

        /// <summary>
        /// Gets or sets the spacer size.
        /// </summary>
        public Size Size { get; set; }

        /// <summary>
        /// Gets the elapsed time.
        /// </summary>
        public TimeSpan TimeSpan { get; }

        /// <summary>
        /// Gets the text to display.
        /// </summary>
        public string TimeSpanText
        {
            get
            {
                return $"{this.TimeSpan.TotalMilliseconds.ToString("N0")}ms";
            }
        }
    }
}
