namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A highlighted range in a search snippet.
    /// </summary>
    public class SnippetHighlight
    {
        #region Public-Members

        /// <summary>
        /// Offset into the snippet text.
        /// </summary>
        public int Start { get; set; } = 0;

        /// <summary>
        /// Length of the match.
        /// </summary>
        public int Length { get; set; } = 0;

        #endregion
    }
}
