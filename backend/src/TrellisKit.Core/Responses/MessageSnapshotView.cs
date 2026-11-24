namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A frozen report snapshot (GET /api/v1/admin/moderation/messaging/reports/{reportId}/snapshot).
    /// </summary>
    public class MessageSnapshotView
    {
        #region Public-Members

        /// <summary>
        /// Snapshot identifier (mrs_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Report identifier.
        /// </summary>
        public string ReportId { get; set; } = String.Empty;

        /// <summary>
        /// Conversation identifier.
        /// </summary>
        public string ConversationId { get; set; } = String.Empty;

        /// <summary>
        /// The reported message (the newest captured message for a conversation report), or null.
        /// </summary>
        public string? ReportedMessageId { get; set; } = null;

        /// <summary>
        /// Reporter.
        /// </summary>
        public UserSummary? Reporter { get; set; } = null;

        /// <summary>
        /// Captured (UTC).
        /// </summary>
        public DateTime CapturedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Messages in sequence order (bounded: messaging.reportContextMessages plus the reported messages, at most 51).
        /// </summary>
        public List<MessageSnapshotItemView> Messages
        {
            get { return _Messages; }
            set { _Messages = value ?? new List<MessageSnapshotItemView>(); }
        }

        #endregion

        #region Private-Members

        private List<MessageSnapshotItemView> _Messages = new List<MessageSnapshotItemView>();

        #endregion
    }
}
