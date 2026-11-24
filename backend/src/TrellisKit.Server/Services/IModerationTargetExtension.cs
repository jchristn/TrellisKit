namespace TrellisKit.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Moderation of target types added after Phase 2 (Phase 3 forum threads): the moderation service delegates loading,
    /// scope checks, state changes, views, revisions, report counts, and purges of these types to an extension.
    /// </summary>
    public interface IModerationTargetExtension
    {
        /// <summary>
        /// Whether the extension handles a target type.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <returns>True when handled.</returns>
        bool Handles(TargetTypeEnum targetType);

        /// <summary>
        /// Load a target in any state, or null.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Target, or null.</returns>
        Task<ModerationTarget?> LoadAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default);

        /// <summary>
        /// Apply a moderation state with its coupling rules (media, promotions, search).
        /// </summary>
        /// <param name="target">Target.</param>
        /// <param name="state">New state.</param>
        /// <param name="moderatorUserId">Moderator, or null for auto-hide.</param>
        /// <param name="reason">Reason.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task SetStateAsync(ModerationTarget target, ModerationStateEnum state, string? moderatorUserId, string? reason, CancellationToken token = default);

        /// <summary>
        /// The target payload for moderators (any state).
        /// </summary>
        /// <param name="actor">Moderator.</param>
        /// <param name="target">Target.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Payload.</returns>
        Task<object> ViewAsync(ContentActor actor, ModerationTarget target, CancellationToken token = default);

        /// <summary>
        /// A bounded preview of the target's revisions.
        /// </summary>
        /// <param name="target">Target.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Preview.</returns>
        Task<BoundedPreview<object>> RevisionsAsync(ModerationTarget target, CancellationToken token = default);

        /// <summary>
        /// Recompute the target's open report count.
        /// </summary>
        /// <param name="target">Target.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task RecomputeReportsAsync(ModerationTarget target, CancellationToken token = default);

        /// <summary>
        /// Purge a removed or deleted target's content after the retention period.
        /// </summary>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when purged.</returns>
        Task<bool> PurgeAsync(string targetId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// The forum of a comment's conversation when it belongs to a forum thread, or null.
        /// </summary>
        /// <param name="target">A Comment target.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Forum identifier, or null.</returns>
        Task<string?> ForumOfCommentAsync(ModerationTarget target, CancellationToken token = default);

        /// <summary>
        /// The display name of a forum scope, or null.
        /// </summary>
        /// <param name="forumId">Forum identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Name, or null.</returns>
        Task<string?> ForumNameAsync(string forumId, CancellationToken token = default);

        /// <summary>
        /// Moderators (and SystemAdmins) whose scope covers a forum, for overdue notices.
        /// </summary>
        /// <param name="forumId">Forum identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Member identifiers.</returns>
        Task<System.Collections.Generic.List<string>> ModeratorIdsAsync(string forumId, CancellationToken token = default);

        /// <summary>
        /// The report scope type this extension owns (for example Ideas), or null when its targets use the built-in
        /// scopes (forums use Forum through ForumId). Default null.
        /// </summary>
        string? ScopeType
        {
            get { return null; }
        }

        /// <summary>
        /// Scope entries a caller holds in this extension's scope ("Ideas", or "Ideas:Comment" for one scope id), added to
        /// the caller's moderation scopes. Default none.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <returns>Scope entries.</returns>
        System.Collections.Generic.IReadOnlyList<string> ScopesFor(ContentActor actor)
        {
            return System.Array.Empty<string>();
        }

        /// <summary>
        /// Whether a caller may act on a target in this extension's scope, or null to apply the default rule. Default null.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="target">Target.</param>
        /// <param name="authorIsSystemAdmin">Whether the content's author is a SystemAdmin.</param>
        /// <returns>Decision, or null.</returns>
        bool? CanAct(ContentActor actor, ModerationTarget target, bool authorIsSystemAdmin)
        {
            return null;
        }

        /// <summary>
        /// Claim a Comment target whose conversation belongs to this extension (set its ScopeType, ScopeId, and extension
        /// fields). Default: not claimed.
        /// </summary>
        /// <param name="target">A Comment target.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when claimed.</returns>
        Task<bool> ClaimCommentAsync(ModerationTarget target, CancellationToken token = default)
        {
            return Task.FromResult(false);
        }

        /// <summary>
        /// Fill a queue item's state and preview for a target of this extension. Default: nothing.
        /// </summary>
        /// <param name="target">Target.</param>
        /// <param name="item">Queue item.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task DescribeAsync(ModerationTarget target, ModerationQueueItem item, CancellationToken token = default)
        {
            return Task.CompletedTask;
        }
    }
}
