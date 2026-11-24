namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One message of a report snapshot (moderators only).
    /// </summary>
    public class MessageSnapshotItemView
    {
        #region Public-Members

        /// <summary>
        /// Snapshot item identifier (mri_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Message identifier.
        /// </summary>
        public string MessageId { get; set; } = String.Empty;

        /// <summary>
        /// Sequence number.
        /// </summary>
        public long Seq { get; set; } = 0;

        /// <summary>
        /// Author, or null after the author's account was deleted.
        /// </summary>
        public UserSummary? Author { get; set; } = null;

        /// <summary>
        /// Text at capture time (empty for tombstones, shown as "Message deleted").
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// Text before the edit, or null.
        /// </summary>
        public string? OriginalBody { get; set; } = null;

        /// <summary>
        /// State at capture time.
        /// </summary>
        public MessageStateEnum State { get; set; } = MessageStateEnum.Visible;

        /// <summary>
        /// Whether this is the reported message.
        /// </summary>
        public bool IsReported { get; set; } = false;

        /// <summary>
        /// Attachment references, or null.
        /// </summary>
        public string? AttachmentSummary { get; set; } = null;

        /// <summary>
        /// Edited (UTC), or null.
        /// </summary>
        public DateTime? EditedUtc { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
