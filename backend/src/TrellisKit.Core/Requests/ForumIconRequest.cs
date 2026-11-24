namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// A forum icon in a request.
    /// </summary>
    public class ForumIconRequest
    {
        #region Public-Members

        /// <summary>
        /// One emoji grapheme, or null.
        /// </summary>
        public string? Emoji { get; set; } = null;

        /// <summary>
        /// A Ready image owned by an administrator, or null.
        /// </summary>
        public string? MediaId { get; set; } = null;

        #endregion
    }
}
