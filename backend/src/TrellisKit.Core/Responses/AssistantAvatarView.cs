namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Assistant avatar.
    /// </summary>
    public class AssistantAvatarView
    {
        #region Public-Members

        /// <summary>
        /// Media id.
        /// </summary>
        public string? MediaId { get; set; } = null;

        /// <summary>
        /// Image URL.
        /// </summary>
        public string? ImageUrl { get; set; } = null;

        /// <summary>
        /// Emoji.
        /// </summary>
        public string? Emoji { get; set; } = null;

        #endregion
    }
}
