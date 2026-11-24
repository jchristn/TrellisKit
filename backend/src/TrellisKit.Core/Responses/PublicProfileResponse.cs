namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A member's public profile.
    /// </summary>
    public class PublicProfileResponse
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Display name.
        /// </summary>
        public string DisplayName { get; set; } = String.Empty;

        /// <summary>
        /// Handle.
        /// </summary>
        public string? Handle { get; set; } = null;

        /// <summary>
        /// Absolute URL of the member's public profile page when the member turned it on, otherwise null
        /// (docs/PHASE4_CONTRACT.md section 18.2).
        /// </summary>
        public string? PublicUrl { get; set; } = null;

        /// <summary>
        /// Avatar URL path.
        /// </summary>
        public string? AvatarUrl { get; set; } = null;

        /// <summary>
        /// Bio.
        /// </summary>
        public string? Bio { get; set; } = null;

        /// <summary>
        /// Deprecated (profileFields.legacyFieldsMode; use profile.&lt;key&gt;). Rank key from the property profile.rank.values, or null.
        /// </summary>
        public string? Rank { get; set; } = null;

        /// <summary>
        /// Deprecated (profileFields.legacyFieldsMode; use profile.&lt;key&gt;). Level within the rank (0 to the property profile.rank.maxLevel).
        /// </summary>
        public int RankLevel { get; set; } = 0;

        /// <summary>
        /// Deprecated (profileFields.legacyFieldsMode; use profile.&lt;key&gt;). Affiliation.
        /// </summary>
        public string? Affiliation { get; set; } = null;

        /// <summary>
        /// Deprecated (profileFields.legacyFieldsMode; use profile.&lt;key&gt;). City.
        /// </summary>
        public string? City { get; set; } = null;

        /// <summary>
        /// Deprecated (profileFields.legacyFieldsMode; use profile.&lt;key&gt;). Region.
        /// </summary>
        public string? Region { get; set; } = null;

        /// <summary>
        /// Deprecated (profileFields.legacyFieldsMode; use profile.&lt;key&gt;). Country.
        /// </summary>
        public string? Country { get; set; } = null;

        /// <summary>
        /// Deprecated (profileFields.legacyFieldsMode; use profile.&lt;key&gt;). Rank history. Never null.
        /// </summary>
        public List<RankHistoryView> RankHistory
        {
            get { return _RankHistory; }
            set { _RankHistory = value ?? new List<RankHistoryView>(); }
        }

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Configurable profile fields visible to the viewer with showOnProfile and a value, keyed by field key (unset
        /// fields are omitted).
        /// </summary>
        public Dictionary<string, object?> Profile
        {
            get { return _Profile; }
            set { _Profile = value ?? new Dictionary<string, object?>(StringComparer.Ordinal); }
        }

        /// <summary>
        /// Whether the member holds a staff role (SystemAdmin, Moderator, FeedModerator).
        /// </summary>
        public bool IsStaff { get; set; } = false;

        /// <summary>
        /// Whether the caller has blocked this member (Phase 2 contract section 9.2).
        /// </summary>
        public bool IsBlockedByMe { get; set; } = false;

        /// <summary>
        /// Labels of the member (staff entries only for staff viewers) (never null).
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags of the member (staff entries only for staff viewers) (never null).
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
        private List<RankHistoryView> _RankHistory = new List<RankHistoryView>();
        private Dictionary<string, object?> _Profile = new Dictionary<string, object?>(StringComparer.Ordinal);

        #endregion
    }
}
