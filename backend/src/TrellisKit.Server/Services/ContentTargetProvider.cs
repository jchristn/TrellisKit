namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Plugs a Phase 4 content type (Venue, Instructor, Event, Series, Lesson) into the shared engagement services:
    /// comments and the one-conversation rule, reactions, reports, moderation actions and the moderation queue,
    /// visibility of comments and media attached to the type, and global search. A module derives from this class,
    /// overrides what its types support, and registers it in <see cref="ApplicationServices.ContentTargets"/>.
    /// Every default answers "not supported", so a provider only overrides the parts it uses. Implementations must be
    /// thread safe (one instance serves every request of a property).
    /// </summary>
    public abstract class ContentTargetProvider
    {
        #region Public-Members

        /// <summary>
        /// The target types this provider serves (for example Event, or Series and Lesson). A type is served by at most
        /// one provider.
        /// </summary>
        public abstract IReadOnlyList<TargetTypeEnum> TargetTypes { get; }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The moderation and report scope of a type: "Directory", "Events", or "Library" (docs/PHASE4_CONTRACT.md
        /// sections 3.10, 4.8, 6.3). Reports on the type and on comments under it are stored with this scope type.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <returns>Scope type.</returns>
        public abstract string ScopeType(TargetTypeEnum targetType);

        /// <summary>
        /// Whether a caller moderates content in a scope returned by <see cref="ScopeType"/> (queue access, actions on
        /// the type, and on comments under it). SystemAdmin always does; staff roles of the scope do.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="scopeType">Scope type.</param>
        /// <returns>True when the caller moderates the scope.</returns>
        public abstract bool Moderates(ContentActor actor, string scopeType);

        /// <summary>
        /// App path of an object, for example /events/evt_x (search results, notifications, moderation queue links).
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <returns>Path.</returns>
        public abstract string PathOf(TargetTypeEnum targetType, string targetId);

        /// <summary>
        /// Whether a caller may read an object (detail visibility, not a locked stub). Used for comments and media
        /// attached to the object, reactions, reports, and search hits.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when readable.</returns>
        public abstract Task<bool> IsVisibleAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, CancellationToken token = default);

        /// <summary>
        /// The comment host of an object that hosts a conversation (Event, Lesson). Throws 404 when the caller cannot
        /// read it; with forWrite, applies the write rules (gating, commentsEnabled 403 CommentsDisabled, canceled
        /// events 409 EventCanceled). The default throws 404 (the type hosts no comments).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="forWrite">Whether the caller is about to comment.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Host.</returns>
        /// <exception cref="ApiErrorException">404 NotFound, 403, or 409 per the rules above.</exception>
        public virtual Task<CommentHost> CommentHostAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, bool forWrite, CancellationToken token = default)
        {
            throw ApiErrorException.NotFound(targetType.ToString());
        }

        /// <summary>
        /// The stored comment count of an object that hosts comments (0 by default).
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        public virtual Task<int> CommentCountAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            return Task.FromResult(0);
        }

        /// <summary>
        /// Called after a comment is created on an object of the type (notify hosts or staff, enqueue search, recompute
        /// counters). The default does nothing.
        /// </summary>
        /// <param name="host">Host.</param>
        /// <param name="comment">New comment.</param>
        /// <param name="parent">Parent comment, or null for a top-level comment.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public virtual Task AfterCommentAsync(CommentHost host, Comment comment, Comment? parent, CancellationToken token = default)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Called after a comment under the type is edited, deleted, hidden, or restored (recompute counters, search).
        /// The default does nothing.
        /// </summary>
        /// <param name="comment">Comment.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public virtual Task CommentChangedAsync(Comment comment, CancellationToken token = default)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// The reaction target of an object (reactions on the object itself, not its comments), or null when the
        /// type takes no reactions. With forWrite, applies the write rules. Throws 404 when the caller cannot read it.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="forWrite">Whether the caller is about to react.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Reaction target or null.</returns>
        public virtual Task<ReactionTarget?> ReactionTargetAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, bool forWrite, CancellationToken token = default)
        {
            return Task.FromResult<ReactionTarget?>(null);
        }

        /// <summary>
        /// Called after the reactions of an object changed (recompute reactiontotal). The default does nothing.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public virtual Task ReactionsChangedAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Load an object as a moderation target without visibility checks (moderation queue, actions, reports), or
        /// null when it does not exist.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Target or null.</returns>
        public virtual Task<ModerationTarget?> LoadModerationTargetAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            return Task.FromResult<ModerationTarget?>(null);
        }

        /// <summary>
        /// Apply a moderation state to an object (Hide, Unhide, Remove, Restore). The default throws 400: the type
        /// cannot be moderated.
        /// </summary>
        /// <param name="target">Target.</param>
        /// <param name="state">New state.</param>
        /// <param name="moderatorUserId">Moderator, or null for the system.</param>
        /// <param name="reason">Reason.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed when the type cannot be moderated.</exception>
        public virtual Task SetModerationStateAsync(ModerationTarget target, ModerationStateEnum state, string? moderatorUserId, string? reason, CancellationToken token = default)
        {
            throw new ApiErrorException(400, "ValidationFailed", "This content type cannot be moderated.");
        }

        /// <summary>
        /// Recompute an object's open report count after reports changed. The default does nothing.
        /// </summary>
        /// <param name="target">Target.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public virtual Task RecomputeReportsAsync(ModerationTarget target, CancellationToken token = default)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Whether reports may auto-hide objects of the type (false for directory entries, so a competitor cannot hide
        /// a listing; docs/PHASE4_CONTRACT.md section 3.10). Default false.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <returns>True when auto-hide applies.</returns>
        public virtual bool AllowsAutoHide(TargetTypeEnum targetType)
        {
            return false;
        }

        /// <summary>
        /// The staff view of an object in the moderation queue (a detail payload). The default returns a small summary.
        /// </summary>
        /// <param name="actor">Moderator.</param>
        /// <param name="target">Target.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View object (serialized as JSON).</returns>
        public virtual Task<object> ModerationViewAsync(ContentActor actor, ModerationTarget target, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(target);
            object view = new Dictionary<string, object?> { { "type", target.TargetType.ToString() }, { "id", target.TargetId }, { "path", PathOf(target.TargetType, target.TargetId) } };
            return Task.FromResult(view);
        }

        /// <summary>
        /// The search document of an object, or null to remove it from the index (deleted, draft, archived). The
        /// document id must be "{targetType}:{targetId}" and carry the object's labels and tags. The default returns null.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Document or null.</returns>
        public virtual Task<SearchDocument?> SearchDocumentAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            return Task.FromResult<SearchDocument?>(null);
        }

        /// <summary>
        /// Every searchable object id changed since a time (search sweeps), at most limit. The default returns none.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="sinceUtc">Changed at or after, or null for all.</param>
        /// <param name="limit">Most ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Identifiers.</returns>
        public virtual Task<List<string>> ChangedIdsAsync(TargetTypeEnum targetType, DateTime? sinceUtc, int limit, CancellationToken token = default)
        {
            return Task.FromResult(new List<string>());
        }

        /// <summary>
        /// Whether a caller moderates comments under objects of a type (hide, unhide, remove, restore comments; their
        /// reports in the queue). Defaults to <see cref="Moderates"/> for the type's scope; the library provider widens it
        /// to forum moderators when library.forumModeratorsModerateComments is on (docs/PHASE4_CONTRACT.md section 6.3).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="targetType">Host type (Event, Lesson).</param>
        /// <returns>True when the caller moderates comments under the type.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        public virtual bool ModeratesComments(ContentActor actor, TargetTypeEnum targetType)
        {
            ArgumentNullException.ThrowIfNull(actor);
            return Moderates(actor, ScopeType(targetType));
        }

        /// <summary>
        /// Whether a report reason applies to objects of a type. Default: the Phase 2 reasons and Inaccurate; not
        /// PermanentlyClosed (the directory provider accepts it for venues).
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="reason">Reason.</param>
        /// <returns>True when accepted.</returns>
        public virtual bool AcceptsReportReason(TargetTypeEnum targetType, ReportReasonEnum reason)
        {
            return reason != ReportReasonEnum.PermanentlyClosed && reason != ReportReasonEnum.Filter;
        }

        /// <summary>
        /// Called after a new report on an object of the type is stored (not for duplicates and not for reports on
        /// comments under it). The directory provider overrides it so a PermanentlyClosed venue report also creates a
        /// ReportClosed submission (docs/PHASE4_CONTRACT.md section 3.10). The default does nothing.
        /// </summary>
        /// <param name="target">Reported object.</param>
        /// <param name="report">Stored report.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public virtual Task ReportedAsync(ModerationTarget target, Report report, CancellationToken token = default)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// The global search result of an object the caller may read (the search service has already checked
        /// <see cref="IsVisibleAsync"/>), or null to drop the hit. Set Title, CreatedUtc, Snippet (use
        /// <see cref="SearchService.Snippet"/> with the terms), and Author when there is a member author; the service
        /// fills Type, Id, Path (when empty), labels, and tags. The default returns Type, Id, and Path only.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="terms">Lower-case search terms.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result or null.</returns>
        public virtual Task<SearchResultItem?> SearchResultAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, IReadOnlyList<string> terms, CancellationToken token = default)
        {
            SearchResultItem item = new SearchResultItem { Type = targetType.ToString(), Id = targetId, Path = PathOf(targetType, targetId) };
            return Task.FromResult<SearchResultItem?>(item);
        }

        /// <summary>
        /// The promotion facts of an object for a caller (POST {prefix}/{id}/promote and card rendering), or null when
        /// the type cannot be promoted. Throws 404 when the caller cannot read it. The promotion service calls it with a
        /// SystemAdmin actor to re-check card coupling. The default returns null.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="targetType">Target type (Lesson, Event).</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Source or null.</returns>
        /// <exception cref="ApiErrorException">404 NotFound when the caller cannot read the object.</exception>
        public virtual Task<PromotionSource?> PromotionSourceAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            return Task.FromResult<PromotionSource?>(null);
        }

        /// <summary>
        /// Called after an object was promoted (feedPostId is the new card) or its promotion ended (null). Store it so
        /// <see cref="PromotionSourceAsync"/> reports it and the object's viewer.canPromote turns off. The default does
        /// nothing.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="feedPostId">Card identifier, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public virtual Task PromotionChangedAsync(TargetTypeEnum targetType, string targetId, string? feedPostId, CancellationToken token = default)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Whether objects of a type can be assistant knowledge sources (Lesson, Event, Series, Venue). Default false.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <returns>True when the provider supplies knowledge for the type.</returns>
        public virtual bool ProvidesKnowledge(TargetTypeEnum targetType)
        {
            return false;
        }

        /// <summary>
        /// The knowledge documents an assistant source selects (labels, labelMatch, and tags of the source; at most
        /// maxItems), read as the system: only live objects, each with its audience. The knowledge index worker embeds
        /// them; retrieval re-checks every hit through <see cref="IsVisibleAsync"/> as the chatting member. The default
        /// returns none.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="source">Assistant knowledge source.</param>
        /// <param name="maxItems">Most documents.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Documents.</returns>
        public virtual Task<List<KnowledgeDocument>> KnowledgeDocumentsAsync(TargetTypeEnum targetType, AssistantKnowledgeSource source, int maxItems, CancellationToken token = default)
        {
            return Task.FromResult(new List<KnowledgeDocument>());
        }

        /// <summary>
        /// Keyword knowledge retrieval: the objects matching one keyword that the caller may read, through the type's
        /// own bounded enumeration run as the caller with the source's labels and tags (docs/AI_CONTRACT.md section 7.2).
        /// The service re-checks <see cref="IsVisibleAsync"/>. The default returns none (vector retrieval still works).
        /// </summary>
        /// <param name="actor">Chatting member (anonymous for visitors).</param>
        /// <param name="targetType">Target type.</param>
        /// <param name="source">Assistant knowledge source.</param>
        /// <param name="keyword">Keyword.</param>
        /// <param name="maxResults">Most documents (at most 25).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Documents.</returns>
        public virtual Task<List<KnowledgeDocument>> KnowledgeSearchAsync(ContentActor actor, TargetTypeEnum targetType, AssistantKnowledgeSource source, string keyword, int maxResults, CancellationToken token = default)
        {
            return Task.FromResult(new List<KnowledgeDocument>());
        }

        /// <summary>
        /// What a member's saved list shows about an object (REST_API.md, Saves): null when the object does not exist
        /// or was deleted; <see cref="FavoriteTargetSummary.Available"/> false (and no title, image, or link) when the
        /// caller cannot open it; otherwise its title, image, and app path. The default uses
        /// <see cref="LoadModerationTargetAsync"/> for existence, <see cref="IsVisibleAsync"/> for access, and
        /// <see cref="SearchResultAsync"/> (no terms) for the title and path; override it to add an image.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Summary, or null when the object does not exist.</returns>
        public virtual async Task<FavoriteTargetSummary?> SaveSummaryAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (String.IsNullOrEmpty(targetId)) return null;
            ModerationTarget? target = await LoadModerationTargetAsync(targetType, targetId, token).ConfigureAwait(false);
            if (target == null || target.Deleted) return null;
            if (!await IsVisibleAsync(actor, targetType, targetId, token).ConfigureAwait(false)) return FavoriteTargetSummary.Unavailable();
            SearchResultItem? item = await SearchResultAsync(actor, targetType, targetId, Array.Empty<string>(), token).ConfigureAwait(false);
            return new FavoriteTargetSummary
            {
                Available = true,
                Title = item?.Title,
                Href = String.IsNullOrEmpty(item?.Path) ? PathOf(targetType, targetId) : item.Path
            };
        }

        #endregion
    }
}
