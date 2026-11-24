namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A member's messaging settings (docs/PHASE5_CONTRACT.md section 1.2 and resolved decision 12).
    /// </summary>
    public class MemberMessagingSettings
    {
        #region Public-Members

        /// <summary>
        /// Member.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

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
        /// Whether direct message pushes ignore the member's notification hours.
        /// </summary>
        public bool IgnoreNotificationHours { get; set; } = false;

        /// <summary>
        /// Stored update time, or null for defaults.
        /// </summary>
        public DateTime? UpdatedUtc { get; set; } = null;

        #endregion
    }
}
