namespace TrellisKit.Server.Services
{
    using System;
    using System.Linq;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Per-event access decisions (docs/PHASE4_CONTRACT.md sections 0.4, 1.2, 4.3): who reads an event (or sees it as
    /// a locked stub), who edits, who RSVPs, and who sees attendees. Routes are declared object gated, so these rules
    /// decide per event. Thread safety: stateless.
    /// </summary>
    public static class EventAccess
    {
        #region Public-Methods

        /// <summary>
        /// Whether the caller hosts the event.
        /// </summary>
        /// <param name="e">Event.</param>
        /// <param name="actor">Caller.</param>
        /// <returns>True for a host.</returns>
        public static bool IsHost(EventRecord e, ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(e);
            ArgumentNullException.ThrowIfNull(actor);
            return !String.IsNullOrEmpty(actor.UserId) && e.HostUserIds.Contains(actor.UserId, StringComparer.Ordinal);
        }

        /// <summary>
        /// Whether the caller is event staff or a host of the event.
        /// </summary>
        /// <param name="e">Event.</param>
        /// <param name="actor">Caller.</param>
        /// <returns>True when staff or host.</returns>
        public static bool IsStaffOrHost(EventRecord e, ContentActor actor)
        {
            return actor.IsEventStaff || IsHost(e, actor);
        }

        /// <summary>
        /// How the caller may see an event.
        /// </summary>
        /// <param name="e">Event.</param>
        /// <param name="actor">Caller.</param>
        /// <returns>Decision.</returns>
        public static EventReadEnum Read(EventRecord e, ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(e);
            ArgumentNullException.ThrowIfNull(actor);
            if (e.DeletedUtc != null || e.Status == EventStatusEnum.Template) return EventReadEnum.Hidden;
            bool staff = actor.IsEventStaff;
            bool host = IsHost(e, actor);
            bool proposer = !String.IsNullOrEmpty(actor.UserId) && String.Equals(e.ProposedByUserId, actor.UserId, StringComparison.Ordinal);
            switch (e.Status)
            {
                case EventStatusEnum.Draft:
                    return staff || host ? EventReadEnum.Open : EventReadEnum.Hidden;
                case EventStatusEnum.Proposed:
                case EventStatusEnum.Rejected:
                case EventStatusEnum.Withdrawn:
                    return staff || proposer ? EventReadEnum.Open : EventReadEnum.Hidden;
            }
            if (e.ModerationState != ModerationStateEnum.None) return staff ? EventReadEnum.Open : EventReadEnum.Hidden;
            if (staff || host) return EventReadEnum.Open;
            if (ContentAudienceRule.Meets(e.Audience, actor)) return EventReadEnum.Open;
            if (AccessDecider.ShowsLocked(actor.Access, actor.Tier, e.Audience)) return EventReadEnum.Locked;
            return EventReadEnum.Hidden;
        }

        /// <summary>
        /// The tier an RSVP needs: the higher of the event's participate tier (events feature) and the tier of its
        /// rsvpAudience.
        /// </summary>
        /// <param name="e">Event.</param>
        /// <param name="policy">Access policy.</param>
        /// <returns>Tier.</returns>
        public static AccessTierEnum RsvpTier(EventRecord e, AccessPolicy policy)
        {
            ArgumentNullException.ThrowIfNull(e);
            ArgumentNullException.ThrowIfNull(policy);
            AccessTierEnum participate = AccessDecider.ParticipateTier(policy, FeatureKeyEnum.Events, e.Audience);
            AccessTierEnum rsvp = AccessDecider.ReadTier(policy, e.RsvpAudience == ContentAudienceEnum.Public ? ContentAudienceEnum.SignedIn : e.RsvpAudience);
            if (rsvp < AccessTierEnum.SignedIn) rsvp = AccessTierEnum.SignedIn;
            return rsvp > participate ? rsvp : participate;
        }

        /// <summary>
        /// Whether the caller's tier allows an RSVP on the event (not the window or the status).
        /// </summary>
        /// <param name="e">Event.</param>
        /// <param name="actor">Caller.</param>
        /// <returns>True when allowed.</returns>
        public static bool MayRsvp(EventRecord e, ContentActor actor)
        {
            if (actor.IsAnonymous || String.IsNullOrEmpty(actor.UserId)) return false;
            if (actor.IsSystemAdmin) return true;
            return AccessDecider.Meets(actor.Tier, RsvpTier(e, actor.Access));
        }

        /// <summary>
        /// Whether RSVPs are open now (published, not started, inside the window).
        /// </summary>
        /// <param name="e">Event.</param>
        /// <param name="nowUtc">Now.</param>
        /// <returns>True when open.</returns>
        public static bool RsvpOpen(EventRecord e, DateTime nowUtc)
        {
            if (e.Status != EventStatusEnum.Published || e.ModerationState != ModerationStateEnum.None) return false;
            if (nowUtc >= e.StartUtc) return false;
            DateTime opens = e.RsvpOpensUtc ?? e.PublishedUtc ?? DateTime.MinValue;
            if (nowUtc < opens) return false;
            if (e.RsvpClosesUtc != null && nowUtc >= e.RsvpClosesUtc.Value) return false;
            return true;
        }

        /// <summary>
        /// Whether the caller may take part (comment, react) on the event.
        /// </summary>
        /// <param name="e">Event.</param>
        /// <param name="actor">Caller.</param>
        /// <returns>True when the tier allows it.</returns>
        public static bool MayParticipate(EventRecord e, ContentActor actor)
        {
            if (actor.IsAnonymous || String.IsNullOrEmpty(actor.UserId)) return false;
            if (actor.IsSystemAdmin || actor.IsEventStaff) return true;
            return AccessDecider.Meets(actor.Tier, AccessDecider.ParticipateTier(actor.Access, FeatureKeyEnum.Events, e.Audience));
        }

        /// <summary>
        /// Whether the caller may see the attendee list (attendeeVisibility; staff and hosts always).
        /// </summary>
        /// <param name="e">Event.</param>
        /// <param name="actor">Caller.</param>
        /// <param name="myRsvp">The caller's RSVP, or null.</param>
        /// <returns>True when allowed.</returns>
        public static bool CanSeeAttendees(EventRecord e, ContentActor actor, EventRsvp? myRsvp)
        {
            if (IsStaffOrHost(e, actor)) return true;
            if (actor.IsAnonymous || String.IsNullOrEmpty(actor.UserId)) return false;
            switch (e.AttendeeVisibility)
            {
                case EventAttendeeVisibilityEnum.Everyone:
                    return true;
                case EventAttendeeVisibilityEnum.Attendees:
                    return myRsvp != null && myRsvp.Status == EventRsvpStatusEnum.Going;
                default:
                    return false;
            }
        }

        /// <summary>
        /// When the join URL becomes available.
        /// </summary>
        /// <param name="e">Event.</param>
        /// <param name="revealMinutes">events.joinUrlRevealMinutes.</param>
        /// <returns>Instant.</returns>
        public static DateTime JoinUrlAvailableUtc(EventRecord e, int revealMinutes)
        {
            return DateTime.SpecifyKind(e.StartUtc.AddMinutes(-Math.Max(0, revealMinutes)), DateTimeKind.Utc);
        }

        #endregion
    }
}
