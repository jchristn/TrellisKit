namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One item of a member's content history (contract section 10.2).
    /// </summary>
    public class ModerationContentItem : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// FeedPost or Comment.
        /// </summary>
        public TargetTypeEnum TargetType { get; set; } = TargetTypeEnum.FeedPost;

        /// <summary>
        /// Identifier.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// FeedPost state or Comment state.
        /// </summary>
        public string State { get; set; } = String.Empty;

        /// <summary>
        /// Post of a comment (the post itself for posts).
        /// </summary>
        public string? FeedPostId { get; set; } = null;

        /// <summary>
        /// Excerpt (null once erased or purged).
        /// </summary>
        public string? Excerpt { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Reactions.
        /// </summary>
        public int ReactionTotal { get; set; } = 0;

        /// <summary>
        /// Reports (any status).
        /// </summary>
        public long ReportCount { get; set; } = 0;

        /// <summary>
        /// Labels (never null).
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags (never null).
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(); }
        }

        #endregion

        #region Private-Members

        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion
    }
}
