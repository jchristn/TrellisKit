namespace TrellisKit.Server.Services.Messaging
{
    using System;
    using TrellisKit.Core.Models;

    /// <summary>
    /// What the eligibility rules need to know about the other member of a conversation, loaded in one batch per page
    /// (docs/PHASE5_CONTRACT.md section 3.2).
    /// Thread safety: not thread safe.
    /// </summary>
    public class MessagingPeer
    {
        #region Public-Members

        /// <summary>
        /// Member identifier.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// The member's account, or null when it does not exist in the property.
        /// </summary>
        public User? User { get; set; } = null;

        /// <summary>
        /// Whether the member currently has messaging access (exists, not deleted or banned, and meets the access model).
        /// </summary>
        public bool HasAccess { get; set; } = false;

        /// <summary>
        /// Whether the member is staff (staff always pass a recipient's message policy and cannot be blocked).
        /// </summary>
        public bool IsStaff { get; set; } = false;

        /// <summary>
        /// Whether the viewer blocked the member.
        /// </summary>
        public bool BlockedByViewer { get; set; } = false;

        /// <summary>
        /// Whether the member blocked the viewer (never revealed to the viewer).
        /// </summary>
        public bool BlocksViewer { get; set; } = false;

        /// <summary>
        /// Whether the member knows the viewer (the member messaged or accepted the viewer before).
        /// </summary>
        public bool KnowsViewer { get; set; } = false;

        /// <summary>
        /// The member's messaging settings (defaults when none are stored).
        /// </summary>
        public MemberMessagingSettings Settings { get; set; } = new MemberMessagingSettings();

        #endregion
    }
}
