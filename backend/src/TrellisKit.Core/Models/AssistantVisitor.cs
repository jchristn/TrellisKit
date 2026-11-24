namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// An anonymous visitor of public assistants.
    /// </summary>
    public class AssistantVisitor
    {
        #region Public-Members

        /// <summary>
        /// Identifier (avs_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Created time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last seen time.
        /// </summary>
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Hash of the first IP (never the IP).
        /// </summary>
        public string? IpHash { get; set; } = null;

        /// <summary>
        /// Surface of first use.
        /// </summary>
        public AiConversationSurfaceEnum Surface { get; set; } = AiConversationSurfaceEnum.Widget;

        #endregion
    }
}
