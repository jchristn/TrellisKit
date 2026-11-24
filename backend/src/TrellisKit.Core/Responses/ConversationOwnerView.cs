namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Owner of a conversation (administrator views).
    /// </summary>
    public class ConversationOwnerView
    {
        #region Public-Members

        /// <summary>
        /// Member or Visitor.
        /// </summary>
        public string Type { get; set; } = String.Empty;

        /// <summary>
        /// Member id.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Visitor id.
        /// </summary>
        public string? VisitorId { get; set; } = null;

        /// <summary>
        /// Member summary.
        /// </summary>
        public UserSummary? Member { get; set; } = null;

        #endregion
    }
}
