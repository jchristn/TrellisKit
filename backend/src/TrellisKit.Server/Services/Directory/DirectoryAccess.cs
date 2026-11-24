namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Per-object access rules of the directory (docs/PHASE4_CONTRACT.md sections 0.4 and 1, resolved decision 2): staff
    /// see everything; others read an entry when they meet its audience, see a locked stub when PartiallyGated would
    /// show one, and otherwise get 404. Thread safe (stateless).
    /// </summary>
    public static class DirectoryAccess
    {
        #region Public-Methods

        /// <summary>
        /// How a caller may see an object of an audience: 2 full, 1 locked stub, 0 hidden.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="audience">Stored audience.</param>
        /// <returns>2, 1, or 0.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        public static int ReadLevel(ContentActor actor, ContentAudienceEnum audience)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (actor.IsDirectoryStaff) return 2;
            if (ContentAudienceRule.Meets(audience, actor)) return 2;
            if (AccessDecider.ShowsLocked(actor.Access, AccessDecider.TierOf(actor), audience)) return 1;
            return 0;
        }

        /// <summary>
        /// Audiences a caller's lists include (readable and locked), or null for staff (every audience).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <returns>Audiences, or null.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        public static List<ContentAudienceEnum>? ListedAudiences(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            return actor.IsDirectoryStaff ? null : ContentAudienceRule.ListedAudiences(actor);
        }

        /// <summary>
        /// Audiences a caller may fully read, or null for staff (maps show no stubs).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <returns>Audiences, or null.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        public static List<ContentAudienceEnum>? VisibleAudiences(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            return actor.IsDirectoryStaff ? null : ContentAudienceRule.VisibleAudiences(actor);
        }

        /// <summary>
        /// Whether a non-staff caller may see a venue at all (not archived, deleted, or moderated).
        /// </summary>
        /// <param name="venue">Venue.</param>
        /// <returns>True when listed or readable.</returns>
        /// <exception cref="ArgumentNullException">The venue is null.</exception>
        public static bool IsLive(Venue venue)
        {
            ArgumentNullException.ThrowIfNull(venue);
            return venue.DeletedUtc == null && venue.Status != VenueStatusEnum.Archived && venue.ModerationState == ModerationStateEnum.None;
        }

        /// <summary>
        /// Whether a non-staff caller may see an instructor at all.
        /// </summary>
        /// <param name="instructor">Instructor.</param>
        /// <returns>True when listed or readable.</returns>
        /// <exception cref="ArgumentNullException">The instructor is null.</exception>
        public static bool IsLive(Instructor instructor)
        {
            ArgumentNullException.ThrowIfNull(instructor);
            return instructor.DeletedUtc == null && instructor.Status == InstructorStatusEnum.Active && instructor.ModerationState == ModerationStateEnum.None;
        }

        /// <summary>
        /// Whether a caller may make submissions under the access model (the feature's write tier).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <returns>True when allowed.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        public static bool MaySubmit(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (actor.IsAnonymous || String.IsNullOrEmpty(actor.UserId)) return false;
            if (actor.IsDirectoryStaff) return true;
            return actor.CanWrite && AccessDecider.Meets(AccessDecider.TierOf(actor), AccessDecider.WriteTier(actor.Access, FeatureKeyEnum.Directory));
        }

        /// <summary>
        /// Require directory staff (SystemAdmin or DirectoryModerator).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden.</exception>
        public static void RequireStaff(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (!actor.IsDirectoryStaff) throw ApiErrorException.Forbidden("Directory staff (SystemAdmin or DirectoryModerator) only.");
        }

        /// <summary>
        /// Require SystemAdmin (directory configuration, programs, geocoder, and map).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden.</exception>
        public static void RequireAdmin(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (!actor.IsSystemAdmin) throw ApiErrorException.Forbidden("SystemAdmin only.");
        }

        /// <summary>
        /// The requires value of a locked stub.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <returns>SignIn or Membership.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        public static string Requires(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            return actor.IsAnonymous || String.IsNullOrEmpty(actor.UserId) ? "SignIn" : "Membership";
        }

        #endregion
    }
}
