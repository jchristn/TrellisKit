namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Settings;

    /// <summary>
    /// Venues (docs/PHASE4_CONTRACT.md sections 2.2, 3.1, 3.2, 3.9, and 3.11): member and public reads and lists with the
    /// spatial filters, staff create and edit (geocoding on address changes, manual pins), verify, close, archive, soft
    /// delete, slugs with redirects, photos, programs, and instructor links. Thread safe.
    /// </summary>
    public class VenueService
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
        public VenueService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// POST /api/v1/venues/enumerate: venues the caller may list (locked stubs where PartiallyGated shows them).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query (section 3.2).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidGeo.</exception>
        public async Task<EnumerationResult<VenueSummaryView>> ListAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            EnumerationQuery q = query ?? new EnumerationQuery();
            _Services.Limits.EnforceMember(actor, MediaService.ReadBucket, _Services.Settings.RateLimits.MemberReadsPerMinute, RateLimitService.Minute);
            VenueQuery prepared = Prepare(actor, q, DirectoryCollections.Venues, false);
            Stopwatch watch = Stopwatch.StartNew();
            EnumerationResult<Venue> page = await _Services.Database.DirectoryListings.Venues.EnumerateAsync(prepared.Request, prepared.Filter, token).ConfigureAwait(false);
            TrellisKitTelemetry.Record(DirectoryTelemetry.QueryDuration, watch.Elapsed.TotalSeconds, "kind", QueryKind(prepared));
            List<VenueSummaryView> views = await _Services.Directory.Views.VenueSummariesAsync(actor, page.Objects, new DirectoryViewOptions(), token).ConfigureAwait(false);
            return page.With(views);
        }

        /// <summary>
        /// POST /api/v1/public/venues/enumerate: Public, Active venues when directory.publicListingsEnabled.
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="sourceIp">Caller address (rate limit).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">404 NotAvailable, 400 ValidationFailed or InvalidGeo, 429 RateLimited.</exception>
        public async Task<EnumerationResult<VenueSummaryView>> PublicListAsync(EnumerationQuery? query, string? sourceIp, CancellationToken token = default)
        {
            DirectoryViewOptions options = await PublicOptionsAsync(sourceIp, token).ConfigureAwait(false);
            ContentActor actor = _Services.Actors.Anonymous(sourceIp);
            VenueQuery prepared = Prepare(actor, query ?? new EnumerationQuery(), DirectoryCollections.PublicVenues, false);
            prepared.Filter.Audiences = new List<ContentAudienceEnum> { ContentAudienceEnum.Public };
            prepared.Filter.Statuses = new List<VenueStatusEnum> { VenueStatusEnum.Active };
            prepared.Filter.LiveOnly = true;
            EnumerationResult<Venue> page = await _Services.Database.DirectoryListings.Venues.EnumerateAsync(prepared.Request, prepared.Filter, token).ConfigureAwait(false);
            List<VenueSummaryView> views = await _Services.Directory.Views.VenueSummariesAsync(actor, page.Objects, options, token).ConfigureAwait(false);
            return page.With(views);
        }

        /// <summary>
        /// GET /api/v1/venues/{idOrSlug} (a previous slug finds the venue; the payload carries the current slug).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="idOrSlug">Identifier or slug.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Venue (a locked stub when the caller needs a membership).</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<VenueView> GetAsync(ContentActor actor, string idOrSlug, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Limits.EnforceMember(actor, MediaService.ReadBucket, _Services.Settings.RateLimits.MemberReadsPerMinute, RateLimitService.Minute);
            Venue venue = await FindAsync(idOrSlug, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Venue");
            if (DirectoryViewBuilder.Level(actor, venue, new DirectoryViewOptions()) == 0) throw ApiErrorException.NotFound("Venue");
            return await _Services.Directory.Views.VenueAsync(actor, venue, new DirectoryViewOptions { Detail = true }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// GET /api/v1/public/venues/{id}.
        /// </summary>
        /// <param name="idOrSlug">Identifier or slug.</param>
        /// <param name="sourceIp">Caller address.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Venue.</returns>
        /// <exception cref="ApiErrorException">404 NotAvailable or NotFound, 429 RateLimited.</exception>
        public async Task<VenueView> PublicGetAsync(string idOrSlug, string? sourceIp, CancellationToken token = default)
        {
            DirectoryViewOptions options = await PublicOptionsAsync(sourceIp, token).ConfigureAwait(false);
            options.Detail = true;
            ContentActor actor = _Services.Actors.Anonymous(sourceIp);
            Venue venue = await FindAsync(idOrSlug, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Venue");
            if (DirectoryViewBuilder.Level(actor, venue, options) != 2) throw ApiErrorException.NotFound("Venue");
            return await _Services.Directory.Views.VenueAsync(actor, venue, options, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/venues/{id}/instructors/enumerate: instructors of a venue the caller may read (instructors whose
        /// audience the caller does not meet are omitted).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="venueId">Venue.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<EnumerationResult<InstructorSummaryView>> InstructorsAsync(ContentActor actor, string venueId, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            Venue venue = await FindAsync(venueId, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Venue");
            if (DirectoryViewBuilder.Level(actor, venue, new DirectoryViewOptions()) != 2) throw ApiErrorException.NotFound("Venue");
            EnumerationRequest request = _Services.Enumeration.Prepare(query, DirectoryCollections.VenueInstructors, actor.IsDirectoryStaff, venue.Id);
            InstructorFilter filter = new InstructorFilter { VenueId = venue.Id, Audiences = DirectoryAccess.VisibleAudiences(actor), LiveOnly = !actor.IsDirectoryStaff };
            EnumerationResult<Instructor> page = await _Services.Database.DirectoryListings.Instructors.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            List<InstructorSummaryView> views = await _Services.Directory.Views.InstructorSummariesAsync(actor, page.Objects, new DirectoryViewOptions { RoleVenueId = venue.Id }, token).ConfigureAwait(false);
            return page.With(views);
        }

        /// <summary>
        /// POST /api/v1/admin/venues/enumerate (directory staff): every status, exact addresses, staff counters.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query; filters status, geoSource (None finds venues that need a pin), verified, and the member filters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidGeo, 403 Forbidden.</exception>
        public async Task<EnumerationResult<AdminVenueView>> AdminListAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            DirectoryAccess.RequireStaff(actor);
            VenueQuery prepared = Prepare(actor, query ?? new EnumerationQuery(), DirectoryCollections.AdminVenues, true);
            EnumerationResult<Venue> page = await _Services.Database.DirectoryListings.Venues.EnumerateAsync(prepared.Request, prepared.Filter, token).ConfigureAwait(false);
            List<AdminVenueView> views = await _Services.Directory.Views.AdminVenuesAsync(actor, page.Objects, token).ConfigureAwait(false);
            return page.With(views);
        }

        /// <summary>
        /// GET /api/v1/admin/venues/{id} (directory staff).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Venue identifier or slug.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Admin venue.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<VenueView> AdminGetAsync(ContentActor actor, string id, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            DirectoryAccess.RequireStaff(actor);
            Venue venue = await FindAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Venue");
            return await AdminViewAsync(actor, venue, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/venues (directory staff). Geocodes the address when a geocoder is configured and no manual
        /// pin is given; a provider failure leaves geo.source None (needs a pin). Audited as venue.create.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Admin venue.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, InvalidGeo, UnknownOption, ContentFiltered, or GeocodeFailed; 403 Forbidden; 409 SlugInUse or MediaNotAttachable.</exception>
        public async Task<VenueView> CreateAsync(ContentActor actor, RequestInfo info, VenueRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            DirectoryAccess.RequireStaff(actor);
            Venue venue = await BuildAsync(actor, request, null, null, actor.UserId, true, token).ConfigureAwait(false);
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token, TargetTypeEnum.Venue).ConfigureAwait(false);
            try
            {
                venue = await _Services.Database.DirectoryListings.Venues.CreateAsync(venue, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                TrellisKitTelemetry.Add(DirectoryTelemetry.Venues, 1, "action", "create", "outcome", "conflict");
                throw SlugInUse();
            }
            await AfterSaveAsync(actor, venue, request.Labels, request.Tags, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "venue.create", TargetTypeEnum.Venue.ToString(), venue.Id, venue.Name, token).ConfigureAwait(false);
            TrellisKitTelemetry.Add(DirectoryTelemetry.Venues, 1, "action", "create", "outcome", "ok");
            return await AdminViewAsync(actor, venue, token).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT /api/v1/admin/venues/{id} (directory staff): omitted fields stay. A changed slug keeps the old one as a
        /// redirect. An address change geocodes again (replacing a manual pin) unless a pin is given. Audited as venue.update.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="id">Venue.</param>
        /// <param name="request">Body.</param>
        /// <param name="present">Fields present in the body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Admin venue.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400, 403, 404 NotFound, 409 SlugInUse.</exception>
        public async Task<VenueView> UpdateAsync(ContentActor actor, RequestInfo info, string id, VenueRequest request, HashSet<string> present, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(present);
            DirectoryAccess.RequireStaff(actor);
            Venue existing = await _Services.Database.DirectoryListings.Venues.ReadAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Venue");
            Venue venue = await BuildAsync(actor, request, existing, present, actor.UserId, true, token).ConfigureAwait(false);
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token, TargetTypeEnum.Venue).ConfigureAwait(false);
            Venue? saved;
            try
            {
                saved = await _Services.Database.DirectoryListings.Venues.UpdateAsync(venue, existing.Slug, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw SlugInUse();
            }
            if (saved == null) throw ApiErrorException.NotFound("Venue");
            await AfterSaveAsync(actor, saved, present.Contains("labels") ? request.Labels : null, present.Contains("tags") ? request.Tags : null, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "venue.update", TargetTypeEnum.Venue.ToString(), saved.Id, saved.Name, token).ConfigureAwait(false);
            TrellisKitTelemetry.Add(DirectoryTelemetry.Venues, 1, "action", "update", "outcome", "ok");
            return await AdminViewAsync(actor, saved, token).ConfigureAwait(false);
        }

        /// <summary>
        /// DELETE /api/v1/admin/venues/{id} (directory staff): soft delete; links removed. Audited as venue.delete.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="id">Venue.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task DeleteAsync(ContentActor actor, RequestInfo info, string id, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            DirectoryAccess.RequireStaff(actor);
            Venue venue = await _Services.Database.DirectoryListings.Venues.ReadAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Venue");
            if (!await _Services.Database.DirectoryListings.Venues.SoftDeleteAsync(venue.Id, DateTime.UtcNow, token).ConfigureAwait(false)) throw ApiErrorException.NotFound("Venue");
            await _Services.Media.UnpublishTargetAsync(TargetTypeEnum.Venue, venue.Id, token).ConfigureAwait(false);
            await _Services.Directory.ChangedAsync(TargetTypeEnum.Venue, venue.Id, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "venue.delete", TargetTypeEnum.Venue.ToString(), venue.Id, venue.Name, token).ConfigureAwait(false);
            TrellisKitTelemetry.Add(DirectoryTelemetry.Venues, 1, "action", "delete", "outcome", "ok");
        }

        /// <summary>
        /// POST /api/v1/admin/venues/{id}/verify or unverify (directory staff). Audited as venue.verify.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="id">Venue.</param>
        /// <param name="verified">Verify or unverify.</param>
        /// <param name="note">Note, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Admin venue.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<VenueView> VerifyAsync(ContentActor actor, RequestInfo info, string id, bool verified, string? note, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            DirectoryAccess.RequireStaff(actor);
            if (note != null && note.Trim().Length > 1000) throw ApiErrorException.Field("note", "At most 1000 characters.");
            Venue venue = await _Services.Database.DirectoryListings.Venues.ReadAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Venue");
            await _Services.Database.DirectoryListings.Venues.SetVerifiedAsync(venue.Id, verified, actor.UserId, DateTime.UtcNow, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, verified ? "venue.verify" : "venue.unverify", TargetTypeEnum.Venue.ToString(), venue.Id, String.IsNullOrWhiteSpace(note) ? venue.Name : note.Trim(), token).ConfigureAwait(false);
            TrellisKitTelemetry.Add(DirectoryTelemetry.Venues, 1, "action", "verify", "outcome", "ok");
            await _Services.Directory.ChangedAsync(TargetTypeEnum.Venue, venue.Id, token).ConfigureAwait(false);
            return await AdminGetAsync(actor, venue.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/venues/{id}/close, reopen, archive, unarchive (directory staff). Audited as venue.close or
        /// venue.archive (the summary names reopen and unarchive).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="id">Venue.</param>
        /// <param name="status">Target status (Closed, Active, Archived).</param>
        /// <param name="reason">Reason, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Admin venue.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound.</exception>
        public async Task<VenueView> SetStatusAsync(ContentActor actor, RequestInfo info, string id, VenueStatusEnum status, string? reason, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            DirectoryAccess.RequireStaff(actor);
            if (reason != null && reason.Trim().Length > 1000) throw ApiErrorException.Field("reason", "At most 1000 characters.");
            Venue venue = await _Services.Database.DirectoryListings.Venues.ReadAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Venue");
            string? why = String.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            await _Services.Database.DirectoryListings.Venues.SetStatusAsync(venue.Id, status, why, DateTime.UtcNow, token).ConfigureAwait(false);
            string action = status == VenueStatusEnum.Archived || venue.Status == VenueStatusEnum.Archived ? "venue.archive" : "venue.close";
            string summary = status == VenueStatusEnum.Active ? (venue.Status == VenueStatusEnum.Archived ? "Unarchived" : "Reopened") : status == VenueStatusEnum.Archived ? "Archived" : "Closed";
            await _Services.Audit.WriteAsync(info, action, TargetTypeEnum.Venue.ToString(), venue.Id, summary + (why == null ? String.Empty : ": " + why), token).ConfigureAwait(false);
            TrellisKitTelemetry.Add(DirectoryTelemetry.Venues, 1, "action", status == VenueStatusEnum.Archived ? "archive" : "close", "outcome", "ok");
            await _Services.Directory.ChangedAsync(TargetTypeEnum.Venue, venue.Id, token).ConfigureAwait(false);
            return await AdminGetAsync(actor, venue.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT /api/v1/admin/venues/{id}/instructors/{instructorId} (directory staff): link with a role.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="venueId">Venue.</param>
        /// <param name="instructorId">Instructor.</param>
        /// <param name="role">Role (default Regular).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The instructor as listed at the venue.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<InstructorSummaryView> LinkInstructorAsync(ContentActor actor, RequestInfo info, string venueId, string instructorId, VenueInstructorRoleEnum? role, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            DirectoryAccess.RequireStaff(actor);
            Venue venue = await _Services.Database.DirectoryListings.Venues.ReadAsync(venueId, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Venue");
            Instructor instructor = await _Services.Database.DirectoryListings.Instructors.ReadAsync(instructorId, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Instructor");
            await _Services.Database.DirectoryListings.Venues.LinkAsync(venue.Id, instructor.Id, role ?? VenueInstructorRoleEnum.Regular, DateTime.UtcNow, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "venue.update", TargetTypeEnum.Venue.ToString(), venue.Id, "Linked " + instructor.Id + " as " + (role ?? VenueInstructorRoleEnum.Regular), token).ConfigureAwait(false);
            await _Services.Directory.ChangedAsync(TargetTypeEnum.Venue, venue.Id, token).ConfigureAwait(false);
            Instructor fresh = await _Services.Database.DirectoryListings.Instructors.ReadAsync(instructor.Id, token).ConfigureAwait(false) ?? instructor;
            List<InstructorSummaryView> views = await _Services.Directory.Views.InstructorSummariesAsync(actor, new[] { fresh }, new DirectoryViewOptions { RoleVenueId = venue.Id }, token).ConfigureAwait(false);
            return views[0];
        }

        /// <summary>
        /// DELETE /api/v1/admin/venues/{id}/instructors/{instructorId} (directory staff).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="venueId">Venue.</param>
        /// <param name="instructorId">Instructor.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task UnlinkInstructorAsync(ContentActor actor, RequestInfo info, string venueId, string instructorId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            DirectoryAccess.RequireStaff(actor);
            Venue venue = await _Services.Database.DirectoryListings.Venues.ReadAsync(venueId, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Venue");
            if (!await _Services.Database.DirectoryListings.Venues.UnlinkAsync(venue.Id, instructorId, DateTime.UtcNow, token).ConfigureAwait(false)) throw ApiErrorException.NotFound("Link");
            await _Services.Audit.WriteAsync(info, "venue.update", TargetTypeEnum.Venue.ToString(), venue.Id, "Unlinked " + instructorId, token).ConfigureAwait(false);
            await _Services.Directory.ChangedAsync(TargetTypeEnum.Venue, venue.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// The public route gate: features.directory and directory.publicListingsEnabled on, and the per-IP limit.
        /// </summary>
        /// <param name="sourceIp">Caller address.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">404 NotAvailable, 429 RateLimited.</exception>
        public async Task PublicGateAsync(string? sourceIp, CancellationToken token = default)
        {
            await PublicOptionsAsync(sourceIp, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Find a live venue by identifier, slug, or previous slug.
        /// </summary>
        /// <param name="idOrSlug">Identifier or slug.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Venue, or null.</returns>
        public async Task<Venue?> FindAsync(string? idOrSlug, CancellationToken token = default)
        {
            string key = (idOrSlug ?? String.Empty).Trim();
            if (key.Length == 0) return null;
            if (key.StartsWith(DirectoryIdPrefixes.Venue, StringComparison.Ordinal))
            {
                Venue? byId = await _Services.Database.DirectoryListings.Venues.ReadAsync(key, token).ConfigureAwait(false);
                if (byId != null) return byId;
            }
            return await _Services.Database.DirectoryListings.Venues.ReadBySlugAsync(key, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Validate a request and produce the venue to store (not saved). Used by staff writes, approvals, and imports.
        /// </summary>
        /// <param name="actor">Caller (Public audience needs SystemAdmin; word filter).</param>
        /// <param name="request">Request.</param>
        /// <param name="existing">Current venue for an edit, or null for a create.</param>
        /// <param name="present">Fields present in an edit body, or null (every non-null field applies).</param>
        /// <param name="photoOwnerUserId">Member whose unattached photos may be attached, or null.</param>
        /// <param name="geocodeInline">Geocode an address change now (staff writes and approvals) instead of queueing it.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The venue to store.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, InvalidGeo, UnknownOption, ContentFiltered, GeocodeFailed; 409 MediaNotAttachable; 503 IntegrationNotConfigured.</exception>
        public async Task<Venue> BuildAsync(ContentActor actor, VenueRequest request, Venue? existing, HashSet<string>? present, string? photoOwnerUserId, bool geocodeInline, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            DirectorySettings settings = _Services.Settings.Directory;
            bool create = existing == null;
            DateTime now = DateTime.UtcNow;
            Venue v = existing == null ? new Venue { Id = IdGenerator.Generate(DirectoryIdPrefixes.Venue), CreatedUtc = now, CreatedByUserId = actor.UserId } : existing.Clone();
            v.UpdatedUtc = now;
            bool Has(string name) => present == null ? true : present.Contains(name);

            if (create || Has("name"))
            {
                string? name = DirectoryText.Text(request.Name, 120, "name", errors);
                if (name == null || name.Length < 2) errors["name"] = "2 to 120 characters.";
                else v.Name = name;
            }
            bool deriveSlug = false;
            if (request.Slug != null && Has("slug") && request.Slug.Trim().Length > 0)
            {
                // Public page slug rules (docs/PHASE4_CONTRACT.md section 18.1); an unchanged legacy slug stays valid.
                string slug = request.Slug.Trim().ToLowerInvariant();
                bool unchanged = existing != null && String.Equals(existing.Slug, slug, StringComparison.OrdinalIgnoreCase);
                if (!unchanged && !PublicSlugRules.IsValid(slug)) errors["slug"] = PublicSlugRules.Message;
                else if (!unchanged && await _Services.Database.PublicPages.SlugTakenAsync(PublicPageKindEnum.Venue, slug, existing?.Id, token).ConfigureAwait(false)) throw SlugInUse();
                else v.Slug = slug;
            }
            else if (create) deriveSlug = true;
            if (Has("affiliation") && (request.Affiliation != null || present != null)) v.Affiliation = DirectoryText.Text(request.Affiliation, 120, "affiliation", errors);
            if (Has("summary") && (request.Summary != null || present != null)) v.Summary = DirectoryText.Text(request.Summary, 300, "summary", errors);
            if (Has("description") && (request.Description != null || present != null)) v.Description = DirectoryText.Text(request.Description, settings.MaxDescriptionLength, "description", errors);
            bool addressChanged = false;
            if (Has("address") && (request.Address != null || present != null))
            {
                AddressRequest a = request.Address ?? new AddressRequest();
                string? line1 = DirectoryText.Text(a.Line1, 200, "address.line1", errors);
                string? line2 = DirectoryText.Text(a.Line2, 200, "address.line2", errors);
                string? locality = DirectoryText.Text(a.Locality, 100, "address.locality", errors);
                string? region = DirectoryText.Text(a.Region, 100, "address.region", errors);
                string? postal = DirectoryText.Text(a.PostalCode, 20, "address.postalCode", errors);
                string? country = DirectoryText.Text(a.CountryCode, 2, "address.countryCode", errors)?.ToUpperInvariant();
                bool any = line1 != null || line2 != null || locality != null || region != null || postal != null;
                if (any && country == null) errors["address.countryCode"] = "Required when any address field is set.";
                else if (country != null && Validators.Country(country) != null) errors["address.countryCode"] = Validators.Country(country)!;
                addressChanged = !String.Equals(line1, v.Line1, StringComparison.Ordinal) || !String.Equals(line2, v.Line2, StringComparison.Ordinal) || !String.Equals(locality, v.Locality, StringComparison.Ordinal) ||
                    !String.Equals(region, v.Region, StringComparison.Ordinal) || !String.Equals(postal, v.PostalCode, StringComparison.Ordinal) || !String.Equals(country, v.CountryCode, StringComparison.Ordinal);
                v.Line1 = line1;
                v.Line2 = line2;
                v.Locality = locality;
                v.Region = region;
                v.PostalCode = postal;
                v.CountryCode = country;
            }
            if (request.AddressVisibility != null && Has("addressVisibility")) v.AddressVisibility = request.AddressVisibility.Value;
            if (Has("timeZone") && (request.TimeZone != null || present != null)) v.TimeZone = DirectoryText.TimeZone(request.TimeZone, "timeZone", errors);
            if (Has("phone") && (request.Phone != null || present != null)) v.Phone = DirectoryText.Text(request.Phone, 40, "phone", errors);
            if (Has("email") && (request.Email != null || present != null))
            {
                string? email = DirectoryText.Text(request.Email, 254, "email", errors);
                if (email != null && Validators.Email(email) != null) errors["email"] = "Enter a valid email address.";
                v.Email = email;
            }
            if (Has("websiteUrl") && (request.WebsiteUrl != null || present != null)) v.WebsiteUrl = DirectoryText.HttpsUrl(request.WebsiteUrl, "websiteUrl", errors);
            if (Has("socialLinks") && (request.SocialLinks != null || present != null)) v.SocialLinks = DirectoryText.SocialLinks(request.SocialLinks, settings.MaxSocialLinks, errors);
            if (Has("scheduleText") && (request.ScheduleText != null || present != null)) v.ScheduleText = DirectoryText.Text(request.ScheduleText, 1000, "scheduleText", errors);
            if (Has("programs") && (request.Programs != null || present != null)) v.Programs = await ProgramKeysAsync(request.Programs, existing?.Programs, token).ConfigureAwait(false);
            if (request.VisitorPolicy != null && Has("visitorPolicy")) v.VisitorPolicy = request.VisitorPolicy.Value;
            if (Has("visitorPolicyNote") && (request.VisitorPolicyNote != null || present != null)) v.VisitorPolicyNote = DirectoryText.Text(request.VisitorPolicyNote, 300, "visitorPolicyNote", errors);
            if (Has("audience") && request.Audience != null)
            {
                if (request.Audience == ContentAudienceEnum.Public && !actor.IsSystemAdmin) errors["audience"] = "Only a SystemAdmin may set Public.";
                else v.Audience = request.Audience.Value;
            }
            else if (create) v.Audience = actor.Access.DefaultAudienceFor(FeatureKeyEnum.Directory);
            if (Has("photoMediaIds") && (request.PhotoMediaIds != null || present != null))
            {
                List<string> ids = (request.PhotoMediaIds ?? new List<string>()).Where(i => !String.IsNullOrWhiteSpace(i)).Select(i => i.Trim()).Distinct(StringComparer.Ordinal).ToList();
                if (ids.Count > settings.MaxPhotos) errors["photoMediaIds"] = "At most " + settings.MaxPhotos + " photos.";
                else await ValidatePhotosAsync(actor, ids, TargetTypeEnum.Venue, existing?.Id, photoOwnerUserId, "photoMediaIds", errors, token).ConfigureAwait(false);
                v.PhotoMediaIds = ids;
            }
            if (request.Geo != null && Has("geo"))
            {
                if (request.Geo.Lat == null && request.Geo.Lng == null)
                {
                    v.Lat = null;
                    v.Lng = null;
                    v.GeoSource = GeoSourceEnum.None;
                    v.GeocodedUtc = null;
                }
                else
                {
                    if (request.Geo.Lat == null || request.Geo.Lat < -90 || request.Geo.Lat > 90) throw DirectoryFilters.InvalidGeo("geo.lat", "Latitude must be -90 to 90.");
                    if (request.Geo.Lng == null || request.Geo.Lng < -180 || request.Geo.Lng > 180) throw DirectoryFilters.InvalidGeo("geo.lng", "Longitude must be -180 to 180.");
                    v.Lat = request.Geo.Lat;
                    v.Lng = request.Geo.Lng;
                    v.GeoSource = GeoSourceEnum.Manual;
                    v.GeocodedUtc = now;
                }
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            // The default slug: name, locality, and region (for example example-club-exampleville-ex).
            if (deriveSlug) v.Slug = await FreeSlugAsync(PublicSlugRules.Derive("venue", v.Name, v.Locality, v.Region), v.Id, token).ConfigureAwait(false);
            if (v.Programs.Count > 0)
            {
                List<DirectoryProgram> programs = await _Services.Directory.Config.ProgramsAsync(token).ConfigureAwait(false);
                HashSet<string> allowed = new HashSet<string>(programs.Where(p => !p.IsRetired).Select(p => p.Key), StringComparer.OrdinalIgnoreCase);
                HashSet<string> kept = new HashSet<string>(existing?.Programs ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                List<string> unknown = v.Programs.Where(k => !allowed.Contains(k) && !kept.Contains(k)).ToList();
                if (unknown.Count > 0) throw new ApiErrorException(400, "UnknownOption", "Unknown program " + String.Join(", ", unknown) + ".", new Dictionary<string, string> { { "programs", "Unknown program key " + unknown[0] + "." } });
            }
            await _Services.WordFilter.CheckAsync(actor, TargetTypeEnum.Venue, new Dictionary<string, string?>
            {
                { "name", v.Name }, { "affiliation", v.Affiliation }, { "summary", v.Summary }, { "description", v.Description }, { "scheduleText", v.ScheduleText }, { "visitorPolicyNote", v.VisitorPolicyNote }
            }, token).ConfigureAwait(false);

            bool pinGiven = request.Geo != null && Has("geo");
            bool explicitGeocode = request.Geocode == true;
            if (!pinGiven && (addressChanged || explicitGeocode))
            {
                if (addressChanged && v.GeoSource == GeoSourceEnum.Manual)
                {
                    v.Lat = null;
                    v.Lng = null;
                    v.GeoSource = GeoSourceEnum.None;
                    v.GeocodedUtc = null;
                }
                if (geocodeInline || explicitGeocode)
                {
                    bool configured = await _Services.Directory.Geocoding.IsConfiguredAsync(token).ConfigureAwait(false);
                    if (!configured && explicitGeocode) throw new ApiErrorException(503, "IntegrationNotConfigured", "Geocoding needs a configured geocoder (directory.geocoder.provider).");
                    GeocodeCandidate? found = configured ? await _Services.Directory.Geocoding.GeocodeVenueAsync(v, "write", token).ConfigureAwait(false) : null;
                    if (found != null)
                    {
                        v.Lat = found.Lat;
                        v.Lng = found.Lng;
                        v.GeoSource = GeoSourceEnum.Geocoder;
                        v.GeocodedUtc = now;
                        if (v.TimeZone == null && found.TimeZone != null) v.TimeZone = found.TimeZone;
                    }
                    else
                    {
                        if (explicitGeocode) throw new ApiErrorException(400, "GeocodeFailed", "The geocoder found no usable result for this address; place the pin manually.", new Dictionary<string, string> { { "address", "No geocoder match." } });
                        v.Lat = null;
                        v.Lng = null;
                        v.GeoSource = GeoSourceEnum.None;
                        v.GeocodedUtc = null;
                    }
                }
                else
                {
                    v.GeocodePendingUtc = now;
                }
            }
            return v;
        }

        /// <summary>
        /// Attach photos, publish public media copies, apply labels and tags, and announce the change.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="venue">Stored venue.</param>
        /// <param name="labels">Labels, or null to keep.</param>
        /// <param name="tags">Tags, or null to keep.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task AfterSaveAsync(ContentActor actor, Venue venue, List<string>? labels, Dictionary<string, string>? tags, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(venue);
            await _Services.Media.AttachAsync(TargetTypeEnum.Venue, venue.Id, venue.PhotoMediaIds, token).ConfigureAwait(false);
            if (labels != null || tags != null) await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.Venue, venue.Id, labels, tags, token).ConfigureAwait(false);
            await _Services.Directory.ChangedAsync(TargetTypeEnum.Venue, venue.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Validate photo media: Ready images, unattached (or attached to this target), owned by the caller, the given
        /// owner, or any owner for a SystemAdmin.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="ids">Media ids.</param>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target being edited, or null.</param>
        /// <param name="ownerUserId">Owner whose media may be attached, or null.</param>
        /// <param name="field">Field for errors.</param>
        /// <param name="errors">Field errors.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task ValidatePhotosAsync(ContentActor actor, List<string> ids, TargetTypeEnum targetType, string? targetId, string? ownerUserId, string field, Dictionary<string, string> errors, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(ids);
            ArgumentNullException.ThrowIfNull(errors);
            if (ids.Count == 0) return;
            List<MediaAttachment> rows = await _Services.Database.Media.ReadManyAsync(ids, token).ConfigureAwait(false);
            Dictionary<string, MediaAttachment> byId = rows.ToDictionary(r => r.Id, StringComparer.Ordinal);
            for (int i = 0; i < ids.Count; i++)
            {
                string key = field + "[" + i + "]";
                if (!byId.TryGetValue(ids[i], out MediaAttachment? media) || media.IsDeleted) { errors[key] = "Not found."; continue; }
                bool here = media.IsAttached && media.AttachedTargetType == targetType && targetId != null && media.AttachedTargetId == targetId;
                if (media.IsAttached && !here) { errors[key] = "Already attached to other content."; continue; }
                if (!here && !actor.IsSystemAdmin && media.OwnerUserId != actor.UserId && media.OwnerUserId != ownerUserId) { errors[key] = "Not your media."; continue; }
                if (media.Kind != MediaKindEnum.Image) { errors[key] = "Must be an image."; continue; }
                if (media.Status != MediaStatusEnum.Ready) errors[key] = "The image is not ready yet.";
            }
        }

        /// <summary>
        /// GET /api/v1/admin/directory/export.csv rows: every live venue (all statuses), a page at a time.
        /// </summary>
        /// <param name="afterId">Exclusive position, or null.</param>
        /// <param name="limit">Most venues.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Venues.</returns>
        public Task<List<Venue>> ExportPageAsync(string? afterId, int limit, CancellationToken token = default)
        {
            return _Services.Database.DirectoryListings.Venues.ListPageAsync(afterId, limit, token);
        }

        #endregion

        #region Private-Methods

        private async Task<VenueView> AdminViewAsync(ContentActor actor, Venue venue, CancellationToken token)
        {
            Venue fresh = await _Services.Database.DirectoryListings.Venues.ReadAsync(venue.Id, token).ConfigureAwait(false) ?? venue;
            return await _Services.Directory.Views.VenueAsync(actor, fresh, new DirectoryViewOptions { Admin = true, Detail = true }, token).ConfigureAwait(false);
        }

        private async Task<DirectoryViewOptions> PublicOptionsAsync(string? sourceIp, CancellationToken token)
        {
            if (!_Services.Property.Property.Features.Directory || !await _Services.Directory.Config.FlagAsync(DirectoryPropertySettings.PublicListingsEnabled, token).ConfigureAwait(false))
                throw new ApiErrorException(404, "NotAvailable", "Public venue listings are not available for this property.");
            _Services.Limits.EnforceIp(sourceIp, "public-directory", _Services.Settings.RateLimits.PublicFeedPerMinute, RateLimitService.Minute);
            return new DirectoryViewOptions { PublicRoute = true, ShowContact = await _Services.Directory.Config.FlagAsync(DirectoryPropertySettings.PublicShowsContact, token).ConfigureAwait(false) };
        }

        private VenueQuery Prepare(ContentActor actor, EnumerationQuery q, EnumerationCollection collection, bool admin)
        {
            GeoNear? near = DirectoryFilters.Near(q, _Services.Settings.Directory);
            GeoBox? box = DirectoryFilters.Box(q);
            if (near != null && box != null) throw DirectoryFilters.InvalidGeo("filters.bbox", "near and bbox cannot be combined.");
            if (near != null && String.IsNullOrWhiteSpace(q.Ordering) && String.IsNullOrWhiteSpace(q.ContinuationToken)) q.Ordering = "DistanceAscending";
            EnumerationRequest request = _Services.Enumeration.Prepare(q, collection, actor.IsDirectoryStaff);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (String.Equals(request.Ordering.Name, "DistanceAscending", StringComparison.Ordinal) && near == null) errors["ordering"] = "DistanceAscending needs filters.near.";
            if (String.Equals(request.Ordering.Name, "Relevance", StringComparison.Ordinal) && String.IsNullOrEmpty(request.Query.Search)) errors["ordering"] = "Relevance needs search.";
            VenueFilter filter = new VenueFilter
            {
                Near = near,
                Box = box,
                Programs = DirectoryFilters.Strings(q, "programs"),
                CountryCode = q.FilterString("countryCode"),
                Region = q.FilterString("region"),
                Locality = q.FilterString("locality"),
                VisitorPolicies = DirectoryFilters.Enums<VisitorPolicyEnum>(q, "visitorPolicy", errors),
                VerifiedOnly = q.FilterBool("verifiedOnly", errors) ?? false,
                IncludeClosed = q.FilterBool("includeClosed", errors) ?? false,
                InstructorId = q.FilterString("instructorId"),
                AudienceFilter = DirectoryFilters.Enums<ContentAudienceEnum>(q, "audience", errors),
                Audiences = DirectoryAccess.ListedAudiences(actor),
                LiveOnly = !actor.IsDirectoryStaff,
                Staff = admin
            };
            if (admin)
            {
                List<VenueStatusEnum> statuses = DirectoryFilters.Enums<VenueStatusEnum>(q, "status", errors);
                if (statuses.Count > 0) filter.Statuses = statuses;
                else filter.IncludeClosed = true;
                filter.GeoSource = q.FilterEnum<GeoSourceEnum>("geoSource", errors);
                filter.Verified = q.FilterBool("verified", errors);
                filter.LiveOnly = false;
            }
            DirectoryFilters.ThrowIfAny(errors);
            return new VenueQuery { Request = request, Filter = filter };
        }

        private static string QueryKind(VenueQuery prepared)
        {
            if (prepared.Filter.Near != null) return "near";
            if (prepared.Filter.Box != null) return "bbox";
            return String.IsNullOrEmpty(prepared.Request.Query.Search) ? "list" : "search";
        }

        private async Task<List<string>> ProgramKeysAsync(List<string>? keys, List<string>? existing, CancellationToken token)
        {
            List<DirectoryProgram> programs = await _Services.Directory.Config.ProgramsAsync(token).ConfigureAwait(false);
            List<string> result = new List<string>();
            foreach (string raw in (keys ?? new List<string>()).Where(k => !String.IsNullOrWhiteSpace(k)).Take(DirectoryConfigService.ProgramBound))
            {
                string key = raw.Trim();
                DirectoryProgram? match = programs.FirstOrDefault(p => String.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
                string stored = match?.Key ?? key;
                if (!result.Contains(stored, StringComparer.OrdinalIgnoreCase)) result.Add(stored);
            }
            return result;
        }

        private async Task<string> FreeSlugAsync(string slug, string? exceptId, CancellationToken token)
        {
            string candidate = slug;
            for (int i = 2; i < 50; i++)
            {
                if (!await _Services.Database.PublicPages.SlugTakenAsync(PublicPageKindEnum.Venue, candidate, exceptId, token).ConfigureAwait(false)) return candidate;
                candidate = PublicSlugRules.WithSuffix(slug, i);
            }
            return PublicSlugRules.WithSuffix(slug.Length > 70 ? slug.Substring(0, 70).TrimEnd('-') : slug, 1000 + Random.Shared.Next(9000));
        }

        private static ApiErrorException SlugInUse()
        {
            return new ApiErrorException(409, "SlugInUse", "Another venue uses this slug.", new Dictionary<string, string> { { "slug", "In use." } });
        }

        #endregion
    }
}
