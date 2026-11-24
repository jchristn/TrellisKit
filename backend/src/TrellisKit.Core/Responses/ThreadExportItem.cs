namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// A forum thread in the account export (Phase 3 contract section 13), with its revisions.
    /// </summary>
    public class ThreadExportItem
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Forum identifier.
        /// </summary>
        public string ForumId { get; set; } = String.Empty;

        /// <summary>
        /// Thread type.
        /// </summary>
        public ThreadTypeEnum Type { get; set; } = ThreadTypeEnum.Discussion;

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Body.
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Feedback focus (feedback requests only).
        /// </summary>
        public string? FeedbackFocus { get; set; } = null;

        /// <summary>
        /// Moderation state.
        /// </summary>
        public ModerationStateEnum ModerationState { get; set; } = ModerationStateEnum.None;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last edited.
        /// </summary>
        public DateTime? EditedUtc { get; set; } = null;

        /// <summary>
        /// Deleted.
        /// </summary>
        public DateTime? DeletedUtc { get; set; } = null;

        /// <summary>
        /// Revisions, oldest first.
        /// </summary>
        public List<ThreadRevision> Revisions
        {
            get
            {
                return _Revisions;
            }
            set
            {
                _Revisions = value ?? new List<ThreadRevision>();
            }
        }

        #endregion

        #region Private-Members

        private List<ThreadRevision> _Revisions = new List<ThreadRevision>();

        #endregion
    }
}
