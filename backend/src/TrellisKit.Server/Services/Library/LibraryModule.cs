namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Workers;

    /// <summary>
    /// The Phase 4 services of the library (series, lessons, progress, downloads, document text) for one property (docs/PHASE4_CONTRACT.md section 5)
    /// and their wiring into the Phase 2 and Phase 3 services.
    /// </summary>
    public class LibraryModule
    {
        #region Public-Members

        /// <summary>
        /// Library configuration, levels, settings, and the property decorator.
        /// </summary>
        public LibraryConfigService Config { get; }

        /// <summary>
        /// Payload builders.
        /// </summary>
        public LibraryViewBuilder Views { get; }

        /// <summary>
        /// Series.
        /// </summary>
        public SeriesService Series { get; }

        /// <summary>
        /// Lessons.
        /// </summary>
        public LessonService Lessons { get; }

        /// <summary>
        /// Progress, Continue watching, viewing history, and metrics.
        /// </summary>
        public LessonProgressService Progress { get; }

        /// <summary>
        /// Offline download licenses.
        /// </summary>
        public LessonDownloadService Downloads { get; }

        /// <summary>
        /// Document text for search and assistants.
        /// </summary>
        public DocumentTextService Documents { get; }

        /// <summary>
        /// Notifications, fan-outs, and the library digest.
        /// </summary>
        public LibraryNotificationService Notifications { get; }

        /// <summary>
        /// Content target provider of Series and Lesson.
        /// </summary>
        public LibraryContentProvider Provider { get; }

        /// <summary>
        /// Reviewed exercise and technique catalog (features.catalog, docs/CATALOG_MODALITIES.md).
        /// </summary>
        public ExerciseCatalogService ExerciseCatalog { get; }

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private int _Attached = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the module (call <see cref="Attach"/> once the Phase 2 and Phase 3 services exist).
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public LibraryModule(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            Config = new LibraryConfigService(services);
            Views = new LibraryViewBuilder(services);
            Series = new SeriesService(services);
            Lessons = new LessonService(services);
            Progress = new LessonProgressService(services);
            Downloads = new LessonDownloadService(services);
            Documents = new DocumentTextService(services);
            Notifications = new LibraryNotificationService(services);
            Provider = new LibraryContentProvider(services);
            services.ContentTargets.Register(Provider);
            ExerciseCatalog = new ExerciseCatalogService(services);
            services.ContentTargets.Register(new ExerciseCatalogContentProvider(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Wire the module into the shared services (idempotent).
        /// </summary>
        public void Attach()
        {
            if (Interlocked.Exchange(ref _Attached, 1) != 0) return;
            _Services.Notifications.FanoutHandlers[NotificationTypeEnum.NewLessonInSeries] = Notifications.FanoutAsync;
            _Services.PropertyDecorators.Add(Config.DecorateAsync);
            _Services.Media.AddSettledHandler(Lessons.OnMediaSettledAsync);
            // A series cover processed after the series was published reaches the public paths once it is Ready.
            _Services.Media.AddSettledHandler((media, token) => media.AttachedTargetType == TargetTypeEnum.Series && !String.IsNullOrEmpty(media.AttachedTargetId)
                ? Series.SyncPublicCoverAsync(media.AttachedTargetId!, token)
                : Task.CompletedTask);
            _Services.Media.Pipeline.WantsDownloadRendition = WantsDownloadRenditionAsync;
            LibraryExportSections.Register(_Services);
            _Services.Ai.Tools.Register(new TrellisKit.Server.Services.Ai.AssistantTool
            {
                Name = "searchLibrary",
                Label = "Searching the library",
                CompletedLabel = "Searched the library",
                Description = "Search the library's lessons the member may open. Arguments: query (text), levels (level keys), maxResults (1 to 10). Returns titles, series, durations, and app paths (/lessons/{id}).",
                ParametersSchema = "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"},\"levels\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},\"maxResults\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":10}}}",
                WorksSignedOut = true,
                Handler = SearchLibraryToolAsync
            });
            Func<string, CancellationToken, Task>? previous = _Services.Accounts.AccountClosed;
            _Services.Accounts.AccountClosed = async (userId, token) =>
            {
                await EraseForAccountAsync(userId, token).ConfigureAwait(false);
                if (previous != null) await previous(userId, token).ConfigureAwait(false);
            };
        }

        /// <summary>
        /// Add the module's background workers for this property.
        /// </summary>
        /// <param name="workers">Worker list of the property runtime.</param>
        /// <param name="token">Lifetime token (for signaled passes).</param>
        /// <exception cref="ArgumentNullException">The list is null.</exception>
        public void AddWorkers(List<IntervalWorker> workers, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(workers);
            AppLogger logger = _Services.Logger;
            workers.Add(new LeasedPassWorker(_Services.Database, "library_publish", () => TimeSpan.FromSeconds(_Services.Settings.Library.PublishIntervalSeconds), TimeSpan.FromSeconds(20),
                ct => Lessons.PublishDueAsync(200, ct), logger));
            workers.Add(new LeasedPassWorker(_Services.Database, "library_download_prune", () => TimeSpan.FromHours(1), TimeSpan.FromMinutes(5),
                ct => Downloads.PruneAsync(ct), logger));
            workers.Add(new LeasedPassWorker(_Services.Database, "library_digest", () => TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(3),
                ct => Notifications.DigestAsync(DateTime.UtcNow, 500, ct), logger) { LeaseDuration = TimeSpan.FromMinutes(10) });
            workers.Add(new LeasedPassWorker(_Services.Database, "library_document_text", () => TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30),
                ct => Documents.RunAsync(10, ct), logger) { LeaseDuration = TimeSpan.FromMinutes(10) });
        }

        /// <summary>
        /// Account deletion (section 10): progress, follows, and download licenses are deleted (playback sessions keep
        /// their counts without the member).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows removed.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        public async Task<int> EraseForAccountAsync(string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            int removed = await _Services.Database.Library.Progress.DeleteForUserAsync(userId, token).ConfigureAwait(false);
            removed += await _Services.Database.Library.Downloads.DeleteForUserAsync(userId, token).ConfigureAwait(false);
            return removed;
        }

        #endregion

        #region Private-Methods

        private async Task<TrellisKit.Server.Services.Ai.AssistantToolResult> SearchLibraryToolAsync(TrellisKit.Server.Services.Ai.AssistantCaller caller, System.Text.Json.JsonElement arguments, CancellationToken token)
        {
            TrellisKit.Core.Requests.EnumerationQuery query = new TrellisKit.Core.Requests.EnumerationQuery { MaxResults = 5 };
            if (arguments.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (arguments.TryGetProperty("query", out System.Text.Json.JsonElement q) && q.ValueKind == System.Text.Json.JsonValueKind.String && !String.IsNullOrWhiteSpace(q.GetString()))
                {
                    query.Search = q.GetString()!.Trim();
                    if (query.Search.Length > 200) query.Search = query.Search.Substring(0, 200);
                }
                if (arguments.TryGetProperty("maxResults", out System.Text.Json.JsonElement m) && m.ValueKind == System.Text.Json.JsonValueKind.Number && m.TryGetInt32(out int max)) query.MaxResults = Math.Clamp(max, 1, 10);
                if (arguments.TryGetProperty("levels", out System.Text.Json.JsonElement levels) && levels.ValueKind == System.Text.Json.JsonValueKind.Array)
                    query.Filters["levels"] = levels.Clone();
            }
            TrellisKit.Core.Responses.EnumerationResult<TrellisKit.Core.Responses.LessonSummaryView> page = await Lessons.ListAsync(caller.Actor, query, token).ConfigureAwait(false);
            List<TrellisKit.Core.Responses.LessonSummaryView> open = page.Objects.Where(l => !l.Locked).ToList();
            if (open.Count == 0) return new TrellisKit.Server.Services.Ai.AssistantToolResult { Text = "No lessons found.", ResultCount = 0 };
            List<string> lines = open.Select(l => "- " + l.Title + " (" + l.Series.Title + ", " + (l.DurationSeconds / 60) + " min) /lessons/" + l.Id + (String.IsNullOrEmpty(l.Summary) ? String.Empty : ": " + l.Summary)).ToList();
            return new TrellisKit.Server.Services.Ai.AssistantToolResult
            {
                Text = String.Join("\n", lines),
                ResultCount = open.Count,
                Results = open.Select(l => new TrellisKit.Core.Models.AssistantToolCallResult { TargetType = "Lesson", TargetId = l.Id, Title = l.Title, Path = "/lessons/" + l.Id }).ToList()
            };
        }

        private async Task<bool> WantsDownloadRenditionAsync(MediaAttachment media, CancellationToken token)
        {
            if (media.Kind != MediaKindEnum.Video) return false;
            LibraryConfig config = await Config.CurrentAsync(token).ConfigureAwait(false);
            if (!Config.DownloadsEnabled(config)) return false;
            if (media.AttachedTargetType == TargetTypeEnum.Lesson) return true;
            if ((await _Services.Database.Library.Catalog.LessonIdsUsingMediaAsync(media.Id, token).ConfigureAwait(false)).Count > 0) return true;
            List<RoleAssignment> roles = await _Services.Database.RoleAssignments.EnumerateByUsersAsync(new[] { media.OwnerUserId }, token).ConfigureAwait(false);
            return roles.Any(r => r.Role == RoleEnum.SystemAdmin || r.Role == RoleEnum.LibraryEditor);
        }

        #endregion
    }
}
