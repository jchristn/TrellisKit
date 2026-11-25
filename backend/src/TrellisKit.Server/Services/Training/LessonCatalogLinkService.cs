namespace TrellisKit.Server.Services.Training
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Lesson and catalog links (REST_API.md, Lesson techniques): library staff replace a lesson's links; members read
    /// "Techniques in this lesson" for a lesson they can see and "Lessons that teach this" under their lesson access
    /// (locked stubs for PartiallyGated free accounts); signed-out callers read the Public lessons that teach an entry.
    /// Courses and techniques stay separate data sets; links join them without changing either. Every route answers 404
    /// NotAvailable while features.catalog or features.library is off. Thread safety: safe to share.
    /// </summary>
    public sealed class LessonCatalogLinkService
    {
        #region Public-Members

        /// <summary>
        /// Most links per page.
        /// </summary>
        public const int MaxPageSize = 50;

        /// <summary>
        /// Warning of a link to an entry that is not published (library staff only).
        /// </summary>
        public const string EntryNotPublished = "EntryNotPublished";

        #endregion

        #region Private-Members

        private const string _IdPrefix = "lce_";
        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate for one property.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public LessonCatalogLinkService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// PUT /api/v1/admin/lessons/{id}/catalog-entries: replace a lesson's links (SystemAdmin or LibraryEditor).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="lessonId">Lesson.</param>
        /// <param name="request">Replacement links.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored links in position order (at most 50), staff view.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound or NotAvailable, 409 CatalogEntryNotPublished.</exception>
        public async Task<EnumerationResult<LessonCatalogEntryView>> SetAsync(ContentActor actor, RequestInfo info, string lessonId, LessonCatalogEntriesRequest? request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            Available();
            LibraryAccess.RequireStaff(actor);
            Lesson lesson = await _Services.Library.Lessons.RequireAsync(lessonId, token).ConfigureAwait(false);
            List<LessonCatalogEntryItemRequest?> items = request?.Items?.Cast<LessonCatalogEntryItemRequest?>().ToList() ?? throw ApiErrorException.Field("items", "Required (an empty list removes every link).");
            if (items.Count > LessonCatalogEntry.MaxPerLesson) throw ApiErrorException.Field("items", "Use at most " + LessonCatalogEntry.MaxPerLesson + " links.");
            Dictionary<string, string> errors = new Dictionary<string, string>();
            List<LessonCatalogEntry> links = new List<LessonCatalogEntry>();
            List<int> requested = new List<int>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            DateTime now = DateTime.UtcNow;
            for (int i = 0; i < items.Count; i++)
            {
                LessonCatalogEntryItemRequest? item = items[i];
                string field = "items[" + i + "]";
                if (item == null || String.IsNullOrWhiteSpace(item.CatalogEntryId))
                {
                    errors[field + ".catalogEntryId"] = "Required.";
                    continue;
                }
                if (item.StartSeconds != null && (item.StartSeconds < 0 || item.StartSeconds > LessonCatalogEntry.MaxStartSeconds)) errors[field + ".startSeconds"] = "Use 0 to " + LessonCatalogEntry.MaxStartSeconds + " seconds.";
                if (item.Position != null && (item.Position < 1 || item.Position > LessonCatalogEntry.MaxPerLesson)) errors[field + ".position"] = "Use 1 to " + LessonCatalogEntry.MaxPerLesson + ".";
                List<string> labels = MetadataRules.CleanLabels(item.Labels, field + ".labels", errors);
                Dictionary<string, string> tags = MetadataRules.CleanTags(item.Tags, field + ".tags", errors);
                string entryId = item.CatalogEntryId.Trim();
                if (!seen.Add(entryId))
                {
                    errors[field + ".catalogEntryId"] = "Each catalog entry may be linked once.";
                    continue;
                }
                links.Add(new LessonCatalogEntry
                {
                    Id = IdGenerator.Generate(_IdPrefix),
                    LessonId = lesson.Id,
                    CatalogEntryId = entryId,
                    StartSeconds = item.StartSeconds,
                    Source = item.Source ?? LessonCatalogLinkSourceEnum.Editor,
                    CreatedBy = actor.UserId ?? String.Empty,
                    Labels = labels,
                    Tags = tags,
                    CreatedUtc = now
                });
                requested.Add(item.Position ?? (i + 1));
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            foreach (LessonCatalogEntry link in links)
            {
                CatalogEntry? entry = await _Services.Database.ExerciseCatalog.ReadAsync(link.CatalogEntryId, token).ConfigureAwait(false);
                if (entry == null || entry.DeletedUtc != null) throw ApiErrorException.NotFound("Catalog entry");
                if (!IsPublished(entry)) throw new ApiErrorException(409, "CatalogEntryNotPublished", "Only published catalog entries can be linked to a lesson.");
                link.CatalogEntryId = entry.Id;
            }
            List<LessonCatalogEntry> ordered = links.Select((link, index) => new KeyValuePair<int, LessonCatalogEntry>(index, link))
                .OrderBy(p => requested[p.Key]).ThenBy(p => p.Key).Select(p => p.Value).ToList();
            for (int i = 0; i < ordered.Count; i++) ordered[i].Position = i + 1;
            List<LessonCatalogEntry> stored = await _Services.Database.LessonCatalogLinks.ReplaceAsync(lesson.Id, ordered, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "lesson.catalogEntries", TargetTypeEnum.Lesson.ToString(), lesson.Id, stored.Count + " catalog links", token).ConfigureAwait(false);
            return EnumerationResult<LessonCatalogEntryView>.Of(await EntryViewsAsync(actor, stored, true, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/lessons/{id}/catalog-entries: the catalog entries a lesson teaches, for a lesson the caller can see.
        /// Members see published entries only; library staff also see unpublished ones with a warning.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="lessonIdOrSlug">Lesson identifier or slug.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page (at most 50).</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 404 NotFound or NotAvailable, 429 RateLimited.</exception>
        public async Task<EnumerationResult<LessonCatalogEntryView>> EntriesForLessonAsync(ContentActor actor, string lessonIdOrSlug, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            Available();
            _Services.Media.EnforceRead(actor);
            Lesson lesson = await _Services.Library.Lessons.RequireVisibleAsync(actor, lessonIdOrSlug, token).ConfigureAwait(false);
            bool staff = actor.IsLibraryStaff;
            EnumerationRequest request = _Services.Enumeration.Prepare(query ?? new EnumerationQuery(), TrainingCollections.LessonCatalogEntries, staff, "lesson-catalog|" + lesson.Id, MaxPageSize);
            EnumerationResult<LessonCatalogEntry> page = await _Services.Database.LessonCatalogLinks.EnumerateForLessonAsync(request, lesson.Id, !staff, token).ConfigureAwait(false);
            return page.With(await EntryViewsAsync(actor, page.Objects, staff, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/catalog/entries/{id}/lessons and GET /api/v1/public/catalog/entries/{id}/lessons: the lessons that
        /// teach a published catalog entry. Members see the lessons they can read, plus locked stubs where their access
        /// model shows them; the public variant lists Public lessons only. Archived and unpublished lessons never appear.
        /// </summary>
        /// <param name="actor">Caller (anonymous for the public variant).</param>
        /// <param name="entryIdOrSlug">Catalog entry identifier or slug.</param>
        /// <param name="query">Query.</param>
        /// <param name="publicOnly">Public variant: Public lessons only, with public media paths.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page (at most 50).</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 404 NotFound or NotAvailable, 429 RateLimited.</exception>
        public async Task<EnumerationResult<LessonSummaryView>> LessonsForEntryAsync(ContentActor actor, string entryIdOrSlug, EnumerationQuery? query, bool publicOnly, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            Available();
            CatalogEntryView entry = await _Services.Library.ExerciseCatalog.GetAsync(actor, entryIdOrSlug, false, token).ConfigureAwait(false);
            LessonFilter filter = new LessonFilter { CatalogEntryId = entry.Id, PublicOnly = publicOnly };
            if (publicOnly)
            {
                filter.Audiences = new List<ContentAudienceEnum> { ContentAudienceEnum.Public };
                filter.StubAudiences = new List<ContentAudienceEnum>();
            }
            else
            {
                filter.Audiences = LibraryAccess.Readable(actor);
                filter.StubAudiences = LibraryAccess.Stubs(actor);
            }
            EnumerationRequest request = _Services.Enumeration.Prepare(query ?? new EnumerationQuery(), TrainingCollections.CatalogEntryLessons, false,
                "catalog-lessons|" + entry.Id + (publicOnly ? "|public" : String.Empty), MaxPageSize);
            EnumerationResult<Lesson> page = await _Services.Database.Library.Catalog.EnumerateLessonsAsync(request, filter, token).ConfigureAwait(false);
            return page.With(await _Services.Library.Views.LessonSummariesAsync(page.Objects, actor, publicOnly, token).ConfigureAwait(false));
        }

        #endregion

        #region Private-Methods

        private void Available()
        {
            if (!_Services.Property.Property.Features.Catalog || !_Services.Property.Property.Features.Library)
                throw new ApiErrorException(404, "NotAvailable", "Lesson techniques are not available for this property.");
        }

        private static bool IsPublished(CatalogEntry entry)
        {
            return entry.DeletedUtc == null && entry.Status == "Published" && entry.ModerationState == ModerationStateEnum.None;
        }

        private async Task<List<LessonCatalogEntryView>> EntryViewsAsync(ContentActor actor, IReadOnlyList<LessonCatalogEntry> links, bool staff, CancellationToken token)
        {
            List<LessonCatalogEntryView> views = new List<LessonCatalogEntryView>();
            foreach (LessonCatalogEntry link in links)
            {
                CatalogEntry? entry = await _Services.Database.ExerciseCatalog.ReadAsync(link.CatalogEntryId, token).ConfigureAwait(false);
                if (entry == null || entry.DeletedUtc != null) continue;
                bool published = IsPublished(entry);
                if (!published && !staff) continue;
                CatalogEntryView entryView = await _Services.Library.ExerciseCatalog.ViewForAsync(actor, entry, staff, token).ConfigureAwait(false);
                LessonCatalogEntryView view = JsonSerializer.Deserialize<LessonCatalogEntryView>(JsonSerializer.Serialize(entryView))!;
                view.LinkId = link.Id;
                view.LessonId = link.LessonId;
                view.Position = link.Position;
                view.StartSeconds = link.StartSeconds;
                view.Source = link.Source;
                view.Warning = published ? null : EntryNotPublished;
                view.LinkLabels = staff ? link.Labels.ToList() : link.Labels.Where(l => !MetadataRules.IsStaff(l)).ToList();
                view.LinkTags = link.Tags.Where(t => staff || !MetadataRules.IsStaff(t.Key)).ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal);
                views.Add(view);
            }
            return views;
        }

        #endregion
    }
}
