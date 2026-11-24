namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;

    /// <summary>
    /// The one forum authorization component (docs/PHASE3_CONTRACT.md sections 0.4 and 1): feature switch, forum and
    /// thread audience, thread states per caller, moderator scope, forum mutes, locks, and archives.
    /// Thread safety: stateless apart from the shared services; safe to share.
    /// </summary>
    public class ForumAccessService
    {
        #region Private-Members

        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public ForumAccessService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Fail with 404 NotAvailable when the property turned forums off (features.forums false).
        /// </summary>
        /// <exception cref="ApiErrorException">404 NotAvailable.</exception>
        public void RequireEnabled()
        {
            if (!_Services.Property.Property.Features.Forums) throw new ApiErrorException(404, "NotAvailable", "Forums are not available in this community.");
        }

        /// <summary>
        /// The state of a thread.
        /// </summary>
        /// <param name="thread">Thread.</param>
        /// <returns>State.</returns>
        public static ThreadStateEnum StateOf(ForumThread thread)
        {
            ArgumentNullException.ThrowIfNull(thread);
            if (thread.DeletedUtc != null) return ThreadStateEnum.Deleted;
            if (thread.ModerationState == ModerationStateEnum.Removed) return ThreadStateEnum.Removed;
            if (thread.ModerationState == ModerationStateEnum.Hidden) return ThreadStateEnum.Hidden;
            return thread.PublishedUtc == null ? ThreadStateEnum.Processing : ThreadStateEnum.Published;
        }

        /// <summary>
        /// Whether the caller may read a forum (its audience, or moderator scope).
        /// </summary>
        /// <param name="forum">Forum.</param>
        /// <param name="actor">Caller.</param>
        /// <returns>True when readable.</returns>
        public static bool CanReadForum(Forum forum, ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(forum);
            ArgumentNullException.ThrowIfNull(actor);
            if (forum.DeletedUtc != null) return false;
            if (actor.CanModerateForum(forum.Id)) return true;
            return ModelAllowsRead(actor) && ContentAudienceRule.Meets(forum.Audience, actor);
        }

        /// <summary>
        /// Whether the caller may see a thread: the author in every state, moderators in scope Published, Hidden, and
        /// Removed threads, SystemAdmin everything except deleted threads, others Published threads whose thread and
        /// forum audiences they meet. A LinkedFeedPost thread whose feed post is not live is visible to staff only.
        /// </summary>
        /// <param name="context">Thread context.</param>
        /// <param name="actor">Caller.</param>
        /// <returns>True when visible.</returns>
        public static bool CanSeeThread(ThreadContext context, ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(actor);
            ForumThread t = context.Thread;
            ThreadStateEnum state = StateOf(t);
            if (state == ThreadStateEnum.Deleted || context.Forum.DeletedUtc != null) return false;
            if (actor.IsSystemAdmin) return true;
            bool staff = actor.CanModerateForum(t.ForumId);
            if (!staff && !ModelAllowsRead(actor)) return false;
            if (t.Kind == ThreadKindEnum.LinkedFeedPost && !SourceLive(context.Source) && !staff) return false;
            if (actor.UserId != null && String.Equals(t.AuthorUserId, actor.UserId, StringComparison.Ordinal)) return true;
            if (staff && state != ThreadStateEnum.Processing) return true;
            return state == ThreadStateEnum.Published && ContentAudienceRule.Meets(t.Audience, actor) && ContentAudienceRule.Meets(context.Forum.Audience, actor);
        }

        /// <summary>
        /// Whether the access model lets the caller read forums at all (docs/ACCESS_MODELS_CONTRACT.md section 2): in
        /// PartiallyGated with forums gated, reading needs an active membership (staff pass). The route gate applies the
        /// same rule; this keeps object paths that are not forum routes (comments, reactions, media, promoted feed cards)
        /// consistent with it.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <returns>True when allowed.</returns>
        public static bool ModelAllowsRead(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (!actor.Access.IsGated(FeatureKeyEnum.Forums)) return true;
            return AccessDecider.Meets(AccessDecider.TierOf(actor), AccessTierEnum.Member);
        }

        /// <summary>
        /// Whether the access model lets the caller write in forums (create threads): the forums write tier.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <returns>True when allowed.</returns>
        public static bool ModelAllowsWrite(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (actor.IsAnonymous) return false;
            return AccessDecider.Meets(AccessDecider.TierOf(actor), AccessDecider.WriteTier(actor.Access, FeatureKeyEnum.Forums));
        }

        /// <summary>
        /// Whether the access model lets the caller take part (comment, react, vote) on content with an audience: the
        /// higher of the content's read tier (at least SignedIn) and the forums write tier.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="audience">Narrowest audience of the content (thread and forum).</param>
        /// <returns>True when allowed.</returns>
        public static bool ModelAllowsParticipation(ContentActor actor, ContentAudienceEnum audience)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (actor.IsAnonymous) return false;
            return AccessDecider.Meets(AccessDecider.TierOf(actor), AccessDecider.ParticipateTier(actor.Access, FeatureKeyEnum.Forums, audience));
        }

        /// <summary>
        /// The audience of a new forum: Members when forums are gated (PartiallyGated), otherwise the property default
        /// (docs/PHASE3_CONTRACT.md section 0.4: new forums default to access.defaultAudience).
        /// </summary>
        /// <param name="policy">Access policy.</param>
        /// <returns>Audience.</returns>
        public static ContentAudienceEnum DefaultForumAudience(AccessPolicy policy)
        {
            ArgumentNullException.ThrowIfNull(policy);
            return policy.DefaultAudienceFor(FeatureKeyEnum.Forums);
        }

        /// <summary>
        /// Whether a LinkedFeedPost thread's source feed post is live (Published and not moderated or deleted).
        /// </summary>
        /// <param name="source">Source post, or null.</param>
        /// <returns>True when live.</returns>
        public static bool SourceLive(FeedPost? source)
        {
            return source != null && source.StateAt(DateTime.UtcNow) == FeedPostStateEnum.Published;
        }

        /// <summary>
        /// Whether the caller moderates the thread (scope covers the forum) and may act on its author (a Moderator
        /// never acts on SystemAdmin content; SystemAdmin acts on anything).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="forumId">Forum of the content.</param>
        /// <param name="authorIsSystemAdmin">Whether the content author is a SystemAdmin.</param>
        /// <returns>True when the caller may moderate it.</returns>
        public static bool CanModerate(ContentActor actor, string forumId, bool authorIsSystemAdmin)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (actor.IsSystemAdmin) return true;
            return actor.CanModerateForum(forumId) && !authorIsSystemAdmin;
        }

        /// <summary>
        /// Whether a caller may create threads in a forum by its threadCreation rule.
        /// </summary>
        /// <param name="forum">Forum.</param>
        /// <param name="actor">Caller.</param>
        /// <returns>True when allowed.</returns>
        public static bool CreationAllows(Forum forum, ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(forum);
            ArgumentNullException.ThrowIfNull(actor);
            if (actor.IsSystemAdmin) return true;
            switch (forum.ThreadCreation)
            {
                case ThreadCreationEnum.Admins:
                    return false;
                case ThreadCreationEnum.Moderators:
                    return actor.CanModerateForum(forum.Id);
                default:
                    return true;
            }
        }

        /// <summary>
        /// A forum's allowed types filtered by the caller's rights (Feedback needs video uploads in the forum).
        /// </summary>
        /// <param name="forum">Forum.</param>
        /// <param name="actor">Caller.</param>
        /// <returns>Types.</returns>
        public static List<ThreadTypeEnum> AllowedTypesFor(Forum forum, ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(forum);
            ArgumentNullException.ThrowIfNull(actor);
            return forum.AllowedThreadTypes.Where(t => t != ThreadTypeEnum.Feedback || (forum.AllowMedia && forum.AllowVideo && actor.CanAttachVideo)).ToList();
        }

        /// <summary>
        /// Audience rank (Public 0, SignedIn 1, Members 2); higher is narrower.
        /// </summary>
        /// <param name="audience">Audience.</param>
        /// <returns>Rank.</returns>
        public static int Rank(ContentAudienceEnum audience)
        {
            switch (audience)
            {
                case ContentAudienceEnum.Public: return 0;
                case ContentAudienceEnum.SignedIn: return 1;
                default: return 2;
            }
        }

        /// <summary>
        /// The narrower of two audiences.
        /// </summary>
        /// <param name="first">First audience.</param>
        /// <param name="second">Second audience.</param>
        /// <returns>The narrower one.</returns>
        public static ContentAudienceEnum Narrowest(ContentAudienceEnum first, ContentAudienceEnum second)
        {
            return Rank(first) >= Rank(second) ? first : second;
        }

        /// <summary>
        /// Read a forum by identifier or slug that the caller may read.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="idOrSlug">Identifier or slug.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Forum.</returns>
        /// <exception cref="ApiErrorException">404 NotFound (unknown, deleted, or not readable).</exception>
        public async Task<Forum> RequireForumAsync(ContentActor actor, string idOrSlug, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            RequireEnabled();
            string key = (idOrSlug ?? String.Empty).Trim();
            Forum? forum = key.Length == 0 ? null : await _Services.Database.Forums.ReadAsync(key, token).ConfigureAwait(false);
            if (forum == null && key.Length > 0 && !key.StartsWith("frm_", StringComparison.Ordinal)) forum = await _Services.Database.Forums.ReadBySlugAsync(key, token).ConfigureAwait(false);
            if (forum == null || !CanReadForum(forum, actor)) throw ApiErrorException.NotFound("Forum");
            return forum;
        }

        /// <summary>
        /// Load a thread with its forum and source post, without visibility checks.
        /// </summary>
        /// <param name="threadId">Thread identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Context, or null.</returns>
        public async Task<ThreadContext?> LoadAsync(string threadId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(threadId)) return null;
            ForumThread? thread = await _Services.Database.Threads.ReadAsync(threadId.Trim(), token).ConfigureAwait(false);
            if (thread == null) return null;
            List<Forum> forums = await _Services.Database.Forums.ReadManyAsync(new[] { thread.ForumId }, token).ConfigureAwait(false);
            Forum? forum = forums.FirstOrDefault();
            if (forum == null) return null;
            ThreadContext context = new ThreadContext { Thread = thread, Forum = forum };
            if (thread.Kind == ThreadKindEnum.LinkedFeedPost && thread.SourceId != null) context.Source = await _Services.Database.FeedPosts.ReadAsync(thread.SourceId, token).ConfigureAwait(false);
            return context;
        }

        /// <summary>
        /// Load several threads with their forums and source posts, in the given order, skipping unknown ids.
        /// </summary>
        /// <param name="threads">Threads.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Contexts.</returns>
        public async Task<List<ThreadContext>> ContextsAsync(IReadOnlyList<ForumThread> threads, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(threads);
            List<ThreadContext> result = new List<ThreadContext>();
            if (threads.Count == 0) return result;
            Dictionary<string, Forum> forums = (await _Services.Database.Forums.ReadManyAsync(threads.Select(t => t.ForumId), token).ConfigureAwait(false)).ToDictionary(f => f.Id, StringComparer.Ordinal);
            List<string> sourceIds = threads.Where(t => t.Kind == ThreadKindEnum.LinkedFeedPost && t.SourceId != null).Select(t => t.SourceId!).Distinct(StringComparer.Ordinal).ToList();
            Dictionary<string, FeedPost> sources = sourceIds.Count == 0 ? new Dictionary<string, FeedPost>(StringComparer.Ordinal)
                : (await _Services.Database.FeedPosts.ReadManyAsync(sourceIds, token).ConfigureAwait(false)).ToDictionary(p => p.Id, StringComparer.Ordinal);
            foreach (ForumThread thread in threads)
            {
                if (!forums.TryGetValue(thread.ForumId, out Forum? forum)) continue;
                result.Add(new ThreadContext { Thread = thread, Forum = forum, Source = thread.SourceId != null && sources.TryGetValue(thread.SourceId, out FeedPost? post) ? post : null });
            }
            return result;
        }

        /// <summary>
        /// Read a thread the caller may see.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="threadId">Thread identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Context.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<ThreadContext> RequireThreadAsync(ContentActor actor, string threadId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            RequireEnabled();
            ThreadContext? context = await LoadAsync(threadId, token).ConfigureAwait(false);
            if (context == null || !CanSeeThread(context, actor)) throw ApiErrorException.NotFound("Thread");
            return context;
        }

        /// <summary>
        /// Fail with 403 ForumMuted when the caller is muted in the forum.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="forumId">Forum.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">403 ForumMuted.</exception>
        public async Task RequireNotForumMutedAsync(ContentActor actor, string forumId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (actor.IsSystemAdmin || String.IsNullOrEmpty(actor.UserId)) return;
            ForumMute? mute = await _Services.Database.Forums.ActiveMuteAsync(actor.UserId, forumId, DateTime.UtcNow, token).ConfigureAwait(false);
            if (mute == null) return;
            string until = mute.UntilUtc == null ? "until a moderator lifts it" : "until " + mute.UntilUtc.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";
            throw new ApiErrorException(403, "ForumMuted", "You are muted in this forum " + until + ". You can still read, report, and delete your own content.");
        }

        /// <summary>
        /// Forums among the identifiers where the caller has an active mute.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="forumIds">Forums.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Muted forum identifiers.</returns>
        public async Task<HashSet<string>> MutedForumsAsync(ContentActor actor, IEnumerable<string> forumIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(forumIds);
            HashSet<string> muted = new HashSet<string>(StringComparer.Ordinal);
            if (actor.IsSystemAdmin || String.IsNullOrEmpty(actor.UserId)) return muted;
            DateTime now = DateTime.UtcNow;
            foreach (string forumId in forumIds.Distinct(StringComparer.Ordinal))
            {
                if (await _Services.Database.Forums.ActiveMuteAsync(actor.UserId, forumId, now, token).ConfigureAwait(false) != null) muted.Add(forumId);
            }
            return muted;
        }

        /// <summary>
        /// Fail with 409 ThreadLocked or 409 ForumArchived when a thread takes no new conversation.
        /// </summary>
        /// <param name="context">Thread context.</param>
        /// <exception cref="ApiErrorException">409 ThreadLocked or ForumArchived.</exception>
        public static void RequireOpen(ThreadContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (context.Thread.IsLocked) throw new ApiErrorException(409, "ThreadLocked", "This thread is locked.");
            if (context.Forum.IsArchived) throw new ApiErrorException(409, "ForumArchived", "This forum is archived and read-only.");
        }

        /// <summary>
        /// Whether a member is a SystemAdmin.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True for a SystemAdmin.</returns>
        public async Task<bool> IsSystemAdminAsync(string? userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) return false;
            HashSet<string> admins = await _Services.Summaries.SystemAdminsAsync(new[] { userId }, token).ConfigureAwait(false);
            return admins.Contains(userId);
        }

        #endregion
    }
}
