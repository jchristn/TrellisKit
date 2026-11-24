namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The source of a feed card (a promoted thread carries its forum and title).
    /// </summary>
    public class FeedPostSourceView : ContentTarget
    {
        #region Public-Members

        /// <summary>
        /// Forum of a promoted thread, or null.
        /// </summary>
        public string? ForumId { get; set; } = null;

        /// <summary>
        /// Forum name of a promoted thread, or null.
        /// </summary>
        public string? ForumName { get; set; } = null;

        /// <summary>
        /// Title of the source, or null.
        /// </summary>
        public string? Title { get; set; } = null;

        #endregion
    }
}
