namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One idea the member authored (account export section ideas).
    /// </summary>
    public class IdeaExportItem
    {
        #region Public-Members

        /// <summary>
        /// Identifier (ida_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Body.
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// Status key.
        /// </summary>
        public string Status { get; set; } = String.Empty;

        /// <summary>
        /// Audience.
        /// </summary>
        public ContentAudienceEnum Audience { get; set; } = ContentAudienceEnum.Members;

        /// <summary>
        /// Votes.
        /// </summary>
        public int VoteCount { get; set; } = 0;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last edit, or null.
        /// </summary>
        public DateTime? EditedUtc { get; set; } = null;

        #endregion
    }
}
