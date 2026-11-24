namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// ModerationActionView (contract section 10.2).
    /// </summary>
    public class ModerationActionView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Moderator, or null for the system (AutoHide) and the admin token.
        /// </summary>
        public UserSummary? Moderator { get; set; } = null;

        /// <summary>
        /// Who took the action: the moderator's display name, "break-glass token" for the break-glass admin token, or
        /// "system" for system actions such as AutoHide.
        /// </summary>
        public string ModeratorName { get; set; } = "system";

        /// <summary>
        /// Target type.
        /// </summary>
        public TargetTypeEnum TargetType { get; set; } = TargetTypeEnum.FeedPost;

        /// <summary>
        /// Target identifier.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Author of the target.
        /// </summary>
        public UserSummary TargetAuthor { get; set; } = new UserSummary();

        /// <summary>
        /// Action.
        /// </summary>
        public ModerationActionTypeEnum Action { get; set; } = ModerationActionTypeEnum.Hide;

        /// <summary>
        /// Reason.
        /// </summary>
        public string Reason { get; set; } = String.Empty;

        /// <summary>
        /// Moderator note.
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// Whether the author was notified.
        /// </summary>
        public bool NotifiedAuthor { get; set; } = false;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

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
