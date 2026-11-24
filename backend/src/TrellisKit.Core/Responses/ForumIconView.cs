namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A forum icon: an emoji or an image (docs/PHASE3_CONTRACT.md section 2.1).
    /// </summary>
    public class ForumIconView
    {
        #region Public-Members

        /// <summary>
        /// Emoji, or null.
        /// </summary>
        public string? Emoji { get; set; } = null;

        /// <summary>
        /// Image media identifier, or null.
        /// </summary>
        public string? MediaId { get; set; } = null;

        /// <summary>
        /// Thumbnail URL of the image, or null.
        /// </summary>
        public string? ImageUrl { get; set; } = null;

        #endregion
    }
}
