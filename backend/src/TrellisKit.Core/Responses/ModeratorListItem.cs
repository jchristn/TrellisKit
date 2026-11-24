namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One moderator (contract section 10.4).
    /// </summary>
    public class ModeratorListItem : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Member.
        /// </summary>
        public UserSummary User { get; set; } = new UserSummary();

        /// <summary>
        /// Moderator and FeedModerator assignments (bounded by the member's role assignments).
        /// </summary>
        public List<RoleView> Roles { get; set; } = new List<RoleView>();

        /// <summary>
        /// Earliest moderator grant (UTC).
        /// </summary>
        public DateTime? GrantedUtc { get; set; } = null;

        /// <summary>
        /// Who granted it ("admin token" when unknown).
        /// </summary>
        public string? GrantedByName { get; set; } = null;

        /// <summary>
        /// Open reports in the moderator's scope.
        /// </summary>
        public long OpenInScope { get; set; } = 0;

        /// <summary>
        /// Moderation actions in 30 days.
        /// </summary>
        public long Actions30d { get; set; } = 0;

        /// <summary>
        /// Median minutes from report to resolution in 30 days, or null.
        /// </summary>
        public double? MedianResponseMinutes30d { get; set; } = null;

        /// <summary>
        /// Reports resolved after their review target in 30 days.
        /// </summary>
        public long OverdueHandled30d { get; set; } = 0;

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
