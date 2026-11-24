namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.PropertyConfig;

    /// <summary>
    /// A property's stored access configuration (table propertyaccess with its gated features in
    /// propertyaccessfeatures; docs/ACCESS_MODELS_CONTRACT.md section 1): the access model, anonymous browsing, gated
    /// features, and default audience, plus bookkeeping. Seeded from property.json access, then edited by property
    /// administrators. Instances served to requests are treated as immutable snapshots; edits build a new instance.
    /// Thread safety: safe to read concurrently when not mutated.
    /// </summary>
    public class AccessPolicy
    {
        #region Public-Members

        /// <summary>
        /// The configuration used when nothing is stored yet and property.json has no access section: FullyGated.
        /// </summary>
        public static AccessPolicy FullyGatedDefault
        {
            get { return new AccessPolicy(); }
        }

        /// <summary>
        /// Every feature key in its wire form, in enum order.
        /// </summary>
        public static IReadOnlyList<string> FeatureKeys
        {
            get { return _FeatureKeys; }
        }

        /// <summary>
        /// Access model. Default FullyGated.
        /// </summary>
        public AccessModelEnum Model { get; set; } = AccessModelEnum.FullyGated;

        /// <summary>
        /// Whether signed-out callers may read Public objects on the content routes (never in FullyGated). Default false.
        /// </summary>
        public bool AnonymousBrowsing { get; set; } = false;

        /// <summary>
        /// Features that need a membership to write in (used only in PartiallyGated). Never null; default empty;
        /// distinct, in enum order once normalized.
        /// </summary>
        public List<FeatureKeyEnum> GatedFeatures
        {
            get { return _GatedFeatures; }
            set { _GatedFeatures = value ?? new List<FeatureKeyEnum>(); }
        }

        /// <summary>
        /// Configured audience of new objects, or null for the model default (Members, or SignedIn in Open).
        /// </summary>
        public ContentAudienceEnum? DefaultAudience { get; set; } = null;

        /// <summary>
        /// Hash of the property.json access section this row was last seeded from, or null when it was never seeded
        /// from a document (SingleProperty compares it to decide whether the folder changed).
        /// </summary>
        public string? SourceHash { get; set; } = null;

        /// <summary>
        /// Revision, bumped on every change. Default 1. Minimum 1.
        /// </summary>
        public int Revision
        {
            get { return _Revision; }
            set { _Revision = Math.Max(1, value); }
        }

        /// <summary>
        /// Member or platform administrator who made the last change, or null for seeding and the break-glass token.
        /// </summary>
        public string? UpdatedByUserId { get; set; } = null;

        /// <summary>
        /// When the row was created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the row last changed (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Whether purchases exist: every model except Open.
        /// </summary>
        public bool PurchasesEnabled
        {
            get { return Model != AccessModelEnum.Open; }
        }

        /// <summary>
        /// Whether signed-out callers may read Public objects on the content routes: AnonymousBrowsing outside FullyGated.
        /// </summary>
        public bool AllowsAnonymous
        {
            get { return AnonymousBrowsing && Model != AccessModelEnum.FullyGated; }
        }

        /// <summary>
        /// The audience of new objects outside gated features: the configured default, else Members (SignedIn in Open);
        /// FullyGated always Members and Open never Members.
        /// </summary>
        public ContentAudienceEnum EffectiveDefaultAudience
        {
            get
            {
                if (Model == AccessModelEnum.FullyGated) return ContentAudienceEnum.Members;
                ContentAudienceEnum configured = DefaultAudience ?? (Model == AccessModelEnum.Open ? ContentAudienceEnum.SignedIn : ContentAudienceEnum.Members);
                if (Model == AccessModelEnum.Open && configured == ContentAudienceEnum.Members) return ContentAudienceEnum.SignedIn;
                return configured;
            }
        }

        #endregion

        #region Private-Members

        private static readonly List<string> _FeatureKeys = Enum.GetValues<FeatureKeyEnum>().Select(FeatureKey).ToList();
        private List<FeatureKeyEnum> _GatedFeatures = new List<FeatureKeyEnum>();
        private int _Revision = 1;

        #endregion

        #region Public-Methods

        /// <summary>
        /// The wire form of a feature key (lower camel case, for example feed).
        /// </summary>
        /// <param name="feature">Feature.</param>
        /// <returns>Key.</returns>
        public static string FeatureKey(FeatureKeyEnum feature)
        {
            string name = feature.ToString();
            return Char.ToLowerInvariant(name[0]) + name.Substring(1);
        }

        /// <summary>
        /// Parse a feature key (case-insensitive).
        /// </summary>
        /// <param name="key">Key, for example feed.</param>
        /// <param name="feature">Parsed feature.</param>
        /// <returns>True when the key names a feature.</returns>
        public static bool TryParseFeature(string? key, out FeatureKeyEnum feature)
        {
            feature = FeatureKeyEnum.Feed;
            if (String.IsNullOrWhiteSpace(key)) return false;
            string trimmed = key.Trim();
            if (trimmed.Any(c => !Char.IsLetter(c))) return false;
            return Enum.TryParse(trimmed, true, out feature);
        }

        /// <summary>
        /// Validate an access configuration (property.json import and the admin route): known feature keys without
        /// duplicates, and no Members default audience in Open. Messages start with the field name.
        /// </summary>
        /// <param name="model">Access model.</param>
        /// <param name="gatedFeatures">Feature keys, or null.</param>
        /// <param name="defaultAudience">Default audience, or null.</param>
        /// <returns>Problems keyed by field (gatedFeatures, defaultAudience); empty when valid. Never null.</returns>
        public static Dictionary<string, string> Validate(AccessModelEnum model, IEnumerable<string>? gatedFeatures, ContentAudienceEnum? defaultAudience)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>(StringComparer.Ordinal);
            List<string> keys = (gatedFeatures ?? Enumerable.Empty<string>()).ToList();
            HashSet<FeatureKeyEnum> seen = new HashSet<FeatureKeyEnum>();
            for (int i = 0; i < keys.Count; i++)
            {
                if (!TryParseFeature(keys[i], out FeatureKeyEnum feature))
                {
                    errors.TryAdd("gatedFeatures", "gatedFeatures[" + i + "] '" + keys[i] + "' is not a feature key (" + String.Join(", ", _FeatureKeys) + ")");
                    continue;
                }
                if (!seen.Add(feature)) errors.TryAdd("gatedFeatures", "gatedFeatures lists " + FeatureKey(feature) + " more than once");
            }
            if (model == AccessModelEnum.Open && defaultAudience == ContentAudienceEnum.Members)
                errors["defaultAudience"] = "defaultAudience cannot be Members in the Open access model (there are no memberships)";
            return errors;
        }

        /// <summary>
        /// Build a policy from the property.json access section (absent means FullyGated). Unknown feature keys are
        /// dropped (the document validator reports them).
        /// </summary>
        /// <param name="access">Access section, or null.</param>
        /// <returns>Policy (SourceHash set to <see cref="DocumentHash"/>).</returns>
        public static AccessPolicy FromDocument(PropertyAccess? access)
        {
            AccessPolicy policy = new AccessPolicy { SourceHash = DocumentHash(access) };
            if (access == null) return policy;
            policy.Model = access.Model;
            policy.AnonymousBrowsing = access.AnonymousBrowsing;
            policy.DefaultAudience = access.DefaultAudience;
            policy.GatedFeatures = Normalize(access.GatedFeatures);
            return policy;
        }

        /// <summary>
        /// Parse feature keys into distinct features in enum order, dropping unknown keys.
        /// </summary>
        /// <param name="keys">Keys, or null.</param>
        /// <returns>Features. Never null.</returns>
        public static List<FeatureKeyEnum> Normalize(IEnumerable<string>? keys)
        {
            List<FeatureKeyEnum> features = new List<FeatureKeyEnum>();
            foreach (string key in keys ?? Enumerable.Empty<string>())
                if (TryParseFeature(key, out FeatureKeyEnum feature) && !features.Contains(feature)) features.Add(feature);
            features.Sort();
            return features;
        }

        /// <summary>
        /// A stable hash of a property.json access section (absent and default sections hash differently from
        /// configured ones only when their values differ).
        /// </summary>
        /// <param name="access">Access section, or null.</param>
        /// <returns>Lowercase hex SHA-256.</returns>
        public static string DocumentHash(PropertyAccess? access)
        {
            PropertyAccess a = access ?? new PropertyAccess();
            string canonical = a.Model + "|" + a.AnonymousBrowsing + "|" + String.Join(",", Normalize(a.GatedFeatures)) + "|" + (a.DefaultAudience?.ToString() ?? String.Empty);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        }

        /// <summary>
        /// Whether a feature needs a membership to write in: listed in GatedFeatures in PartiallyGated.
        /// </summary>
        /// <param name="feature">Feature.</param>
        /// <returns>True when gated.</returns>
        public bool IsGated(FeatureKeyEnum feature)
        {
            return Model == AccessModelEnum.PartiallyGated && _GatedFeatures.Contains(feature);
        }

        /// <summary>
        /// The audience an object is enforced at per object: in Open a stored Members audience is SignedIn; otherwise
        /// unchanged. (In FullyGated every content route already needs a membership at the route gate, so SignedIn
        /// content is members-only there without changing the per-object rule.)
        /// </summary>
        /// <param name="audience">Stored audience.</param>
        /// <returns>Effective audience.</returns>
        public ContentAudienceEnum Effective(ContentAudienceEnum audience)
        {
            if (Model == AccessModelEnum.Open && audience == ContentAudienceEnum.Members) return ContentAudienceEnum.SignedIn;
            return audience;
        }

        /// <summary>
        /// The audience of a new object in a feature: Members when the feature is gated (PartiallyGated), otherwise
        /// <see cref="EffectiveDefaultAudience"/>.
        /// </summary>
        /// <param name="feature">Feature of the object.</param>
        /// <returns>Audience.</returns>
        public ContentAudienceEnum DefaultAudienceFor(FeatureKeyEnum feature)
        {
            return IsGated(feature) ? ContentAudienceEnum.Members : EffectiveDefaultAudience;
        }

        /// <summary>
        /// A copy of this policy.
        /// </summary>
        /// <returns>Copy.</returns>
        public AccessPolicy Clone()
        {
            return new AccessPolicy
            {
                Model = Model,
                AnonymousBrowsing = AnonymousBrowsing,
                GatedFeatures = new List<FeatureKeyEnum>(_GatedFeatures),
                DefaultAudience = DefaultAudience,
                SourceHash = SourceHash,
                Revision = Revision,
                UpdatedByUserId = UpdatedByUserId,
                CreatedUtc = CreatedUtc,
                UpdatedUtc = UpdatedUtc
            };
        }

        #endregion
    }
}
