namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A member's messaging settings (GET and PUT /api/v1/me/messaging/settings).
    /// </summary>
    public class MessagingSettingsView
    {
        #region Public-Members

        /// <summary>
        /// Who may start conversations with the member.
        /// </summary>
        public DmPolicyEnum DmPolicy { get; set; } = DmPolicyEnum.Everyone;

        /// <summary>
        /// Reciprocal read receipts.
        /// </summary>
        public bool ReadReceipts { get; set; } = true;

        /// <summary>
        /// Reciprocal typing indicators.
        /// </summary>
        public bool TypingIndicators { get; set; } = true;

        /// <summary>
        /// Push preview mode.
        /// </summary>
        public PushPreviewEnum PushPreview { get; set; } = PushPreviewEnum.SenderOnly;

        /// <summary>
        /// Whether direct message pushes ignore notification hours (resolved decision 12).
        /// </summary>
        public bool ExemptFromNotificationHours { get; set; } = false;

        /// <summary>
        /// Last change (UTC), or null for defaults.
        /// </summary>
        public DateTime? UpdatedUtc { get; set; } = null;

        #endregion
    }
}
