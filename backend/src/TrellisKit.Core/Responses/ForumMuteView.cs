namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A forum mute (docs/PHASE3_CONTRACT.md section 11.3).
    /// </summary>
    public class ForumMuteView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (fmu_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Muted member.
        /// </summary>
        public UserSummary User
        {
            get { return _User; }
            set { _User = value ?? new UserSummary(); }
        }

        /// <summary>
        /// Forum.
        /// </summary>
        public ForumMuteForumRef Forum
        {
            get { return _Forum; }
            set { _Forum = value ?? new ForumMuteForumRef(); }
        }

        /// <summary>
        /// End of the mute, or null until lifted.
        /// </summary>
        public DateTime? UntilUtc { get; set; } = null;

        /// <summary>
        /// Reason.
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// Moderator, or null for the admin token.
        /// </summary>
        public UserSummary? MutedBy { get; set; } = null;

        /// <summary>
        /// When the mute was lifted, or null while active.
        /// </summary>
        public DateTime? EndedUtc { get; set; } = null;

        /// <summary>
        /// Creation time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(); }
        }

        #endregion

        #region Private-Members

        private UserSummary _User = new UserSummary();
        private ForumMuteForumRef _Forum = new ForumMuteForumRef();
        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion
    }
}
