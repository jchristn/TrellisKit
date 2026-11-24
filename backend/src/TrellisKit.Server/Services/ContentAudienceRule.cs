namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// The one place that decides whether a caller meets a content object's audience (docs/TRELLISKIT.md section 7,
    /// docs/ACCESS_MODELS_CONTRACT.md section 3), under the property's access model. Public: anyone who reached the
    /// route (signed out only where anonymous browsing or the public routes allow it). SignedIn: a signed-in account
    /// that attested the property minimum age. Members: an active membership. The model adjusts the stored audience
    /// first (<see cref="AccessPolicy.Effective"/>): in Open, Members is enforced as SignedIn; in FullyGated, SignedIn is
    /// enforced as Members (and the Member route gate already requires a membership). SystemAdmin (and the break-glass
    /// token) meets every audience. Authors always see their own content and feed moderators see content they
    /// moderate; those bypasses live in <see cref="ContentAccessService"/>. Thread safety: stateless; safe to call
    /// concurrently.
    /// </summary>
    public static class ContentAudienceRule
    {
        #region Public-Methods

        /// <summary>
        /// Whether a caller meets an audience under the caller's access policy (<see cref="ContentActor.Access"/>).
        /// </summary>
        /// <param name="audience">Content audience.</param>
        /// <param name="actor">Caller.</param>
        /// <returns>True when the caller meets it.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        public static bool Meets(ContentAudienceEnum audience, ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            return AccessDecider.Meets(AccessDecider.TierOf(actor), AccessDecider.ReadTier(actor.Access, audience));
        }

        /// <summary>
        /// Whether a caller in the given state meets an audience under the FullyGated model (for recipients that are
        /// not the request's caller). Prefer the overload that takes the property's policy.
        /// </summary>
        /// <param name="audience">Content audience.</param>
        /// <param name="isSystemAdmin">Whether the caller holds SystemAdmin.</param>
        /// <param name="signedIn">Whether the caller is a signed-in account.</param>
        /// <param name="ageAttested">Whether the caller attested the property minimum age.</param>
        /// <param name="hasMembership">Whether the caller has an active membership.</param>
        /// <returns>True when the caller meets it.</returns>
        public static bool Meets(ContentAudienceEnum audience, bool isSystemAdmin, bool signedIn, bool ageAttested, bool hasMembership)
        {
            return Meets(AccessPolicy.FullyGatedDefault, audience, isSystemAdmin, signedIn, ageAttested, hasMembership);
        }

        /// <summary>
        /// Whether a caller in the given state meets an audience under an access policy.
        /// </summary>
        /// <param name="policy">Access policy.</param>
        /// <param name="audience">Content audience.</param>
        /// <param name="isSystemAdmin">Whether the caller holds SystemAdmin.</param>
        /// <param name="signedIn">Whether the caller is a signed-in account.</param>
        /// <param name="ageAttested">Whether the caller attested the property minimum age.</param>
        /// <param name="hasMembership">Whether the caller has an active membership.</param>
        /// <returns>True when the caller meets it.</returns>
        /// <exception cref="ArgumentNullException">The policy is null.</exception>
        public static bool Meets(AccessPolicy policy, ContentAudienceEnum audience, bool isSystemAdmin, bool signedIn, bool ageAttested, bool hasMembership)
        {
            ArgumentNullException.ThrowIfNull(policy);
            return AccessDecider.Meets(AccessDecider.TierOf(isSystemAdmin, signedIn, ageAttested, hasMembership), AccessDecider.ReadTier(policy, audience));
        }

        /// <summary>
        /// Every audience a caller meets, in enum order, for enumeration filters.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <returns>Audiences (never empty: Public is always met).</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        public static List<ContentAudienceEnum> VisibleAudiences(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            List<ContentAudienceEnum> result = new List<ContentAudienceEnum>();
            foreach (ContentAudienceEnum audience in Enum.GetValues<ContentAudienceEnum>())
            {
                if (Meets(audience, actor)) result.Add(audience);
            }
            return result;
        }

        /// <summary>
        /// Every audience a caller receives in a list: the audiences the caller meets plus, in PartiallyGated for a
        /// signed-in caller below Member, the audiences shown as locked stubs.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <returns>Audiences (never empty).</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        public static List<ContentAudienceEnum> ListedAudiences(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            List<ContentAudienceEnum> result = VisibleAudiences(actor);
            AccessTierEnum tier = AccessDecider.TierOf(actor);
            foreach (ContentAudienceEnum audience in Enum.GetValues<ContentAudienceEnum>())
            {
                if (!result.Contains(audience) && AccessDecider.ShowsLocked(actor.Access, tier, audience)) result.Add(audience);
            }
            result.Sort();
            return result;
        }

        /// <summary>
        /// Whether a recipient of a notification about content may open that content under the FullyGated model.
        /// Prefer the overload that takes the property's policy.
        /// </summary>
        /// <param name="audience">Content audience.</param>
        /// <param name="isSystemAdmin">Whether the recipient holds SystemAdmin.</param>
        /// <param name="ageAttested">Whether the recipient attested the property minimum age.</param>
        /// <param name="hasMembership">Whether the recipient has an active membership.</param>
        /// <returns>True when the recipient may open it.</returns>
        public static bool RecipientMayOpen(ContentAudienceEnum audience, bool isSystemAdmin, bool ageAttested, bool hasMembership)
        {
            return AccessDecider.RecipientMayOpen(AccessPolicy.FullyGatedDefault, audience, isSystemAdmin, ageAttested, hasMembership);
        }

        /// <summary>
        /// Whether a recipient of a notification about content may open that content under an access policy: used by
        /// the notification fan-out, push, new-post emails, and every notification about a feed post or its comments.
        /// </summary>
        /// <param name="policy">Access policy.</param>
        /// <param name="audience">Content audience.</param>
        /// <param name="isSystemAdmin">Whether the recipient holds SystemAdmin.</param>
        /// <param name="ageAttested">Whether the recipient attested the property minimum age.</param>
        /// <param name="hasMembership">Whether the recipient has an active membership.</param>
        /// <returns>True when the recipient may open it.</returns>
        /// <exception cref="ArgumentNullException">The policy is null.</exception>
        public static bool RecipientMayOpen(AccessPolicy policy, ContentAudienceEnum audience, bool isSystemAdmin, bool ageAttested, bool hasMembership)
        {
            return AccessDecider.RecipientMayOpen(policy, audience, isSystemAdmin, ageAttested, hasMembership);
        }

        #endregion
    }
}
