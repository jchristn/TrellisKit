namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The scope of a moderation queue row (docs/PHASE3_CONTRACT.md section 11.4).
    /// </summary>
    public class ModerationScopeView
    {
        #region Public-Members

        /// <summary>
        /// Feed or Forum.
        /// </summary>
        public string Type { get; set; } = "Feed";

        /// <summary>
        /// Forum identifier, or * for the feed.
        /// </summary>
        public string Id { get; set; } = "*";

        /// <summary>
        /// Forum name, or null.
        /// </summary>
        public string? Name { get; set; } = null;

        #endregion
    }
}
