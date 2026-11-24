namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Per-object access decisions of the library (docs/PHASE4_CONTRACT.md section 0.4): which series and lessons a
    /// caller reads in full, which are locked stubs, and who may play, take part, and download. The route gate only
    /// requires a signed-in caller (or anonymous for anonymous-eligible reads); these rules decide per object.
    /// Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public static class LibraryAccess
    {
        #region Public-Methods

        /// <summary>
        /// Whether the caller meets an audience in full (library staff always do).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="audience">Audience.</param>
        /// <returns>True when readable.</returns>
        public static bool CanRead(ContentActor actor, ContentAudienceEnum audience)
        {
            ArgumentNullException.ThrowIfNull(actor);
            return actor.IsLibraryStaff || ContentAudienceRule.Meets(audience, actor);
        }

        /// <summary>
        /// Whether the caller sees an object of an audience as a locked stub (PartiallyGated, a signed-in caller
        /// without a membership, a Members object).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="audience">Audience.</param>
        /// <returns>True when locked.</returns>
        public static bool ShowsLocked(ContentActor actor, ContentAudienceEnum audience)
        {
            ArgumentNullException.ThrowIfNull(actor);
            return !CanRead(actor, audience) && AccessDecider.ShowsLocked(actor.Access, actor.Tier, audience);
        }

        /// <summary>
        /// The audiences the caller reads in full.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <returns>Audiences.</returns>
        public static List<ContentAudienceEnum> Readable(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (actor.IsLibraryStaff) return new List<ContentAudienceEnum>(Enum.GetValues<ContentAudienceEnum>());
            return ContentAudienceRule.VisibleAudiences(actor);
        }

        /// <summary>
        /// The audiences the caller receives as locked stubs.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <returns>Audiences.</returns>
        public static List<ContentAudienceEnum> Stubs(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            List<ContentAudienceEnum> result = new List<ContentAudienceEnum>();
            foreach (ContentAudienceEnum audience in Enum.GetValues<ContentAudienceEnum>())
                if (ShowsLocked(actor, audience)) result.Add(audience);
            return result;
        }

        /// <summary>
        /// The tier that playing, recording progress, commenting, and reacting on a lesson need: a membership in
        /// FullyGated; otherwise the higher of SignedIn and the lesson's read tier (so sample lessons whose audience is
        /// Public or SignedIn need only an account in PartiallyGated, even when the library is gated).
        /// </summary>
        /// <param name="policy">Access policy.</param>
        /// <param name="audience">Lesson audience.</param>
        /// <returns>Tier.</returns>
        public static AccessTierEnum ParticipateTier(AccessPolicy policy, ContentAudienceEnum audience)
        {
            ArgumentNullException.ThrowIfNull(policy);
            if (policy.Model == AccessModelEnum.FullyGated) return AccessTierEnum.Member;
            AccessTierEnum read = AccessDecider.ReadTier(policy, audience);
            return read < AccessTierEnum.SignedIn ? AccessTierEnum.SignedIn : read;
        }

        /// <summary>
        /// Whether the caller may take part on a lesson (progress, comments, reactions).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="audience">Lesson audience.</param>
        /// <returns>True when allowed.</returns>
        public static bool CanParticipate(ContentActor actor, ContentAudienceEnum audience)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (actor.IsLibraryStaff) return true;
            if (actor.IsAnonymous || String.IsNullOrEmpty(actor.UserId)) return false;
            return AccessDecider.Meets(actor.Tier, ParticipateTier(actor.Access, audience));
        }

        /// <summary>
        /// Whether the caller may play a lesson's media: anyone who reads it in full (a Public lesson plays without an
        /// account).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="audience">Lesson audience.</param>
        /// <returns>True when allowed.</returns>
        public static bool CanPlay(ContentActor actor, ContentAudienceEnum audience)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (!CanRead(actor, audience)) return false;
            return audience == ContentAudienceEnum.Public || CanParticipate(actor, audience);
        }

        /// <summary>
        /// Whether the caller qualifies for offline downloads of a lesson (section 5.9): may play it and, in gated
        /// models (FullyGated, or the library gated in PartiallyGated), holds a membership.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="audience">Lesson audience.</param>
        /// <returns>True when the caller qualifies.</returns>
        public static bool QualifiesForDownload(ContentActor actor, ContentAudienceEnum audience)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (actor.IsAnonymous || String.IsNullOrEmpty(actor.UserId)) return false;
            if (!CanParticipate(actor, audience) || !CanRead(actor, audience)) return false;
            bool gated = actor.Access.Model == AccessModelEnum.FullyGated || actor.Access.IsGated(FeatureKeyEnum.Library);
            return !gated || actor.HasMembership || actor.IsLibraryStaff;
        }

        /// <summary>
        /// What a locked stub requires: SignIn for signed-out callers, else Membership.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <returns>Requirement.</returns>
        public static string Requires(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            return actor.IsAnonymous || String.IsNullOrEmpty(actor.UserId) ? "SignIn" : "Membership";
        }

        /// <summary>
        /// The access model denial for an object the caller sees but may not use (docs/ACCESS_MODELS_CONTRACT.md
        /// section 3.4): 401 Unauthorized (required SignIn) signed out, else 402 MembershipRequired (required Membership).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <returns>Exception to throw.</returns>
        public static ApiErrorException Denied(ContentActor actor, TargetTypeEnum targetType, string targetId)
        {
            ArgumentNullException.ThrowIfNull(actor);
            bool signedOut = actor.IsAnonymous || String.IsNullOrEmpty(actor.UserId);
            string error = signedOut ? "Unauthorized" : "MembershipRequired";
            string message = signedOut ? "Sign in to open this." : "This is for members.";
            TrellisKit.Core.Responses.AccessDeniedResponse body = new TrellisKit.Core.Responses.AccessDeniedResponse
            {
                Error = error,
                Message = message,
                Required = signedOut ? "SignIn" : "Membership",
                Feature = AccessPolicy.FeatureKey(FeatureKeyEnum.Library),
                TargetType = targetType.ToString(),
                TargetId = targetId
            };
            return new ApiErrorException(signedOut ? 401 : 402, error, message, null, body);
        }

        /// <summary>
        /// Require library staff (SystemAdmin or LibraryEditor).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <exception cref="ApiErrorException">403 Forbidden.</exception>
        public static void RequireStaff(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (!actor.IsLibraryStaff) throw ApiErrorException.Forbidden("Only library staff (SystemAdmin or LibraryEditor) can manage the library.");
        }

        /// <summary>
        /// Require a SystemAdmin (library settings and levels).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <exception cref="ApiErrorException">403 Forbidden.</exception>
        public static void RequireAdmin(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (!actor.IsSystemAdmin) throw ApiErrorException.Forbidden("Only a SystemAdmin can change library settings and levels.");
        }

        /// <summary>
        /// Whether a lesson is live for members (published, not moderated, not deleted) inside a live series.
        /// </summary>
        /// <param name="lesson">Lesson.</param>
        /// <param name="series">Series, or null.</param>
        /// <returns>True when live.</returns>
        public static bool IsLive(Lesson lesson, LibrarySeries? series)
        {
            ArgumentNullException.ThrowIfNull(lesson);
            return lesson.DeletedUtc == null && lesson.Status == LibraryStatusEnum.Published && lesson.ModerationState == ModerationStateEnum.None &&
                series != null && series.DeletedUtc == null && series.Status == LibraryStatusEnum.Published;
        }

        /// <summary>
        /// Whether an archived lesson is still within the archived playback window.
        /// </summary>
        /// <param name="lesson">Lesson.</param>
        /// <param name="series">Series, or null.</param>
        /// <param name="archivedPlaybackDays">library.archivedPlaybackDays.</param>
        /// <param name="nowUtc">Now.</param>
        /// <returns>True when still playable for members with progress.</returns>
        public static bool InArchiveWindow(Lesson lesson, LibrarySeries? series, int archivedPlaybackDays, DateTime nowUtc)
        {
            ArgumentNullException.ThrowIfNull(lesson);
            if (series == null || series.DeletedUtc != null || lesson.DeletedUtc != null || lesson.ModerationState != ModerationStateEnum.None) return false;
            DateTime cutoff = nowUtc.AddDays(-archivedPlaybackDays);
            bool lessonOk = lesson.Status == LibraryStatusEnum.Published || (lesson.Status == LibraryStatusEnum.Archived && lesson.ArchivedUtc != null && lesson.ArchivedUtc >= cutoff);
            bool seriesOk = series.Status == LibraryStatusEnum.Published || (series.Status == LibraryStatusEnum.Archived && series.ArchivedUtc != null && series.ArchivedUtc >= cutoff);
            return lessonOk && seriesOk && (lesson.Status == LibraryStatusEnum.Archived || series.Status == LibraryStatusEnum.Archived);
        }

        #endregion
    }
}
