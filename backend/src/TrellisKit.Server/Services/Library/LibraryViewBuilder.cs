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
    /// Builds the library payloads (docs/PHASE4_CONTRACT.md sections 1.3 and 2.6): series and lesson summaries and
    /// details with levels, covers and video summaries, the caller's progress and follows, labels and tags, locked
    /// stubs, and viewer capabilities. Every batch is loaded with a bounded number of queries.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class LibraryViewBuilder
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
        public LibraryViewBuilder(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Media payloads by media id (deleted media are absent).
        /// </summary>
        /// <param name="ids">Media ids.</param>
        /// <param name="publicPaths">Use the public copy paths when the media was copied there.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Views by id.</returns>
        public async Task<Dictionary<string, MediaView>> MediaAsync(IEnumerable<string?> ids, bool publicPaths, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            List<string> list = ids.Where(i => !String.IsNullOrEmpty(i)).Select(i => i!).Distinct(StringComparer.Ordinal).ToList();
            Dictionary<string, MediaView> result = new Dictionary<string, MediaView>(StringComparer.Ordinal);
            if (list.Count == 0) return result;
            List<MediaAttachment> media = await _Services.Database.Media.ReadManyAsync(list, token).ConfigureAwait(false);
            Dictionary<string, List<MediaVariant>> variants = await _Services.Database.Media.ReadVariantsManyAsync(list, token).ConfigureAwait(false);
            foreach (MediaAttachment m in media)
            {
                if (m.IsDeleted) continue;
                result[m.Id] = _Services.Media.Urls.Build(m, variants.TryGetValue(m.Id, out List<MediaVariant>? v) ? v : null, publicPaths, null);
            }
            return result;
        }

        /// <summary>
        /// Series payloads in input order.
        /// </summary>
        /// <typeparam name="T">SeriesSummaryView or SeriesView.</typeparam>
        /// <param name="series">Series.</param>
        /// <param name="actor">Caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Views.</returns>
        public async Task<List<T>> SeriesAsync<T>(IReadOnlyList<LibrarySeries> series, ContentActor actor, CancellationToken token = default) where T : SeriesSummaryView, new()
        {
            ArgumentNullException.ThrowIfNull(series);
            ArgumentNullException.ThrowIfNull(actor);
            List<T> views = new List<T>();
            if (series.Count == 0) return views;
            List<string> ids = series.Select(s => s.Id).ToList();
            Dictionary<string, LibraryLevelView> levels = await _Services.Library.Config.LevelMapAsync(token).ConfigureAwait(false);
            // Signed-out visitors get the public copies (covers of Public, published series only).
            Dictionary<string, MediaView> covers = await MediaAsync(series.Select(s => s.CoverMediaId), actor.IsAnonymous || String.IsNullOrEmpty(actor.UserId), token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.Series, ids, actor.IsLibraryStaff, token).ConfigureAwait(false);
            Dictionary<string, SeriesProgressSummary> progress = String.IsNullOrEmpty(actor.UserId)
                ? new Dictionary<string, SeriesProgressSummary>(StringComparer.Ordinal)
                : await _Services.Database.Library.Progress.SeriesProgressAsync(actor.UserId, ids, token).ConfigureAwait(false);
            HashSet<string> followed = String.IsNullOrEmpty(actor.UserId)
                ? new HashSet<string>(StringComparer.Ordinal)
                : await _Services.Database.Library.Progress.FollowedAmongAsync(actor.UserId, ids, token).ConfigureAwait(false);
            foreach (LibrarySeries s in series)
            {
                bool readable = LibraryAccess.CanRead(actor, s.Audience);
                T view = new T
                {
                    Id = s.Id,
                    Slug = s.Slug,
                    Title = s.Title,
                    Summary = s.Summary,
                    Cover = s.CoverMediaId != null && covers.TryGetValue(s.CoverMediaId, out MediaView? cover) ? cover : null,
                    Levels = s.Levels.Select(k => LevelOf(levels, k)).ToList(),
                    Status = s.Status,
                    SortOrder = s.SortOrder,
                    LessonCount = s.LessonCount,
                    TotalDurationSeconds = s.TotalDurationSeconds,
                    IsFollowed = followed.Contains(s.Id),
                    Audience = s.Audience,
                    Locked = !readable,
                    Labels = metadata.TryGetValue(s.Id, out ObjectMetadata? m) ? m.Labels : new List<string>(),
                    Tags = m != null ? m.Tags : new Dictionary<string, string>(),
                    PublishedUtc = s.PublishedUtc,
                    ArchivedUtc = s.ArchivedUtc,
                    CreatedUtc = s.CreatedUtc,
                    UpdatedUtc = s.UpdatedUtc
                };
                if (progress.TryGetValue(s.Id, out SeriesProgressSummary? p))
                {
                    view.MyProgress = new SeriesProgressView
                    {
                        CompletedLessons = p.CompletedLessons,
                        Percent = s.LessonCount <= 0 ? 0 : (int)Math.Min(100, Math.Round(100.0 * p.CompletedLessons / s.LessonCount)),
                        LastLessonId = p.LastLessonId,
                        LastWatchedUtc = p.LastWatchedUtc
                    };
                }
                view.Viewer = new SeriesViewer
                {
                    CanFollow = readable && !actor.IsAnonymous && !String.IsNullOrEmpty(actor.UserId),
                    CanEdit = actor.IsLibraryStaff,
                    CanOpen = readable,
                    Requires = readable ? null : LibraryAccess.Requires(actor)
                };
                if (view is SeriesView detail) detail.Description = readable ? s.Description : null;
                views.Add(view);
            }
            return views;
        }

        /// <summary>
        /// Lesson list items in input order.
        /// </summary>
        /// <param name="lessons">Lessons.</param>
        /// <param name="actor">Caller.</param>
        /// <param name="publicPaths">Public sample payloads (public media paths, no caller state).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Views.</returns>
        public async Task<List<LessonSummaryView>> LessonSummariesAsync(IReadOnlyList<Lesson> lessons, ContentActor actor, bool publicPaths, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(lessons);
            ArgumentNullException.ThrowIfNull(actor);
            List<LessonSummaryView> views = new List<LessonSummaryView>();
            if (lessons.Count == 0) return views;
            LessonBatch batch = await BatchAsync(lessons, actor, publicPaths, token).ConfigureAwait(false);
            foreach (Lesson lesson in lessons)
            {
                LessonSummaryView view = new LessonSummaryView();
                Fill(view, lesson, actor, batch);
                if (!view.Locked && lesson.VideoMediaId != null && batch.Media.TryGetValue(lesson.VideoMediaId, out MediaView? video))
                    view.Video = new LessonVideoSummary { DurationSeconds = lesson.DurationSeconds, PosterThumbUrl = video.Video?.PosterThumbUrl, Blurhash = video.Blurhash };
                views.Add(view);
            }
            return views;
        }

        /// <summary>
        /// One lesson detail (locked stubs keep only the summary fields).
        /// </summary>
        /// <param name="lesson">Lesson (with child lists).</param>
        /// <param name="actor">Caller.</param>
        /// <param name="publicPaths">Public sample payload.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        public async Task<LessonView> LessonAsync(Lesson lesson, ContentActor actor, bool publicPaths, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(lesson);
            ArgumentNullException.ThrowIfNull(actor);
            LessonBatch batch = await BatchAsync(new[] { lesson }, actor, publicPaths, token).ConfigureAwait(false);
            LessonView view = new LessonView();
            Fill(view, lesson, actor, batch);
            view.CommentTarget = new CommentTargetRef { Type = TargetTypeEnum.Lesson.ToString(), Id = lesson.Id };
            view.NotifyStaffOnComment = actor.IsLibraryStaff && lesson.NotifyStaffOnComment;
            if (actor.IsLibraryStaff)
            {
                view.ModerationState = lesson.ModerationState;
                if (lesson.VideoMediaId != null && batch.Media.TryGetValue(lesson.VideoMediaId, out MediaView? staffVideo)) view.MediaStatus = staffVideo.Status;
            }
            List<Lesson?> neighbors = await _Services.Database.Library.Catalog.NeighborsAsync(lesson.SeriesId, lesson.Position, token).ConfigureAwait(false);
            view.Previous = Neighbor(neighbors[0], actor);
            view.Next = Neighbor(neighbors[1], actor);
            Dictionary<string, Dictionary<string, long>> counts = await _Services.Database.Reactions.CountsAsync(TargetTypeEnum.Lesson, new[] { lesson.Id }, token).ConfigureAwait(false);
            if (counts.TryGetValue(lesson.Id, out Dictionary<string, long>? byKey)) view.ReactionCounts = byKey.ToDictionary(p => p.Key, p => (int)p.Value, StringComparer.Ordinal);
            if (!publicPaths)
            {
                if (!String.IsNullOrEmpty(actor.UserId))
                {
                    Dictionary<string, List<string>> mine = await _Services.Database.Reactions.MineAsync(TargetTypeEnum.Lesson, new[] { lesson.Id }, actor.UserId, token).ConfigureAwait(false);
                    if (mine.TryGetValue(lesson.Id, out List<string>? keys)) view.MyReactions = keys;
                }
            }
            if (view.Locked) return view;
            view.Description = lesson.Description;
            if (lesson.VideoMediaId != null && batch.Media.TryGetValue(lesson.VideoMediaId, out MediaView? video)) view.Video = video;
            view.Markers = lesson.Markers.OrderBy(m => m.Seconds).Select(m => new LessonMarkerView { Id = m.Id, Seconds = m.Seconds, Label = m.Label }).ToList();
            Dictionary<string, MediaView> docs = await MediaAsync(lesson.Resources.Select(r => r.MediaId).Concat(lesson.Captions.Select(c => (string?)c.MediaId)), publicPaths, token).ConfigureAwait(false);
            view.Resources = lesson.Resources.OrderBy(r => r.Position).Select(r => new LessonResourceView
            {
                Id = r.Id,
                Kind = r.Kind,
                Title = r.Title,
                Media = r.MediaId != null && docs.TryGetValue(r.MediaId, out MediaView? doc) ? doc : null,
                Url = r.Url,
                Text = r.Text
            }).ToList();
            view.Captions = lesson.Captions.OrderBy(c => c.Position).Select(c => new LessonCaptionView
            {
                Language = c.Language,
                Label = c.Label,
                Media = docs.TryGetValue(c.MediaId, out MediaView? track) ? track : null
            }).ToList();
            return view;
        }

        /// <summary>
        /// A progress payload.
        /// </summary>
        /// <param name="progress">Progress.</param>
        /// <param name="durationSeconds">Lesson duration.</param>
        /// <param name="metadata">Labels and tags, or null.</param>
        /// <returns>View.</returns>
        public static LessonProgressView Progress(LessonProgress progress, int durationSeconds, ObjectMetadata? metadata)
        {
            ArgumentNullException.ThrowIfNull(progress);
            int percent = durationSeconds <= 0 ? (progress.Completed ? 100 : 0) : (int)Math.Min(100, Math.Round(100.0 * Math.Min(progress.MaxPositionSeconds, progress.WatchedSeconds) / durationSeconds));
            return new LessonProgressView
            {
                Id = progress.Id,
                LessonId = progress.LessonId,
                PositionSeconds = Math.Round(progress.PositionSeconds, 3),
                MaxPositionSeconds = Math.Round(progress.MaxPositionSeconds, 3),
                WatchedSeconds = Math.Round(progress.WatchedSeconds, 3),
                Percent = percent,
                Completed = progress.Completed,
                CompletedUtc = progress.CompletedUtc,
                LastWatchedUtc = progress.LastWatchedUtc,
                Labels = metadata?.Labels ?? new List<string>(),
                Tags = metadata?.Tags ?? new Dictionary<string, string>()
            };
        }

        #endregion

        #region Private-Methods

        private async Task<LessonBatch> BatchAsync(IReadOnlyList<Lesson> lessons, ContentActor actor, bool publicPaths, CancellationToken token)
        {
            List<string> ids = lessons.Select(l => l.Id).ToList();
            LessonBatch batch = new LessonBatch();
            batch.Levels = await _Services.Library.Config.LevelMapAsync(token).ConfigureAwait(false);
            batch.Series = (await _Services.Database.Library.Catalog.ReadSeriesManyAsync(lessons.Select(l => l.SeriesId), token).ConfigureAwait(false)).ToDictionary(s => s.Id, StringComparer.Ordinal);
            batch.Media = await MediaAsync(lessons.Select(l => l.VideoMediaId), publicPaths || actor.IsAnonymous || String.IsNullOrEmpty(actor.UserId), token).ConfigureAwait(false);
            batch.Metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.Lesson, ids, actor.IsLibraryStaff, token).ConfigureAwait(false);
            if (!publicPaths && !String.IsNullOrEmpty(actor.UserId))
                batch.Progress = await _Services.Database.Library.Progress.ReadManyAsync(actor.UserId, ids, token).ConfigureAwait(false);
            LibraryConfig config = await _Services.Library.Config.CurrentAsync(token).ConfigureAwait(false);
            batch.DownloadsEnabled = _Services.Library.Config.DownloadsEnabled(config);
            batch.PublicPaths = publicPaths;
            return batch;
        }

        private void Fill(LessonBaseView view, Lesson lesson, ContentActor actor, LessonBatch batch)
        {
            bool readable = LibraryAccess.CanRead(actor, lesson.Audience);
            batch.Series.TryGetValue(lesson.SeriesId, out LibrarySeries? series);
            view.Id = lesson.Id;
            view.Series = new SeriesRef { Id = lesson.SeriesId, Slug = series?.Slug ?? String.Empty, Title = series?.Title ?? String.Empty };
            view.Position = lesson.Position;
            view.Slug = lesson.Slug;
            view.Title = lesson.Title;
            view.Summary = lesson.Summary;
            view.Kind = lesson.Kind;
            view.DurationSeconds = lesson.DurationSeconds;
            view.HasResources = lesson.Resources.Count > 0;
            view.Levels = lesson.Levels.Select(k => LevelOf(batch.Levels, k)).ToList();
            view.Status = lesson.Status;
            view.PublishAtUtc = lesson.PublishAtUtc;
            view.Audience = lesson.Audience;
            view.Downloadable = lesson.Downloadable;
            view.CommentsEnabled = lesson.CommentsEnabled;
            view.CommentCount = lesson.CommentCount;
            view.ReactionTotal = lesson.ReactionTotal;
            view.Locked = !readable;
            view.Labels = batch.Metadata.TryGetValue(lesson.Id, out ObjectMetadata? m) ? m.Labels : new List<string>();
            view.Tags = m != null ? m.Tags : new Dictionary<string, string>();
            view.PublishedUtc = lesson.PublishedUtc;
            view.ArchivedUtc = lesson.ArchivedUtc;
            view.CreatedUtc = lesson.CreatedUtc;
            view.UpdatedUtc = lesson.UpdatedUtc;
            if (batch.Progress.TryGetValue(lesson.Id, out LessonProgress? progress) && readable) view.MyProgress = Progress(progress, lesson.DurationSeconds, null);
            if (batch.PublicPaths)
            {
                view.Viewer = new LessonViewer { CanPlay = readable, CanOpen = readable, Requires = readable ? null : "SignIn" };
                return;
            }
            bool participate = readable && LibraryAccess.CanParticipate(actor, lesson.Audience);
            bool videoReady = lesson.Kind == LessonKindEnum.Video && lesson.VideoMediaId != null && batch.Media.TryGetValue(lesson.VideoMediaId, out MediaView? video) && video.Status == MediaStatusEnum.Ready;
            bool signedIn = !actor.IsAnonymous && !String.IsNullOrEmpty(actor.UserId);
            view.Viewer = new LessonViewer
            {
                CanPlay = readable && LibraryAccess.CanPlay(actor, lesson.Audience),
                CanDownload = readable && batch.DownloadsEnabled && lesson.Downloadable && videoReady && LibraryAccess.QualifiesForDownload(actor, lesson.Audience),
                CanComment = participate && lesson.CommentsEnabled && actor.CanWrite,
                CanReact = participate && actor.CanWrite,
                CanReport = readable && signedIn,
                CanEdit = actor.IsLibraryStaff,
                CanPromote = readable && lesson.PromotedFeedPostId == null && LibraryAccess.IsLive(lesson, series) && (actor.IsSystemAdmin || actor.CanPromoteToFeed),
                CanOpen = readable,
                Requires = readable ? null : LibraryAccess.Requires(actor)
            };
        }

        private static LessonNeighbor? Neighbor(Lesson? lesson, ContentActor actor)
        {
            if (lesson == null) return null;
            return new LessonNeighbor { Id = lesson.Id, Title = lesson.Title, CanOpen = LibraryAccess.CanRead(actor, lesson.Audience) };
        }

        private static LibraryLevelView LevelOf(Dictionary<string, LibraryLevelView> levels, string key)
        {
            if (levels.TryGetValue(key, out LibraryLevelView? level)) return new LibraryLevelView { Key = level.Key, Label = level.Label, Color = level.Color, Id = level.Id, SortOrder = level.SortOrder, IsRetired = level.IsRetired };
            return new LibraryLevelView { Key = key, Label = key };
        }

        #endregion
    }
}
