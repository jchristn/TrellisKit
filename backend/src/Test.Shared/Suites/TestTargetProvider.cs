namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;

    /// <summary>
    /// A test-only content target provider over in-memory objects, so the engagement plumbing (comments, reactions,
    /// reports, moderation, search, promotion, knowledge) is covered without the Phase 4 feature modules. It records
    /// every hook call in <see cref="Calls"/>.
    /// </summary>
    internal sealed class TestTargetProvider : ContentTargetProvider
    {
        private readonly List<TargetTypeEnum> _Types;
        private readonly string _Scope;

        internal TestTargetProvider(string scope, params TargetTypeEnum[] types)
        {
            _Scope = scope;
            _Types = types.ToList();
        }

        internal ConcurrentDictionary<string, TestTarget> Objects { get; } = new ConcurrentDictionary<string, TestTarget>(StringComparer.Ordinal);

        internal ConcurrentDictionary<string, int> Calls { get; } = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

        internal bool AutoHide { get; set; } = false;

        internal bool ForumModeratorsModerateComments { get; set; } = false;

        internal int Called(string name)
        {
            return Calls.TryGetValue(name, out int n) ? n : 0;
        }

        internal TestTarget Add(TargetTypeEnum type, string title, string authorUserId, Action<TestTarget>? change = null)
        {
            TestTarget target = new TestTarget { Type = type, Id = "tst_" + Guid.NewGuid().ToString("N").Substring(0, 16), Title = title, Text = title + " body text", AuthorUserId = authorUserId };
            change?.Invoke(target);
            Objects[target.Id] = target;
            return target;
        }

        /// <inheritdoc />
        public override IReadOnlyList<TargetTypeEnum> TargetTypes
        {
            get { return _Types; }
        }

        /// <inheritdoc />
        public override string ScopeType(TargetTypeEnum targetType)
        {
            return _Scope;
        }

        /// <inheritdoc />
        public override bool Moderates(ContentActor actor, string scopeType)
        {
            if (actor.IsSystemAdmin) return true;
            switch (scopeType)
            {
                case "Library": return actor.IsLibraryEditor;
                case "Events": return actor.IsEventManager;
                case "Directory": return actor.IsDirectoryModerator;
                default: return false;
            }
        }

        /// <inheritdoc />
        public override bool ModeratesComments(ContentActor actor, TargetTypeEnum targetType)
        {
            return base.ModeratesComments(actor, targetType) || (ForumModeratorsModerateComments && actor.AllForumsModerator);
        }

        /// <inheritdoc />
        public override string PathOf(TargetTypeEnum targetType, string targetId)
        {
            return "/" + targetType.ToString().ToLowerInvariant() + "s/" + targetId;
        }

        /// <inheritdoc />
        public override Task<bool> IsVisibleAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            return Task.FromResult(Visible(actor, targetType, targetId) != null);
        }

        /// <inheritdoc />
        public override Task<CommentHost> CommentHostAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, bool forWrite, CancellationToken token = default)
        {
            TestTarget target = Visible(actor, targetType, targetId) ?? throw ApiErrorException.NotFound(targetType.ToString());
            if (forWrite && !target.CommentsEnabled) throw new ApiErrorException(403, "CommentsDisabled", "Comments are turned off here.");
            return Task.FromResult(new CommentHost { TargetType = targetType, TargetId = target.Id, AuthorUserId = target.AuthorUserId, CommentCount = target.CommentCount });
        }

        /// <inheritdoc />
        public override Task<int> CommentCountAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            return Task.FromResult(Objects.TryGetValue(targetId, out TestTarget? t) ? t.CommentCount : 0);
        }

        /// <inheritdoc />
        public override Task AfterCommentAsync(CommentHost host, Comment comment, Comment? parent, CancellationToken token = default)
        {
            Count("AfterComment");
            if (Objects.TryGetValue(host.TargetId, out TestTarget? t)) t.CommentCount++;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override Task CommentChangedAsync(Comment comment, CancellationToken token = default)
        {
            Count("CommentChanged");
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override Task<ReactionTarget?> ReactionTargetAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, bool forWrite, CancellationToken token = default)
        {
            TestTarget? target = Visible(actor, targetType, targetId);
            if (target == null) throw ApiErrorException.NotFound(targetType.ToString());
            return Task.FromResult<ReactionTarget?>(new ReactionTarget { AuthorUserId = target.AuthorUserId, Excerpt = target.Title });
        }

        /// <inheritdoc />
        public override Task ReactionsChangedAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            Count("ReactionsChanged");
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override Task<ModerationTarget?> LoadModerationTargetAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            if (!Objects.TryGetValue(targetId, out TestTarget? t) || t.Type != targetType) return Task.FromResult<ModerationTarget?>(null);
            return Task.FromResult<ModerationTarget?>(new ModerationTarget
            {
                TargetType = targetType,
                TargetId = t.Id,
                AuthorUserId = t.AuthorUserId,
                State = t.State,
                Deleted = t.Deleted,
                Title = t.Title,
                Excerpt = t.Text,
                Data = t
            });
        }

        /// <inheritdoc />
        public override Task SetModerationStateAsync(ModerationTarget target, ModerationStateEnum state, string? moderatorUserId, string? reason, CancellationToken token = default)
        {
            Count("SetModerationState");
            if (Objects.TryGetValue(target.TargetId, out TestTarget? t)) t.State = state;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override Task RecomputeReportsAsync(ModerationTarget target, CancellationToken token = default)
        {
            Count("RecomputeReports");
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override Task ReportedAsync(ModerationTarget target, Report report, CancellationToken token = default)
        {
            Count("Reported:" + report.Reason);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override bool AcceptsReportReason(TargetTypeEnum targetType, ReportReasonEnum reason)
        {
            if (targetType == TargetTypeEnum.Venue && reason == ReportReasonEnum.PermanentlyClosed) return true;
            return base.AcceptsReportReason(targetType, reason);
        }

        /// <inheritdoc />
        public override bool AllowsAutoHide(TargetTypeEnum targetType)
        {
            return AutoHide;
        }

        /// <inheritdoc />
        public override Task<object> ModerationViewAsync(ContentActor actor, ModerationTarget target, CancellationToken token = default)
        {
            object view = new Dictionary<string, object?> { { "type", target.TargetType.ToString() }, { "id", target.TargetId }, { "title", target.Title }, { "state", target.ModerationState.ToString() } };
            return Task.FromResult(view);
        }

        /// <inheritdoc />
        public override Task<SearchDocument?> SearchDocumentAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            if (!Objects.TryGetValue(targetId, out TestTarget? t) || t.Deleted || t.State != ModerationStateEnum.None) return Task.FromResult<SearchDocument?>(null);
            return Task.FromResult<SearchDocument?>(new SearchDocument { Id = targetType + ":" + targetId, Content = t.Title + " " + t.Title + " " + t.Text });
        }

        /// <inheritdoc />
        public override Task<List<string>> ChangedIdsAsync(TargetTypeEnum targetType, DateTime? sinceUtc, int limit, CancellationToken token = default)
        {
            return Task.FromResult(Objects.Values.Where(t => t.Type == targetType).Select(t => t.Id).Take(limit).ToList());
        }

        /// <inheritdoc />
        public override Task<SearchResultItem?> SearchResultAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, IReadOnlyList<string> terms, CancellationToken token = default)
        {
            if (!Objects.TryGetValue(targetId, out TestTarget? t)) return Task.FromResult<SearchResultItem?>(null);
            SearchResultItem item = new SearchResultItem { Title = t.Title, CreatedUtc = t.CreatedUtc, Snippet = SearchService.Snippet(t.Text, terms, "Body", null) ?? new SearchSnippetView { Field = "Title", Text = t.Title } };
            return Task.FromResult<SearchResultItem?>(item);
        }

        /// <inheritdoc />
        public override Task<PromotionSource?> PromotionSourceAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            TestTarget target = Visible(actor, targetType, targetId) ?? throw ApiErrorException.NotFound(targetType.ToString());
            return Task.FromResult<PromotionSource?>(new PromotionSource
            {
                Title = target.Title,
                Excerpt = target.Text,
                Audience = target.Audience,
                Path = PathOf(targetType, target.Id),
                IsLive = target.State == ModerationStateEnum.None && !target.Deleted,
                PromotedFeedPostId = target.PromotedFeedPostId,
                NotifyMembersDefault = targetType == TargetTypeEnum.Lesson,
                CommentsOpen = target.CommentsEnabled
            });
        }

        /// <inheritdoc />
        public override Task PromotionChangedAsync(TargetTypeEnum targetType, string targetId, string? feedPostId, CancellationToken token = default)
        {
            Count("PromotionChanged");
            if (Objects.TryGetValue(targetId, out TestTarget? t)) t.PromotedFeedPostId = feedPostId;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override bool ProvidesKnowledge(TargetTypeEnum targetType)
        {
            return true;
        }

        /// <inheritdoc />
        public override Task<List<KnowledgeDocument>> KnowledgeDocumentsAsync(TargetTypeEnum targetType, AssistantKnowledgeSource source, int maxItems, CancellationToken token = default)
        {
            return Task.FromResult(Objects.Values.Where(t => t.Type == targetType && !t.Deleted && t.State == ModerationStateEnum.None).Take(maxItems)
                .Select(t => new KnowledgeDocument { TargetId = t.Id, Title = t.Title, Text = t.Text, Audience = t.Audience.ToString() }).ToList());
        }

        /// <inheritdoc />
        public override Task<List<KnowledgeDocument>> KnowledgeSearchAsync(ContentActor actor, TargetTypeEnum targetType, AssistantKnowledgeSource source, string keyword, int maxResults, CancellationToken token = default)
        {
            return Task.FromResult(Objects.Values.Where(t => t.Type == targetType && (t.Title + " " + t.Text).Contains(keyword, StringComparison.OrdinalIgnoreCase)).Take(maxResults)
                .Select(t => new KnowledgeDocument { TargetId = t.Id, Title = t.Title, Text = t.Text, Audience = t.Audience.ToString() }).ToList());
        }

        private TestTarget? Visible(ContentActor actor, TargetTypeEnum targetType, string targetId)
        {
            if (!Objects.TryGetValue(targetId ?? String.Empty, out TestTarget? t) || t.Type != targetType || t.Deleted) return null;
            bool staff = Moderates(actor, _Scope);
            if (t.StaffOnly && !staff) return null;
            if (t.State != ModerationStateEnum.None && !staff) return null;
            return t;
        }

        private void Count(string name)
        {
            Calls.AddOrUpdate(name, 1, (k, v) => v + 1);
        }
    }
}
