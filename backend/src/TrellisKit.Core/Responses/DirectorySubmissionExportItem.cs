namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A member's directory submission in the account export.
    /// </summary>
    public class DirectorySubmissionExportItem
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
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
        /// Target type.
        /// </summary>
        public TargetTypeEnum TargetType { get; set; } = TargetTypeEnum.Venue;

        /// <summary>
        /// Target.
        /// </summary>
        public string? TargetId { get; set; } = null;

        /// <summary>
        /// Target name.
        /// </summary>
        public string? TargetName { get; set; } = null;

        /// <summary>
        /// Note.
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// Reviewer note.
        /// </summary>
        public string? ReviewNote { get; set; } = null;

        /// <summary>
        /// Proposed changes.
        /// </summary>
        public List<SubmissionChangeView> Changes { get; set; } = new List<SubmissionChangeView>();

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Decided.
        /// </summary>
        public DateTime? DecidedUtc { get; set; } = null;

        #endregion
    }
}
