namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A member directory submission with its field rows (docs/PHASE4_CONTRACT.md sections 2.4 and 14).
    /// </summary>
    public class DirectorySubmission
    {
        #region Public-Members

        /// <summary>
        /// Identifier (dsb_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Type.
        /// </summary>
        public DirectorySubmissionTypeEnum Type { get; set; } = DirectorySubmissionTypeEnum.NewVenue;

        /// <summary>
        /// Status.
        /// </summary>
        public DirectorySubmissionStatusEnum Status { get; set; } = DirectorySubmissionStatusEnum.Pending;

        /// <summary>
        /// Target type (Venue or Instructor).
        /// </summary>
        public TargetTypeEnum TargetType { get; set; } = TargetTypeEnum.Venue;

        /// <summary>
        /// Target identifier (edits, claims, closures, and approved new entries), or null.
        /// </summary>
        public string? TargetId { get; set; } = null;

        /// <summary>
        /// Target name at submit time (or the proposed name), or null.
        /// </summary>
        public string? TargetName { get; set; } = null;

        /// <summary>
        /// The target's updatedutc the diff was computed against (stale check), or null.
        /// </summary>
        public DateTime? TargetUpdatedUtc { get; set; } = null;

        /// <summary>
        /// Submitter (null after account deletion).
        /// </summary>
        public string? SubmitterUserId { get; set; } = null;

        /// <summary>
        /// Submitter note, or null.
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// Reviewer note shown to the submitter, or null.
        /// </summary>
        public string? ReviewerNote { get; set; } = null;

        /// <summary>
        /// Reviewer, or null.
        /// </summary>
        public string? ReviewerUserId { get; set; } = null;

        /// <summary>
        /// Decision time, or null.
        /// </summary>
        public DateTime? DecidedUtc { get; set; } = null;

        /// <summary>
        /// Review target time.
        /// </summary>
        public DateTime DueUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When directory staff were told it is overdue, or null.
        /// </summary>
        public DateTime? OverdueNotifiedUtc { get; set; } = null;

        /// <summary>
        /// Proposed pin latitude, or null.
        /// </summary>
        public double? ProposedLat { get; set; } = null;

        /// <summary>
        /// Proposed pin longitude, or null.
        /// </summary>
        public double? ProposedLng { get; set; } = null;

        /// <summary>
        /// The entry an approved new submission created, or null.
        /// </summary>
        public string? ResultId { get; set; } = null;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Field rows in field and position order.
        /// </summary>
        public List<DirectorySubmissionField> Fields { get; set; } = new List<DirectorySubmissionField>();

        #endregion
    }
}
