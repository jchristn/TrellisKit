namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// The caller of a content route with everything the capability matrix (Phase 2 contract section 1.1) needs:
    /// roles, the Phase 1 flags, the Phase 2 posting permissions, and mute state. Built by <see cref="ActorService"/>.
    /// SystemAdmin ignores the four permission flags and the mute. The break-glass admin token is a SystemAdmin
    /// actor without a member (it cannot author content).
    /// </summary>
    public class ContentActor
    {
        #region Public-Members

        /// <summary>
        /// Member identifier, or null for the break-glass admin token.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Member, or null for the break-glass admin token.
        /// </summary>
        public User? User { get; set; } = null;

        /// <summary>
        /// Whether the caller is the break-glass admin token.
        /// </summary>
        public bool IsBreakGlass { get; set; } = false;

        /// <summary>
        /// Whether the caller holds SystemAdmin (or is the break-glass token).
        /// </summary>
        public bool IsSystemAdmin { get; set; } = false;

        /// <summary>
        /// Whether the caller moderates the Feed: FeedModerator, or Moderator with a Global or Feed scope.
        /// </summary>
        public bool IsFeedModerator { get; set; } = false;

        /// <summary>
        /// Whether the caller holds SystemAdmin, Moderator, or FeedModerator.
        /// </summary>
        public bool IsStaff { get; set; } = false;

        /// <summary>
        /// Whether the caller holds SupportAgent (docs/PHASE5_CONTRACT.md section 1.1).
        /// </summary>
        public bool IsSupportAgent { get; set; } = false;

        /// <summary>
        /// Whether the caller holds Communicator (docs/PHASE5_CONTRACT.md as-built note D).
        /// </summary>
        public bool IsCommunicator { get; set; } = false;

        /// <summary>
        /// Whether the caller holds ReleaseManager (releases and app configuration).
        /// </summary>
        public bool IsReleaseManager { get; set; } = false;

        /// <summary>
        /// Whether the caller moderates direct message reports: Moderator with resource type Messaging (any resource id)
        /// or Global (docs/PHASE5_CONTRACT.md section 1.1). SystemAdmin is checked separately.
        /// </summary>
        public bool IsMessagingModerator { get; set; } = false;

        /// <summary>
        /// Phase 1 flag: may create feed posts (with CanPost).
        /// </summary>
        public bool CanPostToFeed { get; set; } = false;

        /// <summary>
        /// Phase 2 permission: may post at all.
        /// </summary>
        public bool CanPost { get; set; } = true;

        /// <summary>
        /// Phase 2 permission: may upload and attach media.
        /// </summary>
        public bool CanPostMedia { get; set; } = true;

        /// <summary>
        /// Phase 1 flag: may upload video.
        /// </summary>
        public bool CanUploadVideo { get; set; } = true;

        /// <summary>
        /// Whether a mute is in effect now (never for SystemAdmin).
        /// </summary>
        public bool IsMuted { get; set; } = false;

        /// <summary>
        /// End of the mute, or null when indefinite or not muted.
        /// </summary>
        public DateTime? MutedUntilUtc { get; set; } = null;

        /// <summary>
        /// Whether the caller has an active membership (any active entitlement). True for the break-glass admin token.
        /// </summary>
        public bool HasMembership { get; set; } = false;

        /// <summary>
        /// Whether the caller attested the property minimum age. True for the break-glass admin token.
        /// </summary>
        public bool AgeAttested { get; set; } = false;

        /// <summary>
        /// Whether the caller is signed out (anonymous browsing of Public content; docs/ACCESS_MODELS_CONTRACT.md).
        /// Anonymous callers cannot write, react, or report.
        /// </summary>
        public bool IsAnonymous { get; set; } = false;

        /// <summary>
        /// Source IP of an anonymous caller (rate limits), or null.
        /// </summary>
        public string? SourceIp { get; set; } = null;

        /// <summary>
        /// The property's access policy at the time the caller was resolved. Never null; default FullyGated.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public TrellisKit.Core.Models.AccessPolicy Access
        {
            get { return _Access; }
            set { _Access = value ?? throw new ArgumentNullException(nameof(Access)); }
        }

        /// <summary>
        /// The caller's access tier (Anonymous, SignedIn, Member, Staff).
        /// </summary>
        public TrellisKit.Core.Enums.AccessTierEnum Tier
        {
            get { return AccessDecider.TierOf(this); }
        }

        /// <summary>
        /// Whether the caller sees what members see: SystemAdmin, an active membership, or a signed-in account in the
        /// Open access model (which has no memberships). Used for profile field visibility.
        /// </summary>
        public bool CountsAsMember
        {
            get { return IsSystemAdmin || HasMembership || (!IsAnonymous && _Access.Model == TrellisKit.Core.Enums.AccessModelEnum.Open && !String.IsNullOrEmpty(UserId)); }
        }

        /// <summary>
        /// Whether the caller may promote threads to the feed and open feed posts for discussion (flags.canPromoteToFeed).
        /// </summary>
        public bool CanPromoteToFeed { get; set; } = false;

        /// <summary>
        /// Whether the caller moderates every forum (a Moderator assignment for every forum or a global one).
        /// </summary>
        public bool AllForumsModerator { get; set; } = false;

        /// <summary>
        /// Forums the caller moderates through Forum-scoped Moderator assignments (docs/PHASE3_CONTRACT.md section 1.1).
        /// </summary>
        public HashSet<string> ModeratedForumIds
        {
            get { return _ModeratedForumIds; }
            set { _ModeratedForumIds = value ?? new HashSet<string>(StringComparer.Ordinal); }
        }

        /// <summary>
        /// Whether the caller moderates at least one forum (or is SystemAdmin).
        /// </summary>
        public bool ModeratesAnyForum
        {
            get { return IsSystemAdmin || AllForumsModerator || _ModeratedForumIds.Count > 0; }
        }

        /// <summary>
        /// The caller's forum moderation scope: null for every forum (SystemAdmin or an all-forums moderator), else the
        /// moderated forum identifiers (possibly empty).
        /// </summary>
        public List<string>? ForumScope
        {
            get { return IsSystemAdmin || AllForumsModerator ? null : _ModeratedForumIds.OrderBy(i => i, StringComparer.Ordinal).ToList(); }
        }

        /// <summary>
        /// Whether the caller moderates a forum (SystemAdmin, an all-forums moderator, or a moderator of that forum).
        /// </summary>
        /// <param name="forumId">Forum identifier.</param>
        /// <returns>True when in scope.</returns>
        public bool CanModerateForum(string? forumId)
        {
            if (IsSystemAdmin || AllForumsModerator) return true;
            return !String.IsNullOrEmpty(forumId) && _ModeratedForumIds.Contains(forumId);
        }

        /// <summary>
        /// The provider moderation scopes the caller moderates (Directory, Events, Library), filled from the content
        /// target providers when the actor is resolved (<see cref="ContentTargetRegistry.Decorate"/>).
        /// </summary>
        public HashSet<string> ModeratedTargetScopes
        {
            get { return _ModeratedTargetScopes; }
            set { _ModeratedTargetScopes = value ?? new HashSet<string>(StringComparer.Ordinal); }
        }

        /// <summary>
        /// The provider host types (Event, Lesson) whose comments the caller moderates, filled from the content target
        /// providers when the actor is resolved.
        /// </summary>
        public HashSet<TrellisKit.Core.Enums.TargetTypeEnum> ModeratedCommentHosts
        {
            get { return _ModeratedCommentHosts; }
            set { _ModeratedCommentHosts = value ?? new HashSet<TrellisKit.Core.Enums.TargetTypeEnum>(); }
        }

        /// <summary>
        /// Whether the caller moderates comments under a provider host type (SystemAdmin always does).
        /// </summary>
        /// <param name="hostType">Host type.</param>
        /// <returns>True when the caller moderates them.</returns>
        public bool ModeratesCommentsOn(TrellisKit.Core.Enums.TargetTypeEnum hostType)
        {
            return IsSystemAdmin || _ModeratedCommentHosts.Contains(hostType);
        }

        /// <summary>
        /// Whether the caller moderates any provider scope or provider comments (reaches the moderation routes).
        /// </summary>
        public bool ModeratesAnyTargetScope
        {
            get { return _ModeratedTargetScopes.Count > 0 || _ModeratedCommentHosts.Count > 0; }
        }

        /// <summary>
        /// Holds DirectoryModerator (docs/PHASE4_CONTRACT.md section 1.1).
        /// </summary>
        public bool IsDirectoryModerator { get; set; } = false;

        /// <summary>
        /// Holds EventManager.
        /// </summary>
        public bool IsEventManager { get; set; } = false;

        /// <summary>
        /// Holds LibraryEditor (resolved decision 3): manages series, lessons, and their media without full admin rights.
        /// </summary>
        public bool IsLibraryEditor { get; set; } = false;

        /// <summary>
        /// Directory staff: SystemAdmin or DirectoryModerator.
        /// </summary>
        public bool IsDirectoryStaff
        {
            get { return IsSystemAdmin || IsDirectoryModerator; }
        }

        /// <summary>
        /// Event staff: SystemAdmin or EventManager.
        /// </summary>
        public bool IsEventStaff
        {
            get { return IsSystemAdmin || IsEventManager; }
        }

        /// <summary>
        /// Library staff: SystemAdmin or LibraryEditor.
        /// </summary>
        public bool IsLibraryStaff
        {
            get { return IsSystemAdmin || IsLibraryEditor; }
        }

        /// <summary>
        /// Whether the caller may moderate feed content (SystemAdmin or FeedModerator).
        /// </summary>
        public bool CanModerateFeed
        {
            get { return IsSystemAdmin || IsFeedModerator; }
        }

        /// <summary>
        /// Whether the caller may create feed posts (SystemAdmin, or canPostToFeed and canPost). Ignores the mute.
        /// </summary>
        public bool CanCreateFeedPost
        {
            get { return IsSystemAdmin || (CanPostToFeed && CanPost); }
        }

        /// <summary>
        /// Whether the caller may attach images now (SystemAdmin, or canPost, canPostMedia, and not muted).
        /// </summary>
        public bool CanAttachMedia
        {
            get { return IsSystemAdmin || (CanPost && CanPostMedia && !IsMuted); }
        }

        /// <summary>
        /// Whether the caller may attach video now (the image rule plus canUploadVideo, or SystemAdmin).
        /// </summary>
        public bool CanAttachVideo
        {
            get { return IsSystemAdmin || (CanAttachMedia && CanUploadVideo); }
        }

        /// <summary>
        /// Whether the caller may write text now (SystemAdmin, or canPost and not muted).
        /// </summary>
        public bool CanWrite
        {
            get { return IsSystemAdmin || (CanPost && !IsMuted); }
        }

        #endregion

        #region Private-Members

        private TrellisKit.Core.Models.AccessPolicy _Access = TrellisKit.Core.Models.AccessPolicy.FullyGatedDefault;
        private HashSet<string> _ModeratedForumIds = new HashSet<string>(StringComparer.Ordinal);
        private HashSet<string> _ModeratedTargetScopes = new HashSet<string>(StringComparer.Ordinal);
        private HashSet<TrellisKit.Core.Enums.TargetTypeEnum> _ModeratedCommentHosts = new HashSet<TrellisKit.Core.Enums.TargetTypeEnum>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether the caller is staff for an object type (sees and edits staff: labels and tags): SystemAdmin for
        /// every type; Communicators for broadcasts, segments, and deliveries; Feed moderators for feed content (posts, comments, their revisions, media, reactions, reports, moderation actions).
        /// </summary>
        /// <param name="targetType">Object type.</param>
        /// <returns>True when staff.</returns>
        public bool IsStaffFor(TrellisKit.Core.Enums.TargetTypeEnum targetType)
        {
            if (IsSystemAdmin) return true;
            if (IsSupportAgent && (targetType == TrellisKit.Core.Enums.TargetTypeEnum.Idea || targetType == TrellisKit.Core.Enums.TargetTypeEnum.IdeaStatus || targetType == TrellisKit.Core.Enums.TargetTypeEnum.IdeaVote)) return true;
            if (IsSupportAgent && (targetType == TrellisKit.Core.Enums.TargetTypeEnum.Ticket || targetType == TrellisKit.Core.Enums.TargetTypeEnum.TicketMessage
                || targetType == TrellisKit.Core.Enums.TargetTypeEnum.TicketType)) return true;
            if (IsCommunicator && (targetType == TrellisKit.Core.Enums.TargetTypeEnum.Broadcast || targetType == TrellisKit.Core.Enums.TargetTypeEnum.BroadcastSegment ||
                targetType == TrellisKit.Core.Enums.TargetTypeEnum.BroadcastDelivery)) return true;
            if (IsReleaseManager && (targetType == TrellisKit.Core.Enums.TargetTypeEnum.UpdateGroup || targetType == TrellisKit.Core.Enums.TargetTypeEnum.Update ||
                targetType == TrellisKit.Core.Enums.TargetTypeEnum.FeatureFlag || targetType == TrellisKit.Core.Enums.TargetTypeEnum.ContentVersion ||
                targetType == TrellisKit.Core.Enums.TargetTypeEnum.RemoteString || targetType == TrellisKit.Core.Enums.TargetTypeEnum.Banner ||
                targetType == TrellisKit.Core.Enums.TargetTypeEnum.RemoteAsset)) return true;
            if (IsEventStaff && (targetType == TrellisKit.Core.Enums.TargetTypeEnum.Event || targetType == TrellisKit.Core.Enums.TargetTypeEnum.EventCategory ||
                targetType == TrellisKit.Core.Enums.TargetTypeEnum.EventRsvp || targetType == TrellisKit.Core.Enums.TargetTypeEnum.EventRecurrence ||
                targetType == TrellisKit.Core.Enums.TargetTypeEnum.EventReminder)) return true;
            if (IsLibraryStaff && (targetType == TrellisKit.Core.Enums.TargetTypeEnum.Series || targetType == TrellisKit.Core.Enums.TargetTypeEnum.Lesson ||
                targetType == TrellisKit.Core.Enums.TargetTypeEnum.LessonResource || targetType == TrellisKit.Core.Enums.TargetTypeEnum.LessonMarker ||
                targetType == TrellisKit.Core.Enums.TargetTypeEnum.LessonCaption || targetType == TrellisKit.Core.Enums.TargetTypeEnum.LibraryLevel)) return true;
            if (IsDirectoryStaff && (targetType == TrellisKit.Core.Enums.TargetTypeEnum.Venue || targetType == TrellisKit.Core.Enums.TargetTypeEnum.Instructor ||
                targetType == TrellisKit.Core.Enums.TargetTypeEnum.VenueInstructor || targetType == TrellisKit.Core.Enums.TargetTypeEnum.DirectoryProgram ||
                targetType == TrellisKit.Core.Enums.TargetTypeEnum.DirectorySubmission || targetType == TrellisKit.Core.Enums.TargetTypeEnum.VenueReview ||
                targetType == TrellisKit.Core.Enums.TargetTypeEnum.VenueMedia)) return true;
            if (ModeratesAnyForum)
            {
                switch (targetType)
                {
                    case TrellisKit.Core.Enums.TargetTypeEnum.Forum:
                    case TrellisKit.Core.Enums.TargetTypeEnum.Thread:
                    case TrellisKit.Core.Enums.TargetTypeEnum.ThreadRevision:
                    case TrellisKit.Core.Enums.TargetTypeEnum.Poll:
                    case TrellisKit.Core.Enums.TargetTypeEnum.PollOption:
                    case TrellisKit.Core.Enums.TargetTypeEnum.ForumMute:
                    case TrellisKit.Core.Enums.TargetTypeEnum.Comment:
                    case TrellisKit.Core.Enums.TargetTypeEnum.CommentRevision:
                    case TrellisKit.Core.Enums.TargetTypeEnum.Report:
                    case TrellisKit.Core.Enums.TargetTypeEnum.ModerationAction:
                        return true;
                }
            }
            if (!IsFeedModerator) return false;
            switch (targetType)
            {
                case TrellisKit.Core.Enums.TargetTypeEnum.FeedPost:
                case TrellisKit.Core.Enums.TargetTypeEnum.Comment:
                case TrellisKit.Core.Enums.TargetTypeEnum.ProfileMedia:
                case TrellisKit.Core.Enums.TargetTypeEnum.Media:
                case TrellisKit.Core.Enums.TargetTypeEnum.Reaction:
                case TrellisKit.Core.Enums.TargetTypeEnum.Report:
                case TrellisKit.Core.Enums.TargetTypeEnum.ModerationAction:
                case TrellisKit.Core.Enums.TargetTypeEnum.FeedPostRevision:
                case TrellisKit.Core.Enums.TargetTypeEnum.CommentRevision:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The member identifier, for routes that need a member (the break-glass token cannot author).
        /// </summary>
        /// <returns>Member identifier.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden for the break-glass token.</exception>
        public string RequireUserId()
        {
            if (String.IsNullOrEmpty(UserId)) throw ApiErrorException.Forbidden("This route needs a member session; the admin token cannot call it.");
            return UserId;
        }

        /// <summary>
        /// Throw 403 Muted when a mute is in effect. The body carries mutedUntilUtc (null for an indefinite mute); the
        /// message is plain text without a timestamp, so clients format the end time themselves.
        /// </summary>
        /// <exception cref="ApiErrorException">403 Muted.</exception>
        public void RequireNotMuted()
        {
            if (IsSystemAdmin || !IsMuted) return;
            throw MutedError(MutedUntilUtc);
        }

        /// <summary>
        /// The 403 Muted error with mutedUntilUtc in the body.
        /// </summary>
        /// <param name="mutedUntilUtc">When the mute ends, or null for an indefinite mute.</param>
        /// <returns>Exception.</returns>
        public static ApiErrorException MutedError(DateTime? mutedUntilUtc)
        {
            string message = mutedUntilUtc == null
                ? "Your account is muted, so you cannot post, edit, react, or upload."
                : "Your account is muted for now, so you cannot post, edit, react, or upload until the mute ends.";
            return new ApiErrorException(403, "Muted", message, null, new MutedErrorResponse { Error = "Muted", Message = message, MutedUntilUtc = mutedUntilUtc });
        }

        /// <summary>
        /// Throw 403 Muted or 403 PostingNotAllowed unless the caller may write text.
        /// </summary>
        /// <exception cref="ApiErrorException">403 Muted or 403 PostingNotAllowed.</exception>
        public void RequireCanPost()
        {
            if (IsSystemAdmin) return;
            RequireNotMuted();
            if (!CanPost) throw new ApiErrorException(403, "PostingNotAllowed", "Posting is turned off for your account.");
        }

        /// <summary>
        /// Throw unless the caller may upload or attach media (any kind).
        /// </summary>
        /// <exception cref="ApiErrorException">403 Muted, 403 PostingNotAllowed, or 403 MediaNotAllowed.</exception>
        public void RequireCanPostMedia()
        {
            if (IsSystemAdmin) return;
            RequireCanPost();
            if (!CanPostMedia) throw new ApiErrorException(403, "MediaNotAllowed", "Media uploads are turned off for your account; you can still post text.");
        }

        /// <summary>
        /// Throw unless the caller may upload or attach video.
        /// </summary>
        /// <exception cref="ApiErrorException">403 Muted, 403 PostingNotAllowed, 403 MediaNotAllowed, or 403 VideoUploadNotAllowed.</exception>
        public void RequireCanUploadVideo()
        {
            if (IsSystemAdmin) return;
            RequireCanPostMedia();
            if (!CanUploadVideo) throw new ApiErrorException(403, "VideoUploadNotAllowed", "Video uploads are turned off for your account; you can still attach images.");
        }

        #endregion
    }
}
