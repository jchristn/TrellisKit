namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A member in GET /api/v1/admin/members.
    /// </summary>
    public class AdminMemberListItem : IMetadataPayload
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
        /// Email.
        /// </summary>
        public string? Email { get; set; } = null;

        /// <summary>
        /// Avatar URL path.
        /// </summary>
        public string? AvatarUrl { get; set; } = null;

        /// <summary>
        /// Deprecated (profileFields.legacyFieldsMode; use profile.&lt;key&gt;). Rank key from the property profile.rank.values, or null.
        /// </summary>
        public string? Rank { get; set; } = null;

        /// <summary>
        /// Deprecated (profileFields.legacyFieldsMode; use profile.&lt;key&gt;). Level within the rank (0 to the property profile.rank.maxLevel).
        /// </summary>
        public int RankLevel { get; set; } = 0;

        /// <summary>
        /// Membership.
        /// </summary>
        public MembershipSummary Membership { get; set; } = new MembershipSummary();

        /// <summary>
        /// Whether the member is a platform administrator: true when the member holds the global SystemAdmin role. The
        /// administrative dashboard (/admin) and every /api/v1/admin route that is not scoped to a moderator role require it.
        /// </summary>
        public bool IsAdmin
        {
            get { return Roles.Any(r => r.Role == RoleEnum.SystemAdmin); }
        }

        /// <summary>
        /// Roles. Never null.
        /// </summary>
        public List<RoleView> Roles
        {
            get { return _Roles; }
            set { _Roles = value ?? new List<RoleView>(); }
        }

        /// <summary>
        /// Banned.
        /// </summary>
        public bool IsBanned { get; set; } = false;

        /// <summary>
        /// Muted now.
        /// </summary>
        public bool IsMuted { get; set; } = false;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last seen.
        /// </summary>
        public DateTime? LastSeenUtc { get; set; } = null;

        /// <summary>
        /// Card profile fields as staff see them (docs/MULTIPROPERTY_CONTRACT.md section 9.8).
        /// </summary>
        public Dictionary<string, object?> Profile
        {
            get { return _Profile; }
            set { _Profile = value ?? new Dictionary<string, object?>(StringComparer.Ordinal); }
        }

        /// <summary>
        /// Labels visible to the caller (staff: entries only for staff). Never null.
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags visible to the caller (staff: keys only for staff). Never null.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(); }
        }

        #endregion

        #region Private-Members

        private Dictionary<string, object?> _Profile = new Dictionary<string, object?>(StringComparer.Ordinal);

        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        private List<RoleView> _Roles = new List<RoleView>();

        #endregion
    }
}
