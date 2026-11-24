namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Directory administration (docs/PHASE4_CONTRACT.md section 3.9): bulk import (dry run by default; geocoding runs in
    /// the background), CSV export, regeocoding jobs, metrics, and the GeocodeWorker and GeocodeCachePruneWorker passes.
    /// Thread safe.
    /// </summary>
    public class DirectoryAdminService
    {
        #region Public-Members

        /// <summary>
        /// Most import rows per call.
        /// </summary>
        public const int MaxImportRows = 1000;

        /// <summary>
        /// Most import errors listed.
        /// </summary>
        public const int MaxImportErrors = 100;

        /// <summary>
        /// Most venues per regeocode request.
        /// </summary>
        public const int MaxRegeocodeIds = 500;

        /// <summary>
        /// Rows fetched per export page.
        /// </summary>
        public const int ExportPageSize = 500;

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public DirectoryAdminService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// POST /api/v1/admin/directory/import (directory staff): at most 1000 rows; a row whose slug matches a venue updates
        /// it, others create venues. dryRun (default true) validates only. Audited as directory.import.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts and errors.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden.</exception>
        public async Task<DirectoryImportResponse> ImportAsync(ContentActor actor, RequestInfo info, DirectoryImportRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            DirectoryAccess.RequireStaff(actor);
            List<VenueRequest> rows = request.Rows ?? new List<VenueRequest>();
            if (rows.Count == 0) throw ApiErrorException.Field("rows", "Send 1 to " + MaxImportRows + " rows.");
            if (rows.Count > MaxImportRows) throw ApiErrorException.Field("rows", "At most " + MaxImportRows + " rows per import.");
            DirectoryImportResponse response = new DirectoryImportResponse { DryRun = request.DryRun };
            List<DirectoryImportError> errors = new List<DirectoryImportError>();
            List<string> touched = new List<string>();
            HashSet<string> slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rows.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                VenueRequest row = rows[i];
                if (row == null)
                {
                    response.Skipped++;
                    continue;
                }
                try
                {
                    Venue? existing = String.IsNullOrWhiteSpace(row.Slug) ? null : await _Services.Database.DirectoryListings.Venues.ReadBySlugAsync(row.Slug.Trim(), token).ConfigureAwait(false);
                    if (row.Slug != null && !slugs.Add(row.Slug.Trim()))
                    {
                        errors.Add(new DirectoryImportError { Row = i, Field = "slug", Message = "Listed more than once in this import." });
                        response.Skipped++;
                        continue;
                    }
                    Venue venue = await _Services.Directory.Venues.BuildAsync(actor, row, existing, null, actor.UserId, false, token).ConfigureAwait(false);
                    if (row.Labels != null || row.Tags != null) await _Services.Metadata.ValidateAsync(actor, row.Labels, row.Tags, true, token, TargetTypeEnum.Venue).ConfigureAwait(false);
                    if (existing == null) response.Created++;
                    else response.Updated++;
                    if (request.DryRun) continue;
                    Venue? saved = existing == null
                        ? await _Services.Database.DirectoryListings.Venues.CreateAsync(venue, token).ConfigureAwait(false)
                        : await _Services.Database.DirectoryListings.Venues.UpdateAsync(venue, existing.Slug, token).ConfigureAwait(false);
                    if (saved == null) continue;
                    await _Services.Directory.Venues.AfterSaveAsync(actor, saved, row.Labels, row.Tags, token).ConfigureAwait(false);
                    if (saved.GeocodePendingUtc != null) touched.Add(saved.Id);
                }
                catch (ApiErrorException e)
                {
                    if (e.FieldErrors != null && e.FieldErrors.Count > 0)
                        foreach (KeyValuePair<string, string> field in e.FieldErrors) errors.Add(new DirectoryImportError { Row = i, Field = field.Key, Message = field.Value });
                    else errors.Add(new DirectoryImportError { Row = i, Field = String.Empty, Message = e.Message });
                    response.Skipped++;
                }
                catch (DuplicateRecordException)
                {
                    errors.Add(new DirectoryImportError { Row = i, Field = "slug", Message = "In use." });
                    response.Skipped++;
                }
            }
            response.Errors = new BoundedPreview<DirectoryImportError> { Objects = errors.Take(MaxImportErrors).ToList(), TotalRecords = errors.Count };
            if (!request.DryRun)
            {
                await _Services.Audit.WriteAsync(info, "directory.import", "Directory", _Services.PropertyId, "Created " + response.Created + ", updated " + response.Updated + ", skipped " + response.Skipped, token).ConfigureAwait(false);
                if (touched.Count > 0) _Services.Directory.SignalGeocode();
            }
            return response;
        }

        /// <summary>
        /// POST /api/v1/admin/directory/regeocode (directory staff): queue venues for GeocodeWorker (manual pins are
        /// skipped). Answers 202 { queued }.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Venues queued.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 503 IntegrationNotConfigured.</exception>
        public async Task<DirectoryQueuedResponse> RegeocodeAsync(ContentActor actor, RegeocodeRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            DirectoryAccess.RequireStaff(actor);
            if (request.VenueIds != null && request.VenueIds.Count > MaxRegeocodeIds) throw ApiErrorException.Field("venueIds", "At most " + MaxRegeocodeIds + " venues.");
            if (!await _Services.Directory.Geocoding.IsConfiguredAsync(token).ConfigureAwait(false))
                throw new ApiErrorException(503, "IntegrationNotConfigured", "Regeocoding needs a configured geocoder (directory.geocoder.provider).");
            int queued = await _Services.Database.DirectoryListings.Venues.QueueGeocodeAsync(request.VenueIds, request.OnlyMissing, DateTime.UtcNow, token).ConfigureAwait(false);
            if (queued > 0) _Services.Directory.SignalGeocode();
            return new DirectoryQueuedResponse { Queued = queued };
        }

        /// <summary>
        /// GET /api/v1/admin/directory/metrics?days= (directory staff): counts plus the daily series submissionsByDay,
        /// approvalsByDay, and venuesCreatedByDay (one entry per day of the window).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="days">Window in days (7 to 365, default 30), or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Metrics.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed (fieldErrors.days), 403 Forbidden.</exception>
        public async Task<DirectoryMetricsView> MetricsAsync(ContentActor actor, int? days, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            DirectoryAccess.RequireStaff(actor);
            int window = days ?? 30;
            if (window < 7 || window > 365) throw ApiErrorException.Field("days", "Must be 7 to 365.");
            DateTime now = DateTime.UtcNow;
            DateTime from = DateTime.SpecifyKind(now.Date.AddDays(-(window - 1)), DateTimeKind.Utc);
            DirectoryMetricsView metrics = await _Services.Database.DirectoryListings.Venues.MetricsAsync(now, token).ConfigureAwait(false);
            metrics.Days = window;
            metrics.SubmissionsByDay = await _Services.Database.DirectoryListings.Venues.DailyAsync("submissions", from, window, token).ConfigureAwait(false);
            metrics.ApprovalsByDay = await _Services.Database.DirectoryListings.Venues.DailyAsync("approvals", from, window, token).ConfigureAwait(false);
            metrics.VenuesCreatedByDay = await _Services.Database.DirectoryListings.Venues.DailyAsync("venues", from, window, token).ConfigureAwait(false);
            return metrics;
        }

        /// <summary>
        /// GET /api/v1/admin/directory/export.csv (directory staff): every live venue and instructor, written one page at
        /// a time through the callback (the route streams each chunk).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="write">Receives each CSV chunk.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows written.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden.</exception>
        public async Task<int> ExportAsync(ContentActor actor, Func<string, Task> write, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(write);
            DirectoryAccess.RequireStaff(actor);
            int rows = 0;
            await write(new CsvWriter().Row("kind", "id", "slug", "name", "status", "audience", "affiliation", "line1", "line2", "locality", "region", "postalCode", "countryCode", "addressVisibility",
                "lat", "lng", "geoSource", "phone", "email", "websiteUrl", "programs", "visitorPolicy", "isVerified", "rank", "rankLevel", "linkedUserId", "createdUtc", "updatedUtc").ToString()).ConfigureAwait(false);
            string? after = null;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                List<Venue> venues = await _Services.Directory.Venues.ExportPageAsync(after, ExportPageSize, token).ConfigureAwait(false);
                if (venues.Count == 0) break;
                CsvWriter csv = new CsvWriter();
                foreach (Venue v in venues)
                {
                    csv.Row("Venue", v.Id, v.Slug, v.Name, v.Status, v.Audience, v.Affiliation, v.Line1, v.Line2, v.Locality, v.Region, v.PostalCode, v.CountryCode, v.AddressVisibility,
                        v.Lat?.ToString("R", CultureInfo.InvariantCulture), v.Lng?.ToString("R", CultureInfo.InvariantCulture), v.GeoSource, v.Phone, v.Email, v.WebsiteUrl, String.Join(";", v.Programs), v.VisitorPolicy,
                        v.IsVerified ? "true" : "false", null, null, null, v.CreatedUtc.ToString("O", CultureInfo.InvariantCulture), v.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture));
                    rows++;
                }
                await write(csv.ToString()).ConfigureAwait(false);
                after = venues[venues.Count - 1].Id;
                if (venues.Count < ExportPageSize) break;
            }
            after = null;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                List<Instructor> instructors = await _Services.Database.DirectoryListings.Instructors.ListPageAsync(after, ExportPageSize, token).ConfigureAwait(false);
                if (instructors.Count == 0) break;
                CsvWriter csv = new CsvWriter();
                foreach (Instructor i in instructors)
                {
                    csv.Row("Instructor", i.Id, i.Slug, i.DisplayName, i.Status, i.Audience, i.Affiliation, null, null, null, null, null, null, null, null, null, null, null, null, i.WebsiteUrl, null, null, null,
                        i.RankKey, i.RankLevel?.ToString(CultureInfo.InvariantCulture), i.LinkedUserId, i.CreatedUtc.ToString("O", CultureInfo.InvariantCulture), i.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture));
                    rows++;
                }
                await write(csv.ToString()).ConfigureAwait(false);
                after = instructors[instructors.Count - 1].Id;
                if (instructors.Count < ExportPageSize) break;
            }
            return rows;
        }

        /// <summary>
        /// One GeocodeWorker pass: geocode queued venues (import and regeocode jobs). Manual pins placed since the request
        /// are kept; a miss or provider error leaves geo.source None.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Venues processed.</returns>
        public async Task<int> GeocodePassAsync(CancellationToken token = default)
        {
            if (!await _Services.Directory.Geocoding.IsConfiguredAsync(token).ConfigureAwait(false)) return 0;
            List<Venue> venues = await _Services.Database.DirectoryListings.Venues.ClaimGeocodeAsync(_Services.Settings.Directory.GeocodeBatchSize, token).ConfigureAwait(false);
            foreach (Venue venue in venues)
            {
                token.ThrowIfCancellationRequested();
                if (venue.GeoSource == GeoSourceEnum.Manual) continue;
                GeocodeCandidate? found = await _Services.Directory.Geocoding.GeocodeVenueAsync(venue, "write", token).ConfigureAwait(false);
                await _Services.Database.DirectoryListings.Venues.SetGeocodedAsync(venue.Id, found?.Lat, found?.Lng, found?.TimeZone, DateTime.UtcNow, token).ConfigureAwait(false);
                await _Services.Directory.ChangedAsync(TargetTypeEnum.Venue, venue.Id, token).ConfigureAwait(false);
            }
            return venues.Count;
        }

        /// <summary>
        /// One GeocodeCachePruneWorker pass: delete expired geocoder cache entries.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entries deleted.</returns>
        public Task<int> PrunePassAsync(CancellationToken token = default)
        {
            return _Services.Database.DirectoryListings.GeocodeCache.PruneAsync(DateTime.UtcNow, 1000, token);
        }

        #endregion
    }
}
