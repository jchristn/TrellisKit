namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// A property's access configuration as clients see it: GET /api/v1/property access, and (with the bookkeeping
    /// fields) GET and PUT /api/v1/admin/property/access (docs/ACCESS_MODELS_CONTRACT.md sections 5 and 6).
    /// </summary>
    public class AccessPolicyView
    {
        #region Public-Members

        /// <summary>
        /// Access model.
        /// </summary>
        public AccessModelEnum Model { get; set; } = AccessModelEnum.FullyGated;

        /// <summary>
        /// Whether signed-out visitors may read Public content on the content routes (always false in FullyGated).
        /// </summary>
        public bool AnonymousBrowsing { get; set; } = false;

        /// <summary>
        /// Gated feature keys (empty unless PartiallyGated). On the public view this is effective: gating members also
        /// lists directory. Fixed-bound: at most the 8 feature keys.
        /// </summary>
        public List<string> GatedFeatures
        {
            get { return _GatedFeatures; }
            set { _GatedFeatures = value ?? new List<string>(); }
        }

        /// <summary>
        /// Audience new objects get outside gated features (Members in FullyGated, never Members in Open).
        /// </summary>
        public ContentAudienceEnum DefaultAudience { get; set; } = ContentAudienceEnum.Members;

        /// <summary>
        /// Whether purchases exist (model is not Open).
        /// </summary>
        public bool PurchasesEnabled { get; set; } = true;

        /// <summary>
        /// Whether sign-up codes exist (purchases enabled and features.signupCodes).
        /// </summary>
        public bool SignupCodesEnabled { get; set; } = true;

        /// <summary>
        /// Revision of the configuration, bumped on every change.
        /// </summary>
        public int Revision { get; set; } = 1;

        /// <summary>
        /// Admin view only: the configured default audience (null means the model default; always null in FullyGated,
        /// where the default is Members). Null on the public view.
        /// </summary>
        public ContentAudienceEnum? ConfiguredDefaultAudience { get; set; } = null;

        /// <summary>
        /// Admin view only: who changed it last (member or platform administrator id), or null.
        /// </summary>
        public string? UpdatedByUserId { get; set; } = null;

        /// <summary>
        /// Admin view only: when it last changed (UTC), or null on the public view.
        /// </summary>
        public DateTime? UpdatedUtc { get; set; } = null;

        #endregion

        #region Private-Members

        private List<string> _GatedFeatures = new List<string>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the public view.
        /// </summary>
        /// <param name="policy">Policy.</param>
        /// <param name="signupCodesFeature">property.json features.signupCodes.</param>
        /// <returns>View.</returns>
        /// <exception cref="ArgumentNullException">The policy is null.</exception>
        public static AccessPolicyView Public(AccessPolicy policy, bool signupCodesFeature)
        {
            ArgumentNullException.ThrowIfNull(policy);
            return new AccessPolicyView
            {
                Model = policy.Model,
                AnonymousBrowsing = policy.AllowsAnonymous,
                GatedFeatures = EffectiveGated(policy),
                DefaultAudience = policy.EffectiveDefaultAudience,
                PurchasesEnabled = policy.PurchasesEnabled,
                SignupCodesEnabled = policy.PurchasesEnabled && signupCodesFeature,
                Revision = policy.Revision
            };
        }

        /// <summary>
        /// Build the admin view: the public view plus the stored values and bookkeeping (gatedFeatures as stored, even
        /// outside PartiallyGated, so switching back restores them).
        /// </summary>
        /// <param name="policy">Policy.</param>
        /// <param name="signupCodesFeature">property.json features.signupCodes.</param>
        /// <returns>View.</returns>
        /// <exception cref="ArgumentNullException">The policy is null.</exception>
        public static AccessPolicyView Admin(AccessPolicy policy, bool signupCodesFeature)
        {
            AccessPolicyView view = Public(policy, signupCodesFeature);
            view.AnonymousBrowsing = policy.AnonymousBrowsing;
            view.GatedFeatures = policy.GatedFeatures.Select(AccessPolicy.FeatureKey).ToList();
            view.ConfiguredDefaultAudience = policy.Model == AccessModelEnum.FullyGated ? null : policy.DefaultAudience;
            view.UpdatedByUserId = policy.UpdatedByUserId;
            view.UpdatedUtc = policy.UpdatedUtc;
            return view;
        }

        #endregion

        #region Private-Methods

        private static List<string> EffectiveGated(AccessPolicy policy)
        {
            if (policy.Model != AccessModelEnum.PartiallyGated) return new List<string>();
            List<FeatureKeyEnum> gated = new List<FeatureKeyEnum>(policy.GatedFeatures);
            // The member directory lists members, so gating members gates the directory too.
            if (gated.Contains(FeatureKeyEnum.Members) && !gated.Contains(FeatureKeyEnum.Directory)) gated.Add(FeatureKeyEnum.Directory);
            gated.Sort();
            return gated.Select(AccessPolicy.FeatureKey).ToList();
        }

        #endregion
    }
}
