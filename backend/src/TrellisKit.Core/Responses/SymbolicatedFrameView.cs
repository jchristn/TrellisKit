namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One stack frame before and after symbolication.
    /// </summary>
    public class SymbolicatedFrameView
    {
        /// <summary>
        /// The frame as reported.
        /// </summary>
        public string Raw { get; set; } = String.Empty;

        /// <summary>
        /// Reported function name, or null.
        /// </summary>
        public string? Function { get; set; } = null;

        /// <summary>
        /// Reported file, or null.
        /// </summary>
        public string? File { get; set; } = null;

        /// <summary>
        /// Reported line, or null.
        /// </summary>
        public int? Line { get; set; } = null;

        /// <summary>
        /// Reported column, or null.
        /// </summary>
        public int? Column { get; set; } = null;

        /// <summary>
        /// Original position, or null when unmapped.
        /// </summary>
        public OriginalPositionView? Original { get; set; } = null;
    }
}
