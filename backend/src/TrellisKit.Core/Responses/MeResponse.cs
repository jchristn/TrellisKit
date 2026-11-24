namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The signed-in member (contract section 3).
    /// </summary>
    public class MeResponse : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Email.
        /// </summary>
        public string? Email { get; set; } = null;

        /// <summary>
        /// Email verified.
        /// </summary>
        public bool EmailVerified { get; set; } = false;

        /// <summary>
        /// Display name.
        /// </summary>
        public string DisplayName { get; set; } = String.Empty;

        /// <summary>
        /// Handle.
        /// </summary>
        public string? Handle { get; set; } = null;

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
        /// Deprecated (profileFields.legacyFieldsMode; use profile.&lt;key&gt;). Goals. Never null.
        /// </summary>
        public List<string> Goals
        {
            get { return _Goals; }
            set { _Goals = value ?? new List<string>(); }
        }

        /// <summary>
        /// Deprecated (profileFields.legacyFieldsMode; use profile.&lt;key&gt;). Other goal text.
        /// </summary>
        public string? GoalsOther { get; set; } = null;

        /// <summary>
        /// Deprecated (profileFields.legacyFieldsMode; use profile.&lt;key&gt;). Rank history. Never null.
        /// </summary>
        public List<RankHistoryView> RankHistory
        {
            get { return _RankHistory; }
            set { _RankHistory = value ?? new List<RankHistoryView>(); }
        }

        /// <summary>
        /// Whether the member attested they meet the property minimum age (membership.minimumAge).
        /// </summary>
        public bool AgeAttested { get; set; } = false;

        /// <summary>
        /// True when display name and handle are set, the age is attested, and every enabled field collected at sign-up
        /// and marked required has a value (docs/MULTIPROPERTY_CONTRACT.md section 9.10).
        /// </summary>
        public bool ProfileComplete { get; set; } = false;

        /// <summary>
        /// Configurable profile fields (docs/MULTIPROPERTY_CONTRACT.md section 9.8): in Me every enabled field visible to
        /// the member with every key present (null when unset); in the admin member detail every enabled field.
        /// </summary>
        public Dictionary<string, object?> Profile
        {
            get { return _Profile; }
            set { _Profile = value ?? new Dictionary<string, object?>(StringComparer.Ordinal); }
        }

        /// <summary>
        /// The keys that keep profileComplete false, built-ins first (displayName, handle), then profile order. Empty
        /// when complete or when only age attestation is missing.
        /// </summary>
        public List<string> ProfileMissing
        {
            get { return _ProfileMissing; }
            set { _ProfileMissing = value ?? new List<string>(); }
        }

        /// <summary>
        /// Sign-in methods. Never null.
        /// </summary>
        public List<SignInMethodEnum> SignInMethods
        {
            get { return _SignInMethods; }
            set { _SignInMethods = value ?? new List<SignInMethodEnum>(); }
        }

        /// <summary>
        /// Whether the member is a platform administrator: true when the member holds the global SystemAdmin role. The
        /// administrative dashboard (/admin) and every /api/v1/admin route that is not scoped to a moderator role require it.
        /// </summary>
        public bool IsAdmin
        {
            get { return Roles.Any(r => r.Role == RoleEnum.SystemAdmin); }
        }

        /// <summary>
        /// Explicit roles (User is implicit). Never null.
        /// </summary>
        public List<RoleView> Roles
        {
            get { return _Roles; }
            set { _Roles = value ?? new List<RoleView>(); }
        }

        /// <summary>
        /// Flags.
        /// </summary>
        public MemberFlags Flags { get; set; } = new MemberFlags();

        /// <summary>
        /// Membership.
        /// </summary>
        public MembershipSummary Membership { get; set; } = new MembershipSummary();

        /// <summary>
        /// The caller's access tier under the property's access model (docs/ACCESS_MODELS_CONTRACT.md section 5):
        /// SignedIn, Member (an active membership outside Open), or Staff (SystemAdmin). Default SignedIn.
        /// </summary>
        public TrellisKit.Core.Enums.AccessTierEnum AccessTier { get; set; } = TrellisKit.Core.Enums.AccessTierEnum.SignedIn;

        /// <summary>
        /// Whether the member must buy or redeem a membership before any content (FullyGated and AccessTier SignedIn):
        /// clients show the paywall step after sign-up and onboarding only when true. Default false.
        /// </summary>
        public bool MembershipRequired { get; set; } = false;

        /// <summary>
        /// Gating documents the member still has to accept (docs/TRELLISKIT.md section 14): never accepted, or a newer
        /// version requires re-acceptance. Bounded by property.json (at most 30). Never null; empty when nothing is pending.
        /// </summary>
        public List<DocumentSummaryView> PendingDocuments
        {
            get { return _PendingDocuments; }
            set { _PendingDocuments = value ?? new List<DocumentSummaryView>(); }
        }

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The member's opt-in public profile page: enabled, slug, path, and URL (docs/PHASE4_CONTRACT.md section 18.2).
        /// Never null; off by default.
        /// </summary>
        public ProfilePageView PublicPage { get; set; } = new ProfilePageView();

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

        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        private List<string> _Goals = new List<string>();
        private List<RankHistoryView> _RankHistory = new List<RankHistoryView>();
        private Dictionary<string, object?> _Profile = new Dictionary<string, object?>(StringComparer.Ordinal);
        private List<string> _ProfileMissing = new List<string>();
        private List<SignInMethodEnum> _SignInMethods = new List<SignInMethodEnum>();
        private List<RoleView> _Roles = new List<RoleView>();
        private List<DocumentSummaryView> _PendingDocuments = new List<DocumentSummaryView>();

        #endregion
    }
}
