namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The original source position of a stack frame.
    /// </summary>
    public class OriginalPositionView
    {
        /// <summary>
        /// Original source file.
        /// </summary>
        public string Source { get; set; } = String.Empty;

        /// <summary>
        /// 1-based line.
        /// </summary>
        public int Line { get; set; } = 0;

        /// <summary>
        /// 1-based column.
        /// </summary>
        public int Column { get; set; } = 0;

        /// <summary>
        /// Original name, or null.
        /// </summary>
        public string? Name { get; set; } = null;
    }
}
