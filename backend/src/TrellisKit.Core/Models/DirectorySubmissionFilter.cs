namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Filters of submission enumerations (docs/PHASE4_CONTRACT.md section 3.7).
    /// </summary>
    public class DirectorySubmissionFilter
    {
        #region Public-Members

        /// <summary>
        /// Any of these statuses (empty for all).
        /// </summary>
        public List<DirectorySubmissionStatusEnum> Statuses { get; set; } = new List<DirectorySubmissionStatusEnum>();

        /// <summary>
        /// Any of these types (empty for all).
        /// </summary>
        public List<DirectorySubmissionTypeEnum> Types { get; set; } = new List<DirectorySubmissionTypeEnum>();

        /// <summary>
        /// Submitter, or null.
        /// </summary>
        public string? SubmitterUserId { get; set; } = null;

        /// <summary>
        /// Target, or null.
        /// </summary>
        public string? TargetId { get; set; } = null;

        #endregion
    }
}
