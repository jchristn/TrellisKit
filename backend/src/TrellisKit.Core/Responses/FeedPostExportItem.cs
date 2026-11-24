namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A feed post in the account export (contract section 11), with its revisions.
    /// </summary>
    public class FeedPostExportItem
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Body.
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// Visibility.
        /// </summary>
        public FeedVisibilityEnum Visibility { get; set; } = FeedVisibilityEnum.Members;

        /// <summary>
        /// State.
        /// </summary>
        public FeedPostStateEnum State { get; set; } = FeedPostStateEnum.Published;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Published.
        /// </summary>
        public DateTime? PublishedUtc { get; set; } = null;

        /// <summary>
        /// Edited.
        /// </summary>
        public DateTime? EditedUtc { get; set; } = null;

        /// <summary>
        /// Deleted.
        /// </summary>
        public DateTime? DeletedUtc { get; set; } = null;

        /// <summary>
        /// Revisions, oldest first (never null).
        /// </summary>
        public List<FeedPostRevisionView> Revisions
        {
            get { return _Revisions; }
            set { _Revisions = value ?? new List<FeedPostRevisionView>(); }
        }

        #endregion

        #region Private-Members

        private List<FeedPostRevisionView> _Revisions = new List<FeedPostRevisionView>();

        #endregion
    }
}
