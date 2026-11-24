namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of PUT /api/v1/me/messaging/settings (omitted fields keep their value).
    /// </summary>
    public class MessagingSettingsRequest
    {
        #region Public-Members

        /// <summary>
        /// Everyone, Known, or Nobody.
        /// </summary>
        public string? DmPolicy { get; set; } = null;

        /// <summary>
        /// Read receipts.
        /// </summary>
        public bool? ReadReceipts { get; set; } = null;

        /// <summary>
        /// Typing indicators.
        /// </summary>
        public bool? TypingIndicators { get; set; } = null;

        /// <summary>
        /// SenderOnly, Full, or Hidden.
        /// </summary>
        public string? PushPreview { get; set; } = null;

        /// <summary>
        /// Exempt direct message pushes from notification hours.
        /// </summary>
        public bool? ExemptFromNotificationHours { get; set; } = null;

        #endregion
    }
}
