namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Lesson progress (docs/PHASE4_CONTRACT.md section 5.5): anti-skip clamping, monotonic merge, stale and offline
    /// replay handling, completion threshold and sticky completion, the resume tail, Continue watching, the member's
    /// progress list, playback sessions, automatic follows, staff viewing history (resolved decision 17, audited),
    /// and the library metrics (aggregates only).
    /// Thread safety: safe to call concurrently (merges are single atomic statements).
    /// </summary>
    public class LessonProgressService
    {
        #region Public-Members

        /// <summary>
        /// Most items in a metrics preview (topLessons, completionRateBySeries).
        /// </summary>
        public const int MetricsPreviewMax = 20;

        /// <summary>
        /// How far in the future a client time may be before it is treated as now, in seconds. Default 300. Minimum 0,
        /// maximum 3600.
        /// </summary>
        public int ClockSkewSeconds
        {
            get { return _ClockSkewSeconds; }
            set { _ClockSkewSeconds = Math.Clamp(value, 0, 3600); }
        }

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private int _ClockSkewSeconds = 300;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public LessonProgressService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// PUT /api/v1/lessons/{id}/progress.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="lessonId">Lesson.</param>
        /// <param name="request">Body.</param>
        /// <param name="platform">Client platform (web, ios, android) for playback sessions.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored progress.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 401, 402, 404, 429 RateLimited.</exception>
        public async Task<LessonProgressView> UpdateAsync(ContentActor actor, string lessonId, LessonProgressRequest request, string platform, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            string userId = actor.RequireUserId();
            Lesson lesson = await _Services.Library.Lessons.RequirePlayableAsync(actor, lessonId, token).ConfigureAwait(false);
            _Services.Limits.EnforceMember(actor, "library.progress:" + lesson.Id, _Services.Settings.Library.ProgressPerMinute, RateLimitService.Minute);
            _Services.Limits.EnforceMember(actor, "library.progress", _Services.Settings.Library.ProgressPerHour, RateLimitService.Hour);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (request.PositionSeconds == null || Double.IsNaN(request.PositionSeconds.Value) || Double.IsInfinity(request.PositionSeconds.Value) || request.PositionSeconds < 0)
                errors["positionSeconds"] = "Required, 0 or more.";
            if (request.PlayedSeconds != null && (Double.IsNaN(request.PlayedSeconds.Value) || Double.IsInfinity(request.PlayedSeconds.Value) || request.PlayedSeconds < 0))
                errors["playedSeconds"] = "Use 0 or more.";
            string? session = String.IsNullOrWhiteSpace(request.SessionId) ? null : request.SessionId.Trim();
            if (session != null && session.Length > 100) errors["sessionId"] = "At most 100 characters.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);

            DateTime now = DateTime.UtcNow;
            LibraryConfig config = await _Services.Library.Config.CurrentAsync(token).ConfigureAwait(false);
            DateTime? client = request.ClientUpdatedUtc == null ? null : DateTime.SpecifyKind(request.ClientUpdatedUtc.Value.ToUniversalTime(), DateTimeKind.Utc);
            if (client != null && client > now.AddSeconds(_ClockSkewSeconds)) client = now;
            if (client != null && client < now.AddDays(-_Services.Settings.Library.OfflineReplayDays))
            {
                // Offline entries older than library.offlineReplayDays are dropped.
                LibraryTelemetry.Count(LibraryTelemetry.Progress, "outcome", "stale");
                LessonProgress? stored = await _Services.Database.Library.Progress.ReadAsync(userId, lesson.Id, token).ConfigureAwait(false);
                return LibraryViewBuilder.Progress(stored ?? new LessonProgress { Id = String.Empty, LessonId = lesson.Id, UserId = userId, LastWatchedUtc = now }, lesson.DurationSeconds, null);
            }
            double duration = Math.Max(0, lesson.DurationSeconds);
            double position = request.PositionSeconds!.Value;
            bool clamped = false;
            if (duration > 0 && position > duration)
            {
                position = duration;
                clamped = true;
            }
            if (duration <= 0) position = 0;
            double maxPlayed = 2.0 * _Services.Settings.Library.ProgressIntervalSeconds;
            double played = request.PlayedSeconds ?? 0;
            if (played > maxPlayed)
            {
                played = maxPlayed;
                clamped = true;
            }
            int tail = _Services.Settings.Library.ResumeTailSeconds;
            double resume = duration > tail && position >= duration - tail ? 0 : position;
            LessonProgressMerge merge = new LessonProgressMerge
            {
                LessonId = lesson.Id,
                UserId = userId,
                PositionSeconds = resume,
                ReachedSeconds = position,
                PlayedSeconds = played,
                DurationSeconds = duration,
                ThresholdPercent = _Services.Library.Config.CompletionThreshold(config),
                ClientUpdatedUtc = client,
                NowUtc = now
            };
            LessonProgress progress = await _Services.Database.Library.Progress.MergeAsync(merge, token).ConfigureAwait(false);
            bool stale = client != null && progress.ClientUpdatedUtc != null && progress.ClientUpdatedUtc.Value > client.Value.AddMilliseconds(1);
            LibraryTelemetry.Count(LibraryTelemetry.Progress, "outcome", stale ? "stale" : clamped ? "clamped" : "accepted");
            if (progress.Completed && progress.CompletionSource == LessonCompletionSourceEnum.Threshold && progress.CompletedUtc != null && Math.Abs((progress.CompletedUtc.Value - now).TotalSeconds) < 2)
            {
                LibraryTelemetry.Count(LibraryTelemetry.Completions, "source", "threshold");
                await AutoFollowAsync(lesson, userId, config, token).ConfigureAwait(false);
            }
            bool started = await _Services.Database.Library.Progress.RecordPlayAsync(new LessonPlay
            {
                Id = IdGenerator.Generate(LibraryIdPrefixes.Play),
                LessonId = lesson.Id,
                UserId = userId,
                SessionId = session ?? "u:" + userId + ":" + now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture),
                Platform = platform,
                PlayedSeconds = played,
                StartedUtc = now,
                LastUtc = now
            }, token).ConfigureAwait(false);
            if (started) LibraryTelemetry.Count(LibraryTelemetry.Plays, "platform", platform, "kind", lesson.Kind == LessonKindEnum.Video ? "video" : "article");
            ObjectMetadata metadata = await _Services.Metadata.LoadOneAsync(TargetTypeEnum.LessonProgress, progress.Id, false, token).ConfigureAwait(false);
            return LibraryViewBuilder.Progress(progress, lesson.DurationSeconds, metadata);
        }

        /// <summary>
        /// POST /api/v1/lessons/{id}/complete and .../uncomplete.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="lessonId">Lesson.</param>
        /// <param name="completed">Complete (true) or uncomplete (false).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored progress.</returns>
        /// <exception cref="ApiErrorException">401, 402, 404, 429 RateLimited.</exception>
        public async Task<LessonProgressView> SetCompletedAsync(ContentActor actor, string lessonId, bool completed, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string userId = actor.RequireUserId();
            Lesson lesson = await _Services.Library.Lessons.RequirePlayableAsync(actor, lessonId, token).ConfigureAwait(false);
            _Services.Limits.EnforceMember(actor, "library.progress:" + lesson.Id, _Services.Settings.Library.ProgressPerMinute, RateLimitService.Minute);
            LessonProgress? before = await _Services.Database.Library.Progress.ReadAsync(userId, lesson.Id, token).ConfigureAwait(false);
            LessonProgress progress = await _Services.Database.Library.Progress.SetCompletedAsync(userId, lesson.Id, completed, DateTime.UtcNow, token).ConfigureAwait(false);
            if (completed && (before == null || !before.Completed))
            {
                LibraryTelemetry.Count(LibraryTelemetry.Completions, "source", "manual");
                await AutoFollowAsync(lesson, userId, await _Services.Library.Config.CurrentAsync(token).ConfigureAwait(false), token).ConfigureAwait(false);
            }
            return LibraryViewBuilder.Progress(progress, lesson.DurationSeconds, null);
        }

        /// <summary>
        /// DELETE /api/v1/lessons/{id}/progress.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="lessonId">Lesson.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">401, 402, 404.</exception>
        public async Task ResetAsync(ContentActor actor, string lessonId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string userId = actor.RequireUserId();
            Lesson lesson = await _Services.Library.Lessons.RequireVisibleAsync(actor, lessonId, token).ConfigureAwait(false);
            await _Services.Database.Library.Progress.DeleteAsync(userId, lesson.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/me/library/continue/enumerate: lessons in progress, not completed, still openable, newest first.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query (LastWatchedDescending only; default maxResults 10).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public async Task<EnumerationResult<ContinueWatchingItem>> ContinueAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string userId = actor.RequireUserId();
            EnumerationQuery q = query ?? new EnumerationQuery();
            if (q.MaxResults == null) q.MaxResults = 10;
            EnumerationRequest request = _Services.Enumeration.Prepare(q, LibraryCollections.Continue, false, "continue|" + userId);
            LessonProgressFilter filter = new LessonProgressFilter { UserId = userId, InProgressOnly = true, ArchivedCutoffUtc = DateTime.UtcNow.AddDays(-_Services.Settings.Library.ArchivedPlaybackDays) };
            EnumerationResult<LessonProgress> page = await _Services.Database.Library.Progress.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            List<Lesson> lessons = await _Services.Database.Library.Catalog.ReadLessonsAsync(page.Objects.Select(p => p.LessonId), false, token).ConfigureAwait(false);
            Dictionary<string, Lesson> byId = lessons.Where(l => LibraryAccess.CanRead(actor, l.Audience)).ToDictionary(l => l.Id, StringComparer.Ordinal);
            List<LessonSummaryView> summaries = await _Services.Library.Views.LessonSummariesAsync(byId.Values.ToList(), actor, false, token).ConfigureAwait(false);
            Dictionary<string, LessonSummaryView> views = summaries.ToDictionary(v => v.Id, StringComparer.Ordinal);
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.LessonProgress, page.Objects.Select(p => p.Id), false, token).ConfigureAwait(false);
            List<ContinueWatchingItem> items = new List<ContinueWatchingItem>();
            foreach (LessonProgress p in page.Objects)
            {
                if (!byId.TryGetValue(p.LessonId, out Lesson? lesson) || !views.TryGetValue(p.LessonId, out LessonSummaryView? view)) continue;
                items.Add(new ContinueWatchingItem { Lesson = view, Progress = LibraryViewBuilder.Progress(p, lesson.DurationSeconds, metadata.TryGetValue(p.Id, out ObjectMetadata? m) ? m : null) });
            }
            return page.With(items);
        }

        /// <summary>
        /// POST /api/v1/me/library/progress/enumerate (filters completed, seriesId).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public async Task<EnumerationResult<LessonProgressView>> MineAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string userId = actor.RequireUserId();
            EnumerationQuery q = query ?? new EnumerationQuery();
            LessonProgressFilter filter = ParseFilter(q);
            filter.UserId = userId;
            EnumerationRequest request = _Services.Enumeration.Prepare(q, LibraryCollections.MyProgress, false, "my-progress|" + userId);
            EnumerationResult<LessonProgress> page = await _Services.Database.Library.Progress.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            return page.With(await ProgressViewsAsync(page.Objects, token).ConfigureAwait(false));
        }

        /// <summary>
        /// POST /api/v1/admin/library/members/{userId}/progress/enumerate (library staff; every call is audited as
        /// library.viewingHistory.read with the member).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="userId">Member.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound.</exception>
        public async Task<EnumerationResult<LessonViewingRow>> MemberHistoryAsync(ContentActor actor, RequestInfo info, string userId, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            LibraryAccess.RequireStaff(actor);
            User? user = String.IsNullOrWhiteSpace(userId) ? null : await _Services.Database.Users.ReadAsync(userId.Trim(), token).ConfigureAwait(false);
            if (user == null) throw ApiErrorException.NotFound("User");
            EnumerationQuery q = query ?? new EnumerationQuery();
            LessonProgressFilter filter = ParseFilter(q);
            filter.UserId = user.Id;
            EnumerationRequest request = _Services.Enumeration.Prepare(q, LibraryCollections.MemberProgress, true, "member-progress|" + user.Id);
            EnumerationResult<LessonProgress> page = await _Services.Database.Library.Progress.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "library.viewingHistory.read", TargetTypeEnum.User.ToString(), user.Id, "member viewing history (" + page.Objects.Count + " rows)", token).ConfigureAwait(false);
            LibraryTelemetry.Count(LibraryTelemetry.ViewingHistoryReads, "scope", "member");
            List<Lesson> lessons = await _Services.Database.Library.Catalog.ReadLessonsAsync(page.Objects.Select(p => p.LessonId), false, token).ConfigureAwait(false);
            Dictionary<string, LessonSummaryView> views = (await _Services.Library.Views.LessonSummariesAsync(lessons, actor, false, token).ConfigureAwait(false)).ToDictionary(v => v.Id, StringComparer.Ordinal);
            Dictionary<string, Lesson> byId = lessons.ToDictionary(l => l.Id, StringComparer.Ordinal);
            Dictionary<string, LessonPlayStats> plays = await _Services.Database.Library.Progress.PlayStatsAsync(user.Id, page.Objects.Select(p => p.LessonId), token).ConfigureAwait(false);
            List<LessonViewingRow> rows = new List<LessonViewingRow>();
            foreach (LessonProgress p in page.Objects)
            {
                if (!views.TryGetValue(p.LessonId, out LessonSummaryView? view)) continue;
                plays.TryGetValue(p.LessonId, out LessonPlayStats? stats);
                rows.Add(new LessonViewingRow { Lesson = view, Progress = LibraryViewBuilder.Progress(p, byId[p.LessonId].DurationSeconds, null), Plays = stats?.Plays ?? 0, PlayedSeconds = Math.Round(stats?.PlayedSeconds ?? 0, 1) });
            }
            return page.With(rows);
        }

        /// <summary>
        /// POST /api/v1/admin/lessons/{id}/viewers/enumerate (library staff; audited as library.viewingHistory.read
        /// with the lesson).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="lessonId">Lesson.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound.</exception>
        public async Task<EnumerationResult<LessonViewerRow>> LessonViewersAsync(ContentActor actor, RequestInfo info, string lessonId, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            LibraryAccess.RequireStaff(actor);
            Lesson lesson = await _Services.Library.Lessons.RequireAsync(lessonId, token).ConfigureAwait(false);
            EnumerationQuery q = query ?? new EnumerationQuery();
            LessonProgressFilter filter = ParseFilter(q);
            filter.SeriesId = null;
            filter.LessonId = lesson.Id;
            EnumerationRequest request = _Services.Enumeration.Prepare(q, LibraryCollections.LessonViewers, true, "lesson-viewers|" + lesson.Id);
            EnumerationResult<LessonProgress> page = await _Services.Database.Library.Progress.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "library.viewingHistory.read", TargetTypeEnum.Lesson.ToString(), lesson.Id, "lesson viewers (" + page.Objects.Count + " rows)", token).ConfigureAwait(false);
            LibraryTelemetry.Count(LibraryTelemetry.ViewingHistoryReads, "scope", "lesson");
            Dictionary<string, UserSummary> users = await _Services.Summaries.BuildAsync(page.Objects.Select(p => (string?)p.UserId), token).ConfigureAwait(false);
            Dictionary<string, LessonPlayStats> plays = await _Services.Database.Library.Progress.PlayStatsByUserAsync(lesson.Id, page.Objects.Select(p => p.UserId), token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.LessonProgress, page.Objects.Select(p => p.Id), true, token).ConfigureAwait(false);
            List<LessonViewerRow> rows = page.Objects.Select(p => new LessonViewerRow
            {
                User = users.TryGetValue(p.UserId, out UserSummary? u) ? u : UserSummary.Deleted(),
                Progress = LibraryViewBuilder.Progress(p, lesson.DurationSeconds, null),
                Plays = plays.TryGetValue(p.UserId, out LessonPlayStats? s) ? s.Plays : 0,
                Labels = metadata.TryGetValue(p.Id, out ObjectMetadata? m) ? m.Labels : new List<string>(),
                Tags = m != null ? m.Tags : new Dictionary<string, string>()
            }).ToList();
            return page.With(rows);
        }

        /// <summary>
        /// GET /api/v1/admin/metrics/library?days= (7 to 365; library staff; aggregates only).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="days">Window (default 30).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Metrics.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden.</exception>
        public async Task<LibraryMetrics> MetricsAsync(ContentActor actor, int? days, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            LibraryAccess.RequireStaff(actor);
            int window = days ?? 30;
            if (window < 7 || window > 365) throw ApiErrorException.Field("days", "Must be 7 to 365.");
            DateTime from = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-(window - 1)), DateTimeKind.Utc);
            List<LessonMetricRow> top = await _Services.Database.Library.Progress.TopLessonsAsync(from, MetricsPreviewMax, token).ConfigureAwait(false);
            Dictionary<string, Lesson> lessons = (await _Services.Database.Library.Catalog.ReadLessonsAsync(top.Select(t => t.LessonId), false, token).ConfigureAwait(false)).ToDictionary(l => l.Id, StringComparer.Ordinal);
            List<SeriesCompletionRow> completion = await _Services.Database.Library.Progress.SeriesCompletionAsync(MetricsPreviewMax, token).ConfigureAwait(false);
            Dictionary<string, LibrarySeries> series = (await _Services.Database.Library.Catalog.ReadSeriesManyAsync(completion.Select(c => c.SeriesId), token).ConfigureAwait(false)).ToDictionary(s => s.Id, StringComparer.Ordinal);
            return new LibraryMetrics
            {
                Days = window,
                PlaysByDay = await _Services.Database.Library.Progress.DailyAsync(false, from, window, token).ConfigureAwait(false),
                CompletionsByDay = await _Services.Database.Library.Progress.DailyAsync(true, from, window, token).ConfigureAwait(false),
                ActiveLearners = await _Services.Database.Library.Progress.ActiveLearnersAsync(from, token).ConfigureAwait(false),
                TopLessons = new BoundedPreview<LessonMetricView>
                {
                    Objects = top.Where(t => lessons.ContainsKey(t.LessonId)).Select(t => new LessonMetricView { Id = t.LessonId, Title = lessons[t.LessonId].Title, SeriesId = lessons[t.LessonId].SeriesId, Plays = t.Plays, Completions = t.Completions }).ToList(),
                    TotalRecords = await _Services.Database.Library.Progress.CountPlayedLessonsAsync(from, token).ConfigureAwait(false),
                    Enumerate = Constants.ApiPrefix + "/admin/lessons/enumerate"
                },
                CompletionRateBySeries = new BoundedPreview<SeriesCompletionView>
                {
                    Objects = completion.Where(c => series.ContainsKey(c.SeriesId)).Select(c => new SeriesCompletionView
                    {
                        Id = c.SeriesId,
                        Title = series[c.SeriesId].Title,
                        Learners = c.Learners,
                        CompletionRate = c.ProgressRows == 0 ? 0 : Math.Round((double)c.Completions / c.ProgressRows, 4)
                    }).ToList(),
                    TotalRecords = completion.Count,
                    Enumerate = Constants.ApiPrefix + "/admin/series/enumerate"
                }
            };
        }

        #endregion

        #region Private-Methods

        private async Task AutoFollowAsync(Lesson lesson, string userId, LibraryConfig config, CancellationToken token)
        {
            if (!_Services.Library.Config.AutoFollowOnProgress(config)) return;
            await _Services.Database.Library.Progress.AutoFollowAsync(new SeriesFollow
            {
                Id = IdGenerator.Generate(LibraryIdPrefixes.Follow),
                SeriesId = lesson.SeriesId,
                UserId = userId,
                Source = SeriesFollowSourceEnum.Progress,
                CreatedUtc = DateTime.UtcNow
            }, token).ConfigureAwait(false);
        }

        private async Task<List<LessonProgressView>> ProgressViewsAsync(List<LessonProgress> rows, CancellationToken token)
        {
            Dictionary<string, Lesson> lessons = (await _Services.Database.Library.Catalog.ReadLessonsAsync(rows.Select(p => p.LessonId), false, token).ConfigureAwait(false)).ToDictionary(l => l.Id, StringComparer.Ordinal);
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.LessonProgress, rows.Select(p => p.Id), false, token).ConfigureAwait(false);
            return rows.Select(p => LibraryViewBuilder.Progress(p, lessons.TryGetValue(p.LessonId, out Lesson? l) ? l.DurationSeconds : 0, metadata.TryGetValue(p.Id, out ObjectMetadata? m) ? m : null)).ToList();
        }

        private static LessonProgressFilter ParseFilter(EnumerationQuery q)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            LessonProgressFilter filter = new LessonProgressFilter { Completed = q.FilterBool("completed", errors), SeriesId = q.FilterString("seriesId") };
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            return filter;
        }

        #endregion
    }
}
