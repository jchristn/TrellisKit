namespace TrellisKit.Server.Services
{
    using System;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// The engine's access decision (docs/ACCESS_MODELS_CONTRACT.md section 3): the tier a caller has, and the tier an
    /// access model requires to read an object of an audience, to write in a feature, to take part on an object, and
    /// to reach a route of the Member group. Route groups (<see cref="AuthorizationService"/>) apply the route tier;
    /// <see cref="ContentAudienceRule"/> and the content services apply the object tiers.
    /// Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public static class AccessDecider
    {
        #region Public-Methods

        /// <summary>
        /// The tier of a caller in the given state. A signed-in account that has not attested the minimum age counts
        /// as Anonymous for content (the route gate answers 403 AgeAttestationRequired before content is read).
        /// </summary>
        /// <param name="isStaff">Whether the caller is a SystemAdmin, the break-glass token, or a delegated session.</param>
        /// <param name="signedIn">Whether the caller is signed in.</param>
        /// <param name="ageAttested">Whether the caller attested the minimum age.</param>
        /// <param name="hasMembership">Whether the caller has an active membership.</param>
        /// <returns>Tier.</returns>
        public static AccessTierEnum TierOf(bool isStaff, bool signedIn, bool ageAttested, bool hasMembership)
        {
            if (isStaff) return AccessTierEnum.Staff;
            if (!signedIn || !ageAttested) return AccessTierEnum.Anonymous;
            return hasMembership ? AccessTierEnum.Member : AccessTierEnum.SignedIn;
        }

        /// <summary>
        /// The tier of a content caller.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <returns>Tier.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        public static AccessTierEnum TierOf(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            return TierOf(actor.IsSystemAdmin, !actor.IsAnonymous && (!String.IsNullOrEmpty(actor.UserId) || actor.IsBreakGlass), actor.AgeAttested, actor.HasMembership);
        }

        /// <summary>
        /// The tier that reading an object of an audience requires (after <see cref="AccessPolicy.Effective"/>): Public
        /// Anonymous, SignedIn SignedIn, Members Member. In FullyGated the Member route gate applies first, so reaching
        /// an object at all needs a membership there.
        /// </summary>
        /// <param name="policy">Access policy.</param>
        /// <param name="audience">Stored audience.</param>
        /// <returns>Required tier.</returns>
        /// <exception cref="ArgumentNullException">The policy is null.</exception>
        public static AccessTierEnum ReadTier(AccessPolicy policy, ContentAudienceEnum audience)
        {
            ArgumentNullException.ThrowIfNull(policy);
            switch (policy.Effective(audience))
            {
                case ContentAudienceEnum.Public:
                    return AccessTierEnum.Anonymous;
                case ContentAudienceEnum.SignedIn:
                    return AccessTierEnum.SignedIn;
                default:
                    return AccessTierEnum.Member;
            }
        }

        /// <summary>
        /// The tier that writing in a feature requires: Member in FullyGated; Member for a gated feature in
        /// PartiallyGated, else SignedIn; SignedIn in Open.
        /// </summary>
        /// <param name="policy">Access policy.</param>
        /// <param name="feature">Feature, or null for routes without a feature (media uploads).</param>
        /// <returns>Required tier.</returns>
        /// <exception cref="ArgumentNullException">The policy is null.</exception>
        public static AccessTierEnum WriteTier(AccessPolicy policy, FeatureKeyEnum? feature)
        {
            ArgumentNullException.ThrowIfNull(policy);
            if (policy.Model == AccessModelEnum.FullyGated) return AccessTierEnum.Member;
            if (feature != null && policy.IsGated(feature.Value)) return AccessTierEnum.Member;
            return AccessTierEnum.SignedIn;
        }

        /// <summary>
        /// The tier that taking part on an object (comment, reply, react) requires: the higher of the object's read
        /// tier (at least SignedIn) and the feature's write tier.
        /// </summary>
        /// <param name="policy">Access policy.</param>
        /// <param name="feature">Feature of the object.</param>
        /// <param name="audience">Stored audience of the object.</param>
        /// <returns>Required tier.</returns>
        /// <exception cref="ArgumentNullException">The policy is null.</exception>
        public static AccessTierEnum ParticipateTier(AccessPolicy policy, FeatureKeyEnum? feature, ContentAudienceEnum audience)
        {
            ArgumentNullException.ThrowIfNull(policy);
            AccessTierEnum read = ReadTier(policy, audience);
            if (read < AccessTierEnum.SignedIn) read = AccessTierEnum.SignedIn;
            AccessTierEnum write = WriteTier(policy, feature);
            return write > read ? write : read;
        }

        /// <summary>
        /// The tier a route of the Member group requires at the route gate. FullyGated: Member. Otherwise: account
        /// routes SignedIn; writes and participation the feature's write tier; reads and lists of the members feature
        /// its write tier (profiles are not per-object gated) and of the directory feature Member when directory or
        /// members is gated; other reads and lists Anonymous when the route is
        /// anonymous-eligible and anonymous browsing is on, else SignedIn (each object's audience is checked again).
        /// Undeclared routes: Member in PartiallyGated, SignedIn in Open.
        /// </summary>
        /// <param name="policy">Access policy.</param>
        /// <param name="route">Declaration, or null when the route is not declared.</param>
        /// <returns>Required tier.</returns>
        /// <exception cref="ArgumentNullException">The policy is null.</exception>
        public static AccessTierEnum RouteTier(AccessPolicy policy, RouteAccess? route)
        {
            ArgumentNullException.ThrowIfNull(policy);
            if (policy.Model == AccessModelEnum.FullyGated) return AccessTierEnum.Member;
            if (route == null) return policy.Model == AccessModelEnum.Open ? AccessTierEnum.SignedIn : AccessTierEnum.Member;
            if (route.Account) return AccessTierEnum.SignedIn;
            // Phase 4 venue, instructor, event, and library routes: the objects decide (locked stubs, samples).
            if (route.ObjectGated)
            {
                if (route.AnonymousEligible && policy.AllowsAnonymous) return AccessTierEnum.Anonymous;
                return AccessTierEnum.SignedIn;
            }
            if (route.Action == AccessActionEnum.Write || route.Action == AccessActionEnum.Participate) return WriteTier(policy, route.Feature);
            if (route.Feature == FeatureKeyEnum.Members) return WriteTier(policy, FeatureKeyEnum.Members);
            // The member directory lists members, so it needs a membership when either directory or members is gated.
            if (route.Feature == FeatureKeyEnum.Directory)
                return policy.IsGated(FeatureKeyEnum.Directory) || policy.IsGated(FeatureKeyEnum.Members) ? AccessTierEnum.Member : AccessTierEnum.SignedIn;
            // Forums gated in PartiallyGated: reading needs a membership too (docs/PHASE3_CONTRACT.md section 0.4).
            if (route.Feature == FeatureKeyEnum.Forums && policy.IsGated(FeatureKeyEnum.Forums)) return AccessTierEnum.Member;
            // Search gated in PartiallyGated: searching needs a membership (it has no writes to gate).
            if (route.Feature == FeatureKeyEnum.Search && policy.IsGated(FeatureKeyEnum.Search)) return AccessTierEnum.Member;
            // Messaging gated: lapsed members cannot read their history either (docs/PHASE5_CONTRACT.md resolved decision 26).
            if (route.Feature == FeatureKeyEnum.Messaging && policy.IsGated(FeatureKeyEnum.Messaging)) return AccessTierEnum.Member;
            // Ideas gated: reading the board needs a membership too (section 0.3).
            if (route.Feature == FeatureKeyEnum.Ideas && policy.IsGated(FeatureKeyEnum.Ideas)) return AccessTierEnum.Member;
            if (route.AnonymousEligible && policy.AllowsAnonymous) return AccessTierEnum.Anonymous;
            return AccessTierEnum.SignedIn;
        }

        /// <summary>
        /// Whether a caller tier meets a required tier.
        /// </summary>
        /// <param name="tier">Caller tier.</param>
        /// <param name="required">Required tier.</param>
        /// <returns>True when met.</returns>
        public static bool Meets(AccessTierEnum tier, AccessTierEnum required)
        {
            return tier >= required;
        }

        /// <summary>
        /// Whether a caller who cannot read an object should see it as a locked stub (PartiallyGated, a signed-in
        /// caller below the Member tier, an object that needs Member).
        /// </summary>
        /// <param name="policy">Access policy.</param>
        /// <param name="tier">Caller tier.</param>
        /// <param name="audience">Stored audience.</param>
        /// <returns>True when the object is shown locked.</returns>
        /// <exception cref="ArgumentNullException">The policy is null.</exception>
        public static bool ShowsLocked(AccessPolicy policy, AccessTierEnum tier, ContentAudienceEnum audience)
        {
            ArgumentNullException.ThrowIfNull(policy);
            return policy.Model == AccessModelEnum.PartiallyGated && tier == AccessTierEnum.SignedIn && ReadTier(policy, audience) == AccessTierEnum.Member;
        }

        /// <summary>
        /// Whether a member (not the request's caller) may open an object of an audience: notification recipients,
        /// push, and new-post emails. Staff always; otherwise age attested and, in FullyGated, a membership; in the
        /// other models the object's read tier (at least SignedIn).
        /// </summary>
        /// <param name="policy">Access policy.</param>
        /// <param name="audience">Stored audience.</param>
        /// <param name="isStaff">Whether the recipient is a SystemAdmin.</param>
        /// <param name="ageAttested">Whether the recipient attested the minimum age.</param>
        /// <param name="hasMembership">Whether the recipient has an active membership.</param>
        /// <returns>True when the recipient may open it.</returns>
        /// <exception cref="ArgumentNullException">The policy is null.</exception>
        public static bool RecipientMayOpen(AccessPolicy policy, ContentAudienceEnum audience, bool isStaff, bool ageAttested, bool hasMembership)
        {
            ArgumentNullException.ThrowIfNull(policy);
            if (isStaff) return true;
            if (!ageAttested) return false;
            if (policy.Model == AccessModelEnum.FullyGated) return hasMembership;
            return Meets(TierOf(false, true, true, hasMembership), ReadTier(policy, audience));
        }

        /// <summary>
        /// Whether fan-out recipients of an object of an audience must hold a membership (the SQL pre-filter of the
        /// notification fan-out): always in FullyGated, for objects that need Member in PartiallyGated, never in Open.
        /// </summary>
        /// <param name="policy">Access policy.</param>
        /// <param name="audience">Stored audience.</param>
        /// <returns>True when a membership is required.</returns>
        /// <exception cref="ArgumentNullException">The policy is null.</exception>
        public static bool RecipientsNeedMembership(AccessPolicy policy, ContentAudienceEnum audience)
        {
            ArgumentNullException.ThrowIfNull(policy);
            if (policy.Model == AccessModelEnum.FullyGated) return true;
            return ReadTier(policy, audience) == AccessTierEnum.Member;
        }

        #endregion
    }
}
