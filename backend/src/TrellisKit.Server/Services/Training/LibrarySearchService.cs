namespace TrellisKit.Server.Services.Training
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// POST /api/v1/library/search (REST_API.md, Library search): one search across courses (series), lessons, and
    /// techniques (published catalog entries, only while features.catalog is on). Each kind runs through its own member
    /// enumeration (LibraryCollections.Series, LibraryCollections.Lessons, ExerciseCatalogCollections.Entries) with the same
    /// search, so access rules, locked stubs, and continuation are exactly those of the kind's own route. Rate limited by
    /// the shared search limiter (search.perMinute, bucket search.global). Thread safety: safe to share.
    /// </summary>
    public sealed class LibrarySearchService
    {
        #region Public-Members

        /// <summary>
        /// Series kind.
        /// </summary>
        public const string SeriesKind = "Series";

        /// <summary>
        /// Lesson kind.
        /// </summary>
        public const string LessonKind = "Lesson";

        /// <summary>
        /// Catalog entry kind.
        /// </summary>
        public const string CatalogEntryKind = "CatalogEntry";

        /// <summary>
        /// The searchable kinds.
        /// </summary>
        public static readonly IReadOnlyList<string> Kinds = new List<string> { SeriesKind, LessonKind, CatalogEntryKind };

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate for one property.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public LibrarySearchService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run a unified library search.
        /// </summary>
        /// <param name="actor">Caller (anonymous where the library is browsable signed out).</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One bounded page per kind.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotAvailable, 429 RateLimited.</exception>
        public async Task<LibrarySearchResponse> SearchAsync(ContentActor actor, LibrarySearchRequest? request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (!_Services.Property.Property.Features.Library) throw new ApiErrorException(404, "NotAvailable", "The library is unavailable.");
            LibrarySearchRequest body = request ?? new LibrarySearchRequest();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string query = (body.Query ?? String.Empty).Trim();
            if (query.Length < LibrarySearchRequest.MinQueryLength || query.Length > LibrarySearchRequest.MaxQueryLength)
                errors["query"] = "Use " + LibrarySearchRequest.MinQueryLength + " to " + LibrarySearchRequest.MaxQueryLength + " characters.";
            int perKind = body.MaxResultsPerKind ?? LibrarySearchRequest.DefaultResultsPerKind;
            if (perKind < 1 || perKind > LibrarySearchRequest.MaxResultsPerKindLimit) errors["maxResultsPerKind"] = "Use 1 to " + LibrarySearchRequest.MaxResultsPerKindLimit + ".";
            HashSet<string> kinds = new HashSet<string>(StringComparer.Ordinal);
            if (body.Kinds == null || body.Kinds.Count == 0) kinds.UnionWith(Kinds);
            else if (body.Kinds.Count > Kinds.Count) errors["kinds"] = "Use at most " + Kinds.Count + " kinds.";
            else
            {
                foreach (string raw in body.Kinds)
                {
                    string? kind = Kinds.FirstOrDefault(k => String.Equals(k, (raw ?? String.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
                    if (kind == null)
                    {
                        errors["kinds"] = "Use Series, Lesson, or CatalogEntry.";
                        break;
                    }
                    kinds.Add(kind);
                }
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            if (!_Services.Property.Property.Features.Catalog) kinds.Remove(CatalogEntryKind);
            _Services.Limits.EnforceMember(actor, "search.global", _Services.Settings.Search.PerMinute, RateLimitService.Minute);
            LibrarySearchResponse response = new LibrarySearchResponse();
            try
            {
                if (kinds.Contains(SeriesKind)) response.Series = await _Services.Library.Series.ListAsync(actor, Query(query, perKind, body), token).ConfigureAwait(false);
                if (kinds.Contains(LessonKind)) response.Lessons = await _Services.Library.Lessons.ListAsync(actor, Query(query, perKind, body), token).ConfigureAwait(false);
                if (kinds.Contains(CatalogEntryKind)) response.Techniques = await _Services.Library.ExerciseCatalog.ListAsync(actor, Query(query, perKind, body), false, token).ConfigureAwait(false);
                TrainingTelemetry.Count(TrainingTelemetry.Searches, "outcome", "success");
            }
            catch (Exception)
            {
                TrainingTelemetry.Count(TrainingTelemetry.Searches, "outcome", "error");
                throw;
            }
            return response;
        }

        #endregion

        #region Private-Methods

        private static EnumerationQuery Query(string search, int maxResults, LibrarySearchRequest body)
        {
            EnumerationQuery query = new EnumerationQuery { Search = search, MaxResults = maxResults };
            if (body.Labels != null) query.Labels = body.Labels.ToList();
            if (body.Tags != null) query.Tags = new Dictionary<string, string>(body.Tags, StringComparer.Ordinal);
            return query;
        }

        #endregion
    }
}
