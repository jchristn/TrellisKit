namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Moderation of ideas and idea comments (docs/PHASE5_CONTRACT.md sections 7.3 and 11): reports are scoped Ideas
    /// (scope id Idea for ideas, Comment for idea comments) and handled by SystemAdmin, SupportAgent, and, for comments
    /// when ideas.forumModeratorsModerate, forum moderators with resource id *. Content by a SystemAdmin is protected.
    /// </summary>
    public class IdeaModerationTarget : IModerationTargetExtension
    {
        #region Public-Members

        /// <summary>
        /// Scope id of reports on ideas.
        /// </summary>
        public const string IdeaScopeId = "Idea";

        /// <summary>
        /// Scope id of reports on idea comments.
        /// </summary>
        public const string CommentScopeId = "Comment";

        /// <inheritdoc />
        public string? ScopeType
        {
            get { return ModerationService.IdeasScopeType; }
        }

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the extension.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public IdeaModerationTarget(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public bool Handles(TargetTypeEnum targetType)
        {
            return targetType == TargetTypeEnum.Idea;
        }

        /// <inheritdoc />
        public IReadOnlyList<string> ScopesFor(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (actor.IsSupportAgent) return new[] { ModerationService.IdeasScopeType };
            if (actor.AllForumsModerator && _Services.Settings.Ideas.ForumModeratorsModerate) return new[] { ModerationService.IdeasScopeType + ":" + CommentScopeId };
            return Array.Empty<string>();
        }

        /// <inheritdoc />
        public bool? CanAct(ContentActor actor, ModerationTarget target, bool authorIsSystemAdmin)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(target);
            if (actor.IsSystemAdmin) return true;
            if (authorIsSystemAdmin) return false;
            if (actor.IsSupportAgent) return true;
            if (target.TargetType == TargetTypeEnum.Comment && actor.AllForumsModerator && _Services.Settings.Ideas.ForumModeratorsModerate) return true;
            return false;
        }

        /// <inheritdoc />
        public Task<bool> ClaimCommentAsync(ModerationTarget target, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(target);
            if (target.Comment == null || target.Comment.TargetType != TargetTypeEnum.Idea) return Task.FromResult(false);
            Claim(target, target.Comment);
            return Task.FromResult(true);
        }

        /// <summary>
        /// Mark a Comment target as an idea comment (scope Ideas, scope id Comment, idea path).
        /// </summary>
        /// <param name="target">Target.</param>
        /// <param name="comment">Comment on an idea.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static void Claim(ModerationTarget target, Comment comment)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(comment);
            target.ScopeType = ModerationService.IdeasScopeType;
            target.ScopeId = CommentScopeId;
            target.ForumId = null;
            target.ExtensionPath = IdeaNotificationService.IdeaPath(comment.TargetId, comment.Id);
        }

        /// <summary>
        /// The moderation target of an idea.
        /// </summary>
        /// <param name="idea">Idea.</param>
        /// <returns>Target.</returns>
        /// <exception cref="ArgumentNullException">The idea is null.</exception>
        public static ModerationTarget TargetOf(Idea idea)
        {
            ArgumentNullException.ThrowIfNull(idea);
            return new ModerationTarget
            {
                TargetType = TargetTypeEnum.Idea,
                TargetId = idea.Id,
                AuthorUserId = idea.AuthorUserId ?? String.Empty,
                ScopeType = ModerationService.IdeasScopeType,
                ScopeId = IdeaScopeId,
                ExtensionState = idea.ModerationState,
                ExtensionDeleted = idea.DeletedUtc != null,
                Purged = idea.PurgedUtc != null,
                ExtensionPath = IdeaNotificationService.IdeaPath(idea.Id, null),
                ExtensionNoun = "idea",
                ExtensionExcerpt = idea.Title
            };
        }

        /// <inheritdoc />
        public async Task<ModerationTarget?> LoadAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            if (targetType != TargetTypeEnum.Idea || String.IsNullOrWhiteSpace(targetId)) return null;
            Idea? idea = await _Services.Database.Ideas.ReadAsync(targetId.Trim(), token).ConfigureAwait(false);
            return idea == null ? null : TargetOf(idea);
        }

        /// <inheritdoc />
        public async Task SetStateAsync(ModerationTarget target, ModerationStateEnum state, string? moderatorUserId, string? reason, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(target);
            await _Services.Database.Ideas.SetModerationStateAsync(target.TargetId, state, moderatorUserId, reason, DateTime.UtcNow, token).ConfigureAwait(false);
            target.ExtensionState = state;
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Idea, target.TargetId, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<object> ViewAsync(ContentActor actor, ModerationTarget target, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(target);
            Idea? idea = await _Services.Database.Ideas.ReadAsync(target.TargetId, token).ConfigureAwait(false);
            if (idea == null) return new IdeaView { Id = target.TargetId };
            ContentActor staff = new ContentActor { UserId = actor.UserId, IsSystemAdmin = true, IsStaff = true, Access = actor.Access };
            return await _Services.Ideas.Ideas.ViewAsync(idea, staff, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task<BoundedPreview<object>> RevisionsAsync(ModerationTarget target, CancellationToken token = default)
        {
            return Task.FromResult(new BoundedPreview<object>());
        }

        /// <inheritdoc />
        public Task RecomputeReportsAsync(ModerationTarget target, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(target);
            return _Services.Database.Ideas.RecomputeOpenReportCountAsync(target.TargetId, token);
        }

        /// <inheritdoc />
        public Task<bool> PurgeAsync(string targetId, DateTime nowUtc, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(targetId)) return Task.FromResult(false);
            return _Services.Database.Ideas.PurgeAsync(targetId.Trim(), nowUtc, token);
        }

        /// <inheritdoc />
        public Task<string?> ForumOfCommentAsync(ModerationTarget target, CancellationToken token = default)
        {
            return Task.FromResult<string?>(null);
        }

        /// <inheritdoc />
        public Task<string?> ForumNameAsync(string forumId, CancellationToken token = default)
        {
            return Task.FromResult<string?>(null);
        }

        /// <inheritdoc />
        public async Task<List<string>> ModeratorIdsAsync(string forumId, CancellationToken token = default)
        {
            List<RoleAssignment> roles = new List<RoleAssignment>();
            roles.AddRange(await _Services.Database.RoleAssignments.EnumerateByRoleAsync(RoleEnum.SystemAdmin, 500, token).ConfigureAwait(false));
            roles.AddRange(await _Services.Database.RoleAssignments.EnumerateByRoleAsync(RoleEnum.SupportAgent, 500, token).ConfigureAwait(false));
            if (String.Equals(forumId, CommentScopeId, StringComparison.Ordinal) && _Services.Settings.Ideas.ForumModeratorsModerate)
                roles.AddRange((await _Services.Database.RoleAssignments.EnumerateByRoleAsync(RoleEnum.Moderator, 500, token).ConfigureAwait(false))
                    .Where(r => r.ResourceType == ResourceTypeEnum.Forum && r.ResourceId == "*"));
            List<string> candidates = roles.Select(r => r.UserId).Distinct(StringComparer.Ordinal).ToList();
            List<User> users = await _Services.Database.Users.ReadManyAsync(candidates, token).ConfigureAwait(false);
            return users.Where(u => !u.IsDeleted && !u.IsBanned).Select(u => u.Id).OrderBy(i => i, StringComparer.Ordinal).ToList();
        }

        /// <inheritdoc />
        public async Task DescribeAsync(ModerationTarget target, ModerationQueueItem item, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(item);
            Idea? idea = await _Services.Database.Ideas.ReadAsync(target.TargetId, token).ConfigureAwait(false);
            if (idea == null) return;
            item.TargetState = idea.DeletedUtc != null ? "Deleted" : idea.ModerationState == ModerationStateEnum.None ? "Visible" : idea.ModerationState.ToString();
            item.Preview = new ModerationPreview { Title = idea.Title, Excerpt = MarkdownNormalizer.Excerpt(idea.Body), HasMedia = false };
            if (item.Scope != null) item.Scope.Name = "Ideas board";
        }

        #endregion
    }
}
