namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// The library's content target provider (Series and Lesson; docs/PHASE4_CONTRACT.md sections 6 and 7): lessons
    /// host comments and take reactions under the gating of section 0.4 and commentsEnabled, reports and moderation
    /// resolve to the Library scope (library staff, and forum Moderator holders with resourceId * when
    /// library.forumModeratorsModerateComments is on), and both types provide search documents (lessons include their
    /// caption and PDF text).
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class LibraryContentProvider : ContentTargetProvider
    {
        #region Public-Members

        /// <summary>
        /// The moderation and report scope of library content.
        /// </summary>
        public const string Scope = "Library";

        /// <inheritdoc />
        public override IReadOnlyList<TargetTypeEnum> TargetTypes
        {
            get { return _Types; }
        }

        #endregion

        #region Private-Members

        private static readonly IReadOnlyList<TargetTypeEnum> _Types = new[] { TargetTypeEnum.Series, TargetTypeEnum.Lesson };
        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public LibraryContentProvider(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override string ScopeType(TargetTypeEnum targetType)
        {
            return Scope;
        }

        /// <inheritdoc />
        public override bool Moderates(ContentActor actor, string scopeType)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (!String.Equals(scopeType, Scope, StringComparison.OrdinalIgnoreCase)) return false;
            return actor.IsLibraryStaff || (_Services.Settings.Library.ForumModeratorsModerateComments && actor.AllForumsModerator);
        }

        /// <inheritdoc />
        public override string PathOf(TargetTypeEnum targetType, string targetId)
        {
            return (targetType == TargetTypeEnum.Series ? "/series/" : "/lessons/") + targetId;
        }

        /// <inheritdoc />
        public override async Task<bool> IsVisibleAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            try
            {
                if (targetType == TargetTypeEnum.Series)
                {
                    LibrarySeries series = await _Services.Library.Series.RequireVisibleAsync(actor, targetId, token).ConfigureAwait(false);
                    return LibraryAccess.CanRead(actor, series.Audience);
                }
                Lesson lesson = await _Services.Library.Lessons.RequireVisibleAsync(actor, targetId, token).ConfigureAwait(false);
                return LibraryAccess.CanRead(actor, lesson.Audience);
            }
            catch (ApiErrorException)
            {
                return false;
            }
        }

        /// <inheritdoc />
        public override async Task<CommentHost> CommentHostAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, bool forWrite, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (targetType != TargetTypeEnum.Lesson) throw ApiErrorException.NotFound(targetType.ToString());
            Lesson lesson = await RequireReadableAsync(actor, targetId, forWrite, token).ConfigureAwait(false);
            if (forWrite && !lesson.CommentsEnabled) throw new ApiErrorException(403, "CommentsDisabled", "Comments are turned off for this lesson.");
            return new CommentHost
            {
                TargetType = TargetTypeEnum.Lesson,
                TargetId = lesson.Id,
                AuthorUserId = String.Empty,
                CommentCount = lesson.CommentCount,
                AllowMedia = _Services.Settings.Library.CommentMedia,
                AllowVideo = _Services.Settings.Library.CommentMedia,
                ModeratorInScope = Moderates(actor, Scope)
            };
        }

        /// <inheritdoc />
        public override async Task<int> CommentCountAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            if (targetType != TargetTypeEnum.Lesson) return 0;
            Lesson? lesson = await _Services.Database.Library.Catalog.ReadLessonAsync(targetId, token).ConfigureAwait(false);
            return lesson?.CommentCount ?? 0;
        }

        /// <inheritdoc />
        public override async Task AfterCommentAsync(CommentHost host, Comment comment, Comment? parent, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(host);
            ArgumentNullException.ThrowIfNull(comment);
            if (host.TargetType != TargetTypeEnum.Lesson) return;
            await _Services.Database.Library.Catalog.RecomputeEngagementAsync(host.TargetId, token).ConfigureAwait(false);
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Comment, comment.Id, token).ConfigureAwait(false);
            Lesson? lesson = await _Services.Database.Library.Catalog.ReadLessonAsync(host.TargetId, token).ConfigureAwait(false);
            if (lesson != null) await _Services.Library.Notifications.CommentToStaffAsync(lesson, comment, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async Task CommentChangedAsync(Comment comment, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(comment);
            if (comment.TargetType != TargetTypeEnum.Lesson) return;
            await _Services.Database.Library.Catalog.RecomputeEngagementAsync(comment.TargetId, token).ConfigureAwait(false);
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Comment, comment.Id, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async Task<ReactionTarget?> ReactionTargetAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, bool forWrite, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (targetType != TargetTypeEnum.Lesson) return null;
            Lesson lesson = await RequireReadableAsync(actor, targetId, forWrite, token).ConfigureAwait(false);
            return new ReactionTarget { AuthorUserId = String.Empty, Excerpt = lesson.Title };
        }

        /// <inheritdoc />
        public override async Task ReactionsChangedAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            if (targetType == TargetTypeEnum.Lesson) await _Services.Database.Library.Catalog.RecomputeEngagementAsync(targetId, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async Task<ModerationTarget?> LoadModerationTargetAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            if (targetType == TargetTypeEnum.Lesson)
            {
                Lesson? lesson = await _Services.Database.Library.Catalog.ReadLessonAsync(targetId, token).ConfigureAwait(false);
                return lesson == null ? null : new ModerationTarget { TargetType = TargetTypeEnum.Lesson, TargetId = lesson.Id, AuthorUserId = lesson.CreatedByUserId ?? String.Empty, State = lesson.ModerationState, Deleted = lesson.DeletedUtc != null };
            }
            LibrarySeries? series = await _Services.Database.Library.Catalog.ReadSeriesAsync(targetId, token).ConfigureAwait(false);
            return series == null ? null : new ModerationTarget { TargetType = TargetTypeEnum.Series, TargetId = series.Id, AuthorUserId = series.CreatedByUserId ?? String.Empty };
        }

        /// <inheritdoc />
        public override async Task SetModerationStateAsync(ModerationTarget target, ModerationStateEnum state, string? moderatorUserId, string? reason, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(target);
            if (target.TargetType != TargetTypeEnum.Lesson) throw new ApiErrorException(400, "ValidationFailed", "Series are not moderated; archive the series instead.");
            await _Services.Database.Library.Catalog.SetLessonModerationAsync(target.TargetId, state, moderatorUserId, reason, DateTime.UtcNow, token).ConfigureAwait(false);
            if (state != ModerationStateEnum.None) await _Services.Media.UnpublishTargetAsync(TargetTypeEnum.Lesson, target.TargetId, token).ConfigureAwait(false);
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Lesson, target.TargetId, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async Task RecomputeReportsAsync(ModerationTarget target, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(target);
            if (target.TargetType == TargetTypeEnum.Lesson) await _Services.Database.Library.Catalog.RecomputeReportsAsync(target.TargetId, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async Task<object> ModerationViewAsync(ContentActor actor, ModerationTarget target, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(target);
            if (target.TargetType == TargetTypeEnum.Lesson)
            {
                Lesson? lesson = await _Services.Database.Library.Catalog.ReadLessonAsync(target.TargetId, token).ConfigureAwait(false);
                if (lesson != null) return (await _Services.Library.Views.LessonSummariesAsync(new[] { lesson }, actor, false, token).ConfigureAwait(false))[0];
            }
            return await base.ModerationViewAsync(actor, target, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async Task<SearchDocument?> SearchDocumentAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            if (targetType == TargetTypeEnum.Series)
            {
                LibrarySeries? series = await _Services.Database.Library.Catalog.ReadSeriesAsync(targetId, token).ConfigureAwait(false);
                if (series == null || series.Status != LibraryStatusEnum.Published) return null;
                return await DocumentAsync(TargetTypeEnum.Series, series.Id, series.Audience, String.Join("\n", new[] { series.Title, series.Title, series.Summary ?? String.Empty, series.Description ?? String.Empty }), null, token).ConfigureAwait(false);
            }
            Lesson? lesson = await _Services.Database.Library.Catalog.ReadLessonAsync(targetId, token).ConfigureAwait(false);
            if (lesson == null) return null;
            LibrarySeries? parent = await _Services.Database.Library.Catalog.ReadSeriesAsync(lesson.SeriesId, token).ConfigureAwait(false);
            if (!LibraryAccess.IsLive(lesson, parent)) return null;
            List<string> parts = new List<string> { lesson.Title, lesson.Title, lesson.Summary ?? String.Empty, lesson.Description ?? String.Empty, parent!.Title };
            parts.AddRange(lesson.Markers.Select(m => m.Label));
            parts.AddRange(lesson.Resources.Select(r => r.Title + (r.Text == null ? String.Empty : " " + r.Text)));
            parts.Add(await _Services.Library.Documents.LessonTextAsync(lesson, token).ConfigureAwait(false));
            return await DocumentAsync(TargetTypeEnum.Lesson, lesson.Id, lesson.Audience, String.Join("\n", parts.Where(p => !String.IsNullOrWhiteSpace(p))), lesson.SeriesId, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override Task<List<string>> ChangedIdsAsync(TargetTypeEnum targetType, DateTime? sinceUtc, int limit, CancellationToken token = default)
        {
            return _Services.Database.Library.Catalog.ChangedIdsAsync(targetType == TargetTypeEnum.Series, sinceUtc, limit, token);
        }

        /// <inheritdoc />
        public override async Task<SearchResultItem?> SearchResultAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, IReadOnlyList<string> terms, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(terms);
            if (targetType == TargetTypeEnum.Series)
            {
                LibrarySeries? series = await _Services.Database.Library.Catalog.ReadSeriesAsync(targetId, token).ConfigureAwait(false);
                if (series == null) return null;
                SearchResultItem item = new SearchResultItem { Type = targetType.ToString(), Id = series.Id, Path = PathOf(targetType, series.Id), Title = series.Title, CreatedUtc = series.PublishedUtc ?? series.CreatedUtc };
                SearchSnippetView? snippet = SearchService.Snippet(series.Summary ?? series.Description ?? series.Title, terms, "summary", null);
                if (snippet != null) item.Snippet = snippet;
                return item;
            }
            Lesson? lesson = await _Services.Database.Library.Catalog.ReadLessonAsync(targetId, token).ConfigureAwait(false);
            if (lesson == null) return null;
            SearchResultItem result = new SearchResultItem { Type = targetType.ToString(), Id = lesson.Id, Path = PathOf(targetType, lesson.Id), Title = lesson.Title, CreatedUtc = lesson.PublishedUtc ?? lesson.CreatedUtc };
            SearchSnippetView? text = SearchService.Snippet(lesson.Summary ?? lesson.Description ?? lesson.Title, terms, "summary", null);
            if (text != null) result.Snippet = text;
            return result;
        }

        /// <inheritdoc />
        public override async Task<PromotionSource?> PromotionSourceAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (targetType != TargetTypeEnum.Lesson) return null;
            Lesson lesson = await _Services.Library.Lessons.RequireVisibleAsync(actor, targetId, token).ConfigureAwait(false);
            if (!LibraryAccess.CanRead(actor, lesson.Audience)) throw ApiErrorException.NotFound("Lesson");
            LibrarySeries? series = await _Services.Database.Library.Catalog.ReadSeriesAsync(lesson.SeriesId, token).ConfigureAwait(false);
            return new PromotionSource
            {
                Title = lesson.Title,
                Excerpt = lesson.Summary ?? (series == null ? String.Empty : series.Title),
                Audience = lesson.Audience,
                Path = PathOf(TargetTypeEnum.Lesson, lesson.Id),
                AuthorUserId = null,
                IsLive = LibraryAccess.IsLive(lesson, series),
                PromotedFeedPostId = lesson.PromotedFeedPostId,
                NotifyMembersDefault = true,
                CommentsOpen = lesson.CommentsEnabled,
                ReactionsOpen = true
            };
        }

        /// <inheritdoc />
        public override async Task PromotionChangedAsync(TargetTypeEnum targetType, string targetId, string? feedPostId, CancellationToken token = default)
        {
            if (targetType == TargetTypeEnum.Lesson) await _Services.Database.Library.Catalog.SetLessonPromotionAsync(targetId, feedPostId, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override bool ProvidesKnowledge(TargetTypeEnum targetType)
        {
            return targetType == TargetTypeEnum.Lesson || targetType == TargetTypeEnum.Series;
        }

        /// <inheritdoc />
        public override async Task<List<KnowledgeDocument>> KnowledgeDocumentsAsync(TargetTypeEnum targetType, TrellisKit.Core.Models.AssistantKnowledgeSource source, int maxItems, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ContentActor system = new ContentActor { IsSystemAdmin = true, Access = _Services.Access.Current };
            return await KnowledgeAsync(system, targetType, source, null, Math.Clamp(maxItems, 1, 5000), true, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async Task<List<KnowledgeDocument>> KnowledgeSearchAsync(ContentActor actor, TargetTypeEnum targetType, TrellisKit.Core.Models.AssistantKnowledgeSource source, string keyword, int maxResults, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(source);
            if (String.IsNullOrWhiteSpace(keyword)) return new List<KnowledgeDocument>();
            return await KnowledgeAsync(actor, targetType, source, keyword.Trim(), Math.Clamp(maxResults, 1, 25), false, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task<List<KnowledgeDocument>> KnowledgeAsync(ContentActor actor, TargetTypeEnum targetType, TrellisKit.Core.Models.AssistantKnowledgeSource source, string? keyword, int max, bool system, CancellationToken token)
        {
            List<KnowledgeDocument> documents = new List<KnowledgeDocument>();
            string? continuation = null;
            while (documents.Count < max)
            {
                token.ThrowIfCancellationRequested();
                TrellisKit.Core.Requests.EnumerationQuery q = new TrellisKit.Core.Requests.EnumerationQuery
                {
                    MaxResults = Math.Min(100, max - documents.Count),
                    ContinuationToken = continuation,
                    Search = keyword,
                    Labels = source.Labels,
                    LabelMatch = source.LabelMatch,
                    Tags = source.Tags
                };
                List<ContentAudienceEnum>? audiences = system ? null : LibraryAccess.Readable(actor);
                if (targetType == TargetTypeEnum.Series)
                {
                    TrellisKit.Core.Enumeration.EnumerationRequest request = _Services.Enumeration.Prepare(q, TrellisKit.Core.Enumeration.LibraryCollections.Series, system, "knowledge-series");
                    TrellisKit.Core.Responses.EnumerationResult<LibrarySeries> page = await _Services.Database.Library.Catalog.EnumerateSeriesAsync(request, new SeriesFilter { Audiences = audiences }, token).ConfigureAwait(false);
                    documents.AddRange(page.Objects.Select(s => new KnowledgeDocument
                    {
                        TargetId = s.Id,
                        Title = s.Title,
                        Text = String.Join("\n", new[] { s.Title, s.Summary ?? String.Empty, s.Description ?? String.Empty }.Where(p => p.Length > 0)),
                        Audience = s.Audience.ToString()
                    }));
                    if (page.EndOfResults || page.ContinuationToken == null) break;
                    continuation = page.ContinuationToken;
                }
                else
                {
                    TrellisKit.Core.Enumeration.EnumerationRequest request = _Services.Enumeration.Prepare(q, TrellisKit.Core.Enumeration.LibraryCollections.Lessons, system, "knowledge-lessons");
                    TrellisKit.Core.Responses.EnumerationResult<Lesson> page = await _Services.Database.Library.Catalog.EnumerateLessonsAsync(request, new LessonFilter { Audiences = audiences }, token).ConfigureAwait(false);
                    List<Lesson> full = await _Services.Database.Library.Catalog.ReadLessonsAsync(page.Objects.Select(l => l.Id), true, token).ConfigureAwait(false);
                    Dictionary<string, LibrarySeries> series = (await _Services.Database.Library.Catalog.ReadSeriesManyAsync(full.Select(l => l.SeriesId), token).ConfigureAwait(false)).ToDictionary(s => s.Id, StringComparer.Ordinal);
                    foreach (Lesson lesson in full)
                    {
                        List<string> parts = new List<string> { lesson.Title, lesson.Summary ?? String.Empty, lesson.Description ?? String.Empty };
                        if (series.TryGetValue(lesson.SeriesId, out LibrarySeries? s)) parts.Add("Series: " + s.Title);
                        parts.AddRange(lesson.Markers.Select(m => "Chapter: " + m.Label));
                        parts.AddRange(lesson.Resources.Select(r => "Resource: " + r.Title + (r.Text == null ? String.Empty : " (" + r.Text + ")")));
                        parts.Add(await _Services.Library.Documents.LessonTextAsync(lesson, token).ConfigureAwait(false));
                        documents.Add(new KnowledgeDocument { TargetId = lesson.Id, Title = lesson.Title, Text = String.Join("\n", parts.Where(p => !String.IsNullOrWhiteSpace(p))), Audience = lesson.Audience.ToString() });
                    }
                    if (page.EndOfResults || page.ContinuationToken == null) break;
                    continuation = page.ContinuationToken;
                }
            }
            return documents.Take(max).ToList();
        }

        private async Task<Lesson> RequireReadableAsync(ContentActor actor, string lessonId, bool forWrite, CancellationToken token)
        {
            Lesson lesson = await _Services.Library.Lessons.RequireVisibleAsync(actor, lessonId, token).ConfigureAwait(false);
            if (!LibraryAccess.CanRead(actor, lesson.Audience)) throw LibraryAccess.Denied(actor, TargetTypeEnum.Lesson, lesson.Id);
            if (forWrite)
            {
                if (!LibraryAccess.CanParticipate(actor, lesson.Audience)) throw LibraryAccess.Denied(actor, TargetTypeEnum.Lesson, lesson.Id);
                LibrarySeries? series = await _Services.Database.Library.Catalog.ReadSeriesAsync(lesson.SeriesId, token).ConfigureAwait(false);
                if (!LibraryAccess.IsLive(lesson, series)) throw ApiErrorException.NotFound("Lesson");
            }
            return lesson;
        }

        private async Task<SearchDocument> DocumentAsync(TargetTypeEnum type, string id, ContentAudienceEnum audience, string content, string? seriesId, CancellationToken token)
        {
            ObjectMetadata metadata = await _Services.Database.Metadata.ReadAsync(type, id, token).ConfigureAwait(false);
            SearchDocument document = new SearchDocument { Id = type + ":" + id, Content = content, Labels = metadata.Labels };
            foreach (KeyValuePair<string, string> tag in metadata.Tags) document.Tags[tag.Key] = tag.Value;
            document.Tags["tk.type"] = type.ToString();
            document.Tags["tk.audience"] = audience.ToString();
            if (seriesId != null) document.Tags["tk.series"] = seriesId;
            return document;
        }

        #endregion
    }
}
