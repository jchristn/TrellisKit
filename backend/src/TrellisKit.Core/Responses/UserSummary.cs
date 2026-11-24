namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A person embedded in content payloads (authors, actors, reactors). Phase 2 contract section 2.
    /// </summary>
    public class UserSummary : IMetadataPayload
    {
        #region Public-Members

        /// <summary>Metadata labels when this summary is the listed object; empty on embedded previews.</summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>Metadata tags when this summary is the listed object; empty on embedded previews.</summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(StringComparer.Ordinal); }
        }

        /// <summary>
        /// Member identifier, or null for a deleted account.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Display name ("Deleted member" for a deleted account).
        /// </summary>
        public string DisplayName { get; set; } = String.Empty;

        /// <summary>
        /// Handle, or null.
        /// </summary>
        public string? Handle { get; set; } = null;

        /// <summary>
        /// Avatar URL path, or null.
        /// </summary>
        public string? AvatarUrl { get; set; } = null;

        /// <summary>
        /// Deprecated (use profile.rank.key): rank key of the seeded rank field, or null (unset, disabled, not visible, or
        /// profileFields.legacyFieldsMode Off).
        /// </summary>
        public string? Rank { get; set; } = null;

        /// <summary>
        /// Deprecated (use profile.rank.level): rank level, 0 when unset.
        /// </summary>
        public int RankLevel { get; set; } = 0;

        /// <summary>
        /// Whether the member holds SystemAdmin, Moderator, or FeedModerator.
        /// </summary>
        public bool IsStaff { get; set; } = false;

        /// <summary>
        /// Whether the account was deleted.
        /// </summary>
        public bool IsDeleted { get; set; } = false;

        /// <summary>
        /// Card profile fields (docs/MULTIPROPERTY_CONTRACT.md section 9.8): fields visible to the viewer with showOnCard
        /// and a value, at most profileFields.maxCardFields, keyed by field key. Empty for deleted accounts.
        /// </summary>
        public Dictionary<string, object?> Profile
        {
            get { return _Profile; }
            set { _Profile = value ?? new Dictionary<string, object?>(StringComparer.Ordinal); }
        }

        #endregion

        #region Private-Members

        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>(StringComparer.Ordinal);
        private Dictionary<string, object?> _Profile = new Dictionary<string, object?>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// The summary shown for a deleted (or unknown) account.
        /// </summary>
        /// <returns>Summary.</returns>
        public static UserSummary Deleted()
        {
            return new UserSummary { DisplayName = "Deleted member", IsDeleted = true };
        }

        #endregion
    }
}
