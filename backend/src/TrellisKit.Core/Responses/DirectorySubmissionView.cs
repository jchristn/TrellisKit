namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A directory submission (docs/PHASE4_CONTRACT.md section 2.4).
    /// </summary>
    public class DirectorySubmissionView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (empty on a Draft response).
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
        /// Target.
        /// </summary>
        public SubmissionTargetView Target { get; set; } = new SubmissionTargetView();

        /// <summary>
        /// Submitter ("Deleted member" after account deletion).
        /// </summary>
        public UserSummary? Submitter { get; set; } = null;

        /// <summary>
        /// Note.
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// Field changes (at most 40 fields).
        /// </summary>
        public List<SubmissionChangeView> Changes { get; set; } = new List<SubmissionChangeView>();

        /// <summary>
        /// Proposed pin, or null.
        /// </summary>
        public GeoView? ProposedGeo { get; set; } = null;

        /// <summary>
        /// Possible duplicates (VenueSummary or InstructorSummary, at most 5).
        /// </summary>
        public BoundedPreview<object> PossibleDuplicates { get; set; } = new BoundedPreview<object>();

        /// <summary>
        /// The submitter is the linked member of the instructor (fast-track badge).
        /// </summary>
        public bool IsLinkedMember { get; set; } = false;

        /// <summary>
        /// Pending past directory.reviewTargetHours.
        /// </summary>
        public bool IsOverdue { get; set; } = false;

        /// <summary>
        /// Review target time.
        /// </summary>
        public DateTime DueUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Reviewer (staff views), or null.
        /// </summary>
        public UserSummary? Reviewer { get; set; } = null;

        /// <summary>
        /// Reviewer note.
        /// </summary>
        public string? ReviewNote { get; set; } = null;

        /// <summary>
        /// Decision time.
        /// </summary>
        public DateTime? DecidedUtc { get; set; } = null;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
