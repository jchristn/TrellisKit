namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One message of a report snapshot.
    /// </summary>
    public class MessageReportSnapshotItem
    {
        #region Public-Members

        /// <summary>
        /// Identifier (mri_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Snapshot identifier.
        /// </summary>
        public string SnapshotId { get; set; } = String.Empty;

        /// <summary>
        /// Message identifier.
        /// </summary>
        public string MessageId { get; set; } = String.Empty;

        /// <summary>
        /// Sequence number.
        /// </summary>
        public long Seq { get; set; } = 0;

        /// <summary>
        /// Author, or null.
        /// </summary>
        public string? AuthorUserId { get; set; } = null;

        /// <summary>
        /// Text at capture time (empty for tombstones).
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
        /// Attachment references (media ids and kinds), or null.
        /// </summary>
        public string? AttachmentSummary { get; set; } = null;

        /// <summary>
        /// Edited (UTC), or null.
        /// </summary>
        public DateTime? EditedUtc { get; set; } = null;

        /// <summary>
        /// Message created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
