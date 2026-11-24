namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Reactions (contract section 6): the set comes from the property's reactions (read on every call, so a property
    /// reload applies at once), react and unreact on feed posts and comments (idempotent, several keys per member, one
    /// row per key), the reaction fields of payloads, and who reacted. Counts of keys removed from the property are
    /// hidden everywhere while their rows stay, so restoring the key restores the counts. Reacting to your own content
    /// is allowed and does not notify. Thread safety: safe to call concurrently.
    /// </summary>
    public class ReactionService
    {
        #region Public-Members

        /// <summary>
        /// Rate limit bucket shared by PUT and DELETE (reactions.perMinute).
        /// </summary>
        public const string RateBucket = "reactions";

        /// <summary>
        /// Resolves a Thread reaction target (or, with forWrite, checks that a thread comment may take reactions) for the
        /// forum module (Phase 3 sections 4.9 and resolved decision 14). Arguments: caller, target type (Thread or
        /// Comment), target identifier, forWrite. Returns null when the target is not a forum target.
        /// </summary>
        public Func<ContentActor, TargetTypeEnum, string, bool, CancellationToken, Task<ReactionTarget?>>? ForumTargets { get; set; } = null;

        /// <summary>
        /// Resolvers for target types added after Phase 3 (direct messages, ideas), tried in order before the built-in
        /// types. Arguments: caller, target type, target identifier, forWrite. Each returns null for target types it does
        /// not handle and throws the route error (for example 404 or 403) for a target it handles but refuses.
        /// Register during module attachment only; the list is not safe to change while requests run.
        /// </summary>
        public List<Func<ContentActor, TargetTypeEnum, string, bool, CancellationToken, Task<ReactionTarget?>>> ExtraTargets { get; } =
            new List<Func<ContentActor, TargetTypeEnum, string, bool, CancellationToken, Task<ReactionTarget?>>>();

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="services">Application services (read on each call).</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public ReactionService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// GET /api/v1/reactions/set: the property's reactions in order and the one-tap key (the first).
        /// </summary>
        /// <returns>Set.</returns>
        public ReactionSetResponse GetSet()
        {
            List<PropertyReaction> reactions = _Services.Property.Property.Reactions ?? new List<PropertyReaction>();
            return new ReactionSetResponse
            {
                Items = reactions.Select(r => new ReactionSetItem { Key = r.Key, Emoji = r.Emoji, Label = r.Label }).ToList(),
                LikeKey = reactions.Count == 0 ? null : reactions[0].Key
            };
        }

        /// <summary>
        /// Keys of the property's current set.
        /// </summary>
        /// <returns>Keys.</returns>
        public HashSet<string> CurrentKeys()
        {
            return new HashSet<string>((_Services.Property.Property.Reactions ?? new List<PropertyReaction>()).Select(r => r.Key), StringComparer.Ordinal);
        }

        /// <summary>
        /// Reaction fields for a page of targets (two queries): counts of current keys above zero, their sum, and the
        /// viewer's current keys.
        /// </summary>
        /// <param name="targetType">FeedPost or Comment.</param>
        /// <param name="targetIds">Target identifiers.</param>
        /// <param name="viewerUserId">Viewer, or null (public feed).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>States keyed by every requested identifier.</returns>
        /// <exception cref="ArgumentNullException">The identifiers are null.</exception>
        public async Task<Dictionary<string, ReactionState>> LoadAsync(TargetTypeEnum targetType, IReadOnlyCollection<string> targetIds, string? viewerUserId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(targetIds);
            HashSet<string> keys = CurrentKeys();
            Dictionary<string, Dictionary<string, long>> counts = await _Services.Database.Reactions.CountsAsync(targetType, targetIds, token).ConfigureAwait(false);
            Dictionary<string, List<string>> mine = String.IsNullOrEmpty(viewerUserId)
                ? new Dictionary<string, List<string>>(StringComparer.Ordinal)
                : await _Services.Database.Reactions.MineAsync(targetType, targetIds, viewerUserId, token).ConfigureAwait(false);
            Dictionary<string, ReactionState> result = new Dictionary<string, ReactionState>(StringComparer.Ordinal);
            foreach (string id in targetIds.Distinct(StringComparer.Ordinal))
            {
                ReactionState state = new ReactionState();
                if (counts.TryGetValue(id, out Dictionary<string, long>? c))
                {
                    foreach (PropertyReaction reaction in _Services.Property.Property.Reactions ?? new List<PropertyReaction>())
                    {
                        if (c.TryGetValue(reaction.Key, out long n) && n > 0) state.ReactionCounts[reaction.Key] = n;
                    }
                }
                state.ReactionTotal = state.ReactionCounts.Values.Sum();
                if (mine.TryGetValue(id, out List<string>? m)) state.MyReactions = m.Where(keys.Contains).ToList();
                result[id] = state;
            }
            return result;
        }

        /// <summary>
        /// PUT .../reactions/{key}: add the caller's reaction. Idempotent; notifies the content's author (aggregated)
        /// when the row is new and the author is someone else.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="targetType">FeedPost or Comment.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="key">Reaction key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The target's reaction fields.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 UnknownReaction, 403 Forbidden (admin token), 403 Muted, 403 InteractionBlocked, 404 NotFound, 429 RateLimited.</exception>
        public Task<ReactionState> AddAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, string key, CancellationToken token = default)
        {
            return ChangeAsync(actor, targetType, targetId, key, true, token);
        }

        /// <summary>
        /// DELETE .../reactions/{key}: remove the caller's reaction. Idempotent; nothing is retracted.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="targetType">FeedPost or Comment.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="key">Reaction key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The target's reaction fields.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 UnknownReaction, 403 Forbidden (admin token), 403 Muted, 403 InteractionBlocked, 404 NotFound, 429 RateLimited.</exception>
        public Task<ReactionState> RemoveAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, string key, CancellationToken token = default)
        {
            return ChangeAsync(actor, targetType, targetId, key, false, token);
        }

        /// <summary>
        /// GET .../reactions: who reacted, newest first (ContentCollections.Reactions; filter key; default 50, max 100).
        /// Members the caller blocked are omitted, and so are rows of keys no longer in the property's set.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="targetType">FeedPost or Comment.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 400 UnknownReaction, 400 InvalidContinuationToken, 404 NotFound, 429 RateLimited.</exception>
        public async Task<EnumerationResult<ReactionListItem>> ListAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Media.EnforceRead(actor);
            EnumerationQuery q = query ?? new EnumerationQuery();
            if (q.MaxResults == null) q.MaxResults = 50;
            string? key = q.FilterString("key");
            HashSet<string> keys = CurrentKeys();
            if (key != null && !keys.Contains(key)) throw UnknownReaction(key);
            await RequireTargetAsync(actor, targetType, targetId, false, token).ConfigureAwait(false);
            bool staff = actor.IsStaffFor(TargetTypeEnum.Reaction);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, ContentCollections.Reactions, staff, "reactions|" + targetType + "|" + targetId, 100);
            HashSet<string> blocked = String.IsNullOrEmpty(actor.UserId) ? new HashSet<string>(StringComparer.Ordinal) : await _Services.Blocks.BlockedByAsync(actor.UserId, token).ConfigureAwait(false);
            EnumerationResult<Reaction> page = await _Services.Database.Reactions.EnumerateAsync(targetType, targetId, request, key, keys, blocked, token).ConfigureAwait(false);
            Dictionary<string, UserSummary> users = await _Services.Summaries.BuildAsync(page.Objects.Select(r => (string?)r.UserId), token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.Reaction, page.Objects.Select(r => r.Id), staff, token).ConfigureAwait(false);
            return page.With(page.Objects.Select(r => new ReactionListItem
            {
                Id = r.Id,
                User = users.TryGetValue(r.UserId, out UserSummary? u) ? u : UserSummary.Deleted(),
                Key = r.Key,
                CreatedUtc = r.CreatedUtc,
                Labels = metadata.TryGetValue(r.Id, out ObjectMetadata? m) ? m.Labels : new List<string>(),
                Tags = m != null ? m.Tags : new Dictionary<string, string>()
            }).ToList());
        }

        #endregion

        #region Private-Methods

        private async Task<ReactionState> ChangeAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, string key, bool add, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string userId = actor.RequireUserId();
            using (Activity? activity = TrellisKitTelemetry.StartSpan(add ? "reactions.add" : "reactions.remove"))
            {
                actor.RequireNotMuted();
                _Services.Limits.EnforceMember(actor, RateBucket, _Services.Settings.Reactions.PerMinute, RateLimitService.Minute);
                if (!CurrentKeys().Contains(key ?? String.Empty)) throw UnknownReaction(key);
                ReactionTarget target = await RequireTargetAsync(actor, targetType, targetId, true, token).ConfigureAwait(false);
                if (!String.IsNullOrEmpty(target.AuthorUserId) && !String.Equals(target.AuthorUserId, userId, StringComparison.Ordinal) && await _Services.Blocks.HasBlockedAsync(target.AuthorUserId, userId, token).ConfigureAwait(false))
                    throw new ApiErrorException(403, "InteractionBlocked", "You cannot react to this member's content.");
                if (add)
                {
                    bool added = await _Services.Database.Reactions.AddAsync(targetType, targetId, userId, key!, DateTime.UtcNow, token).ConfigureAwait(false);
                    if (added && !String.IsNullOrEmpty(target.AuthorUserId) && !String.Equals(target.AuthorUserId, userId, StringComparison.Ordinal))
                    {
                        await _Services.Notifications.CreateAsync(new NotificationDraft
                        {
                            RecipientUserId = target.AuthorUserId,
                            Type = NotificationTypeEnum.Reaction,
                            ActorUserId = userId,
                            TargetType = targetType,
                            TargetId = targetId,
                            FeedPostId = target.FeedPostId,
                            CommentId = targetType == TargetTypeEnum.Comment ? targetId : null,
                            ThreadId = target.ThreadId,
                            ForumId = target.ForumId,
                            Path = target.Path ?? (target.ThreadId == null ? null : "/threads/" + target.ThreadId + (targetType == TargetTypeEnum.Comment ? "?comment=" + targetId : String.Empty)),
                            Body = target.Excerpt
                        }, token).ConfigureAwait(false);
                    }
                }
                else
                {
                    await _Services.Database.Reactions.RemoveAsync(targetType, targetId, userId, key!, token).ConfigureAwait(false);
                }
                ContentTargetProvider? changed = ProviderOf(targetType);
                if (changed != null) await changed.ReactionsChangedAsync(targetType, targetId, token).ConfigureAwait(false);
                TrellisKitTelemetry.Add(TrellisKitTelemetry.Reactions, 1, "action", add ? "add" : "remove", "target_type", targetType.ToString(), "key", key!);
                activity?.SetStatus(ActivityStatusCode.Ok);
                Dictionary<string, ReactionState> states = await LoadAsync(targetType, new[] { targetId }, userId, token).ConfigureAwait(false);
                return states[targetId];
            }
        }

        private async Task<ReactionTarget> RequireTargetAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, bool forWrite, CancellationToken token)
        {
            DateTime now = DateTime.UtcNow;
            foreach (Func<ContentActor, TargetTypeEnum, string, bool, CancellationToken, Task<ReactionTarget?>> resolver in ExtraTargets)
            {
                ReactionTarget? extra = await resolver(actor, targetType, targetId, forWrite, token).ConfigureAwait(false);
                if (extra != null) return extra;
            }
            if (ForumTargets != null && (targetType == TargetTypeEnum.Thread || targetType == TargetTypeEnum.Comment))
            {
                ReactionTarget? forum = await ForumTargets(actor, targetType, targetId, forWrite, token).ConfigureAwait(false);
                if (forum != null) return forum;
            }
            ContentTargetProvider? provider = ProviderOf(targetType);
            if (provider != null)
            {
                if (String.IsNullOrWhiteSpace(targetId)) throw ApiErrorException.NotFound(targetType.ToString());
                ReactionTarget? own = await provider.ReactionTargetAsync(actor, targetType, targetId.Trim(), forWrite, token).ConfigureAwait(false);
                if (own == null) throw ApiErrorException.NotFound(targetType.ToString());
                own.Path ??= provider.PathOf(targetType, targetId.Trim());
                return own;
            }
            if (targetType == TargetTypeEnum.FeedPost)
            {
                FeedPost post = await _Services.ContentAccess.RequireVisiblePostAsync(actor, targetId, token).ConfigureAwait(false);
                if (post.Kind != FeedPostKindEnum.Original && post.SourceId != null)
                    throw new ApiErrorException(409, "UseCommentTarget", "This card's conversation is " + (post.SourceType ?? "Thread") + " " + post.SourceId + "; use its commentTarget.");
                if (forWrite && post.StateAt(now) != FeedPostStateEnum.Published) throw ApiErrorException.NotFound("Feed post");
                return new ReactionTarget { AuthorUserId = post.AuthorUserId, FeedPostId = post.Id, Excerpt = post.Excerpt };
            }
            if (targetType == TargetTypeEnum.Comment)
            {
                Comment comment = await _Services.ContentAccess.RequireCommentAsync(actor, targetId, token).ConfigureAwait(false);
                if (!ContentAccessService.CanSeeCommentContent(comment, actor)) throw ApiErrorException.NotFound("Comment");
                if (forWrite && comment.ModerationState != ModerationStateEnum.None) throw ApiErrorException.NotFound("Comment");
                if (forWrite && comment.TargetType == TargetTypeEnum.FeedPost)
                {
                    FeedPost? post = await _Services.Database.FeedPosts.ReadAsync(comment.TargetId, token).ConfigureAwait(false);
                    if (post == null || post.StateAt(now) != FeedPostStateEnum.Published) throw ApiErrorException.NotFound("Comment");
                }
                ContentTargetProvider? host = ProviderOf(comment.TargetType);
                if (host != null)
                {
                    // Comments under a provider object: the object must still be readable (and writable rules apply on write).
                    await host.CommentHostAsync(actor, comment.TargetType, comment.TargetId, false, token).ConfigureAwait(false);
                    return new ReactionTarget { AuthorUserId = comment.AuthorUserId, Excerpt = comment.Excerpt, Path = host.PathOf(comment.TargetType, comment.TargetId) + "?comment=" + comment.Id };
                }
                return new ReactionTarget { AuthorUserId = comment.AuthorUserId, FeedPostId = comment.TargetType == TargetTypeEnum.FeedPost ? comment.TargetId : null, Excerpt = comment.Excerpt };
            }
            throw ApiErrorException.NotFound(targetType.ToString());
        }

        private ContentTargetProvider? ProviderOf(TargetTypeEnum targetType)
        {
            if (targetType == TargetTypeEnum.FeedPost || targetType == TargetTypeEnum.Thread || targetType == TargetTypeEnum.Comment) return null;
            return _Services.ContentTargets.Find(targetType);
        }

        private static ApiErrorException UnknownReaction(string? key)
        {
            return new ApiErrorException(400, "UnknownReaction", "\"" + (key ?? String.Empty) + "\" is not one of this community's reactions.");
        }

        #endregion
    }
}
