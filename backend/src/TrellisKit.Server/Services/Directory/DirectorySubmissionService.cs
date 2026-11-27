namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
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
    using TrellisKit.Server.Services.Email;
    using TrellisKit.Server.Services.Email.Templates;

    /// <summary>
    /// Member directory submissions and their review (docs/PHASE4_CONTRACT.md sections 2.4, 3.7, and 3.8; resolved
    /// decisions 7 and 9): NewVenue, EditVenue, NewInstructor, EditInstructor, ClaimInstructor, and ReportClosed with
    /// structured field rows, duplicate detection, the pending limit, the word filter, reviewer overrides, approval in one
    /// transaction with the SubmissionStale check, rejection, withdrawal, media attach and cleanup, decision
    /// notifications and emails, and the overdue notice. Nothing from a submission is published before approval.
    /// Thread safe.
    /// </summary>
    public class DirectorySubmissionService
    {
        #region Public-Members

        /// <summary>
        /// Most possible duplicates shown.
        /// </summary>
        public const int MaxDuplicates = 5;

        #endregion

        #region Private-Members

        // The admin console's directory page on its submissions tab (the review queue).
        private const string StaffPath = "/admin/directory?tab=submissions";

        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public DirectorySubmissionService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// POST /api/v1/directory/submissions. Answers a Draft (nothing queued) when possible duplicates were found and
        /// confirmNotDuplicate is false.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The submission (Status Draft or Pending).</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, ContentFiltered, UnknownOption; 403 SubmissionsDisabled, PostingNotAllowed, Muted; 404 NotFound; 409 SubmissionLimitReached or AlreadyClaimed; 429 RateLimited.</exception>
        public async Task<DirectorySubmissionView> SubmitAsync(ContentActor actor, DirectorySubmissionRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            string userId = actor.RequireUserId();
            actor.RequireNotMuted();
            actor.RequireCanPost();
            if (request.Type == null) throw ApiErrorException.Field("type", "Use NewVenue, EditVenue, NewInstructor, EditInstructor, ClaimInstructor, or ReportClosed.");
            DirectorySubmissionTypeEnum type = request.Type.Value;
            DirectoryConfig config = await _Services.Directory.Config.CurrentAsync(token).ConfigureAwait(false);
            if (!config.SubmissionsEnabled) throw new ApiErrorException(403, "SubmissionsDisabled", "Directory suggestions are turned off for this property.");
            if (type == DirectorySubmissionTypeEnum.ClaimInstructor && !config.ClaimsEnabled) throw new ApiErrorException(403, "SubmissionsDisabled", "Instructor claims are turned off for this property.");
            DirectorySettings settings = _Services.Settings.Directory;
            int perDay = DailyLimit(actor);
            _Services.Limits.CheckMember(actor, "directory-submissions", perDay, RateLimitService.Day);
            if (await _Services.Database.DirectoryListings.Submissions.CountPendingByUserAsync(userId, token).ConfigureAwait(false) >= settings.MaxPendingSubmissionsPerMember)
                throw new ApiErrorException(409, "SubmissionLimitReached", "You have " + settings.MaxPendingSubmissionsPerMember + " pending suggestions; wait for a review or withdraw one.");
            string? note = DirectoryText.Text(request.Note, 1000, "note", new Dictionary<string, string>());
            if (note != null && note.Length > 1000) throw ApiErrorException.Field("note", "At most 1000 characters.");
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, false, token, TargetTypeEnum.DirectorySubmission).ConfigureAwait(false);

            DateTime now = DateTime.UtcNow;
            DirectorySubmission submission = new DirectorySubmission
            {
                Id = IdGenerator.Generate(DirectoryIdPrefixes.Submission),
                Type = type,
                Status = DirectorySubmissionStatusEnum.Pending,
                SubmitterUserId = userId,
                Note = note,
                DueUtc = now.AddHours(settings.ReviewTargetHours),
                CreatedUtc = now,
                UpdatedUtc = now
            };
            List<object> duplicates = new List<object>();
            long duplicateCount = 0;
            switch (type)
            {
                case DirectorySubmissionTypeEnum.NewVenue:
                case DirectorySubmissionTypeEnum.EditVenue:
                    {
                        submission.TargetType = TargetTypeEnum.Venue;
                        VenueRequest fields = DirectorySubmissionFields.Venue(request.Fields, out HashSet<string> present);
                        GeoRequest? memberPin = DirectorySubmissionFields.TakePin(fields, present);
                        Venue? target = null;
                        if (type == DirectorySubmissionTypeEnum.EditVenue)
                        {
                            target = await RequireReadableVenueAsync(actor, request.TargetId, token).ConfigureAwait(false);
                            if (present.Count == 0 && memberPin == null) throw ApiErrorException.Field("fields", "Send at least one changed field.");
                        }
                        else if (!present.Contains("addressVisibility") && present.Contains("address"))
                        {
                            fields.AddressVisibility = settings.SubmissionDefaultAddressVisibility;
                            present.Add("addressVisibility");
                        }
                        Venue proposed = await _Services.Directory.Venues.BuildAsync(actor, fields, target, type == DirectorySubmissionTypeEnum.EditVenue ? present : null, userId, false, token).ConfigureAwait(false);
                        submission.Fields = DirectorySubmissionFields.Rows(proposed, type == DirectorySubmissionTypeEnum.NewVenue ? present.Append("name") : present);
                        submission.TargetId = target?.Id;
                        submission.TargetName = target?.Name ?? proposed.Name;
                        submission.TargetUpdatedUtc = target?.UpdatedUtc;
                        if (target != null) DirectorySubmissionFields.FillCurrent(submission.Fields, DirectorySubmissionFields.Values(target));
                        if (present.Contains("address") && proposed.CountryCode != null)
                        {
                            GeocodeCandidate? pin = await _Services.Directory.Geocoding.GeocodeVenueAsync(proposed, "write", token).ConfigureAwait(false);
                            if (pin != null)
                            {
                                submission.ProposedLat = pin.Lat;
                                submission.ProposedLng = pin.Lng;
                                proposed.Lat = pin.Lat;
                                proposed.Lng = pin.Lng;
                            }
                        }
                        if (memberPin != null)
                        {
                            // A pin the member placed on the map wins over the geocoded one; the reviewer confirms it.
                            submission.ProposedLat = memberPin.Lat;
                            submission.ProposedLng = memberPin.Lng;
                            proposed.Lat = memberPin.Lat;
                            proposed.Lng = memberPin.Lng;
                        }
                        if (type == DirectorySubmissionTypeEnum.NewVenue)
                        {
                            List<Venue> found = await VenueDuplicatesAsync(proposed, token).ConfigureAwait(false);
                            duplicateCount = found.Count;
                            duplicates.AddRange(await _Services.Directory.Views.VenueSummariesAsync(actor, found, new DirectoryViewOptions(), token).ConfigureAwait(false));
                        }
                        break;
                    }
                case DirectorySubmissionTypeEnum.NewInstructor:
                case DirectorySubmissionTypeEnum.EditInstructor:
                    {
                        submission.TargetType = TargetTypeEnum.Instructor;
                        InstructorRequest fields = DirectorySubmissionFields.Instructor(request.Fields, out HashSet<string> present);
                        Instructor? target = null;
                        if (type == DirectorySubmissionTypeEnum.EditInstructor)
                        {
                            target = await RequireReadableInstructorAsync(actor, request.TargetId, token).ConfigureAwait(false);
                            if (present.Count == 0) throw ApiErrorException.Field("fields", "Send at least one changed field.");
                        }
                        Instructor proposed = await _Services.Directory.Instructors.BuildAsync(actor, fields, target, type == DirectorySubmissionTypeEnum.EditInstructor ? present : null, userId, token).ConfigureAwait(false);
                        List<string> venueIds = await ReadableVenueIdsAsync(actor, fields.VenueIds, token).ConfigureAwait(false);
                        submission.Fields = DirectorySubmissionFields.Rows(proposed, venueIds, type == DirectorySubmissionTypeEnum.NewInstructor ? present.Append("displayName") : present);
                        submission.TargetId = target?.Id;
                        submission.TargetName = target?.DisplayName ?? proposed.DisplayName;
                        submission.TargetUpdatedUtc = target?.UpdatedUtc;
                        if (target != null)
                        {
                            Dictionary<string, List<string?>> current = DirectorySubmissionFields.Values(target);
                            current["venueIds"] = (await _Services.Database.DirectoryListings.Instructors.VenueIdsAsync(target.Id, 50, token).ConfigureAwait(false)).Cast<string?>().ToList();
                            DirectorySubmissionFields.FillCurrent(submission.Fields, current);
                        }
                        if (type == DirectorySubmissionTypeEnum.NewInstructor)
                        {
                            List<Instructor> found = await InstructorDuplicatesAsync(proposed, token).ConfigureAwait(false);
                            duplicateCount = found.Count;
                            duplicates.AddRange(await _Services.Directory.Views.InstructorSummariesAsync(actor, found, new DirectoryViewOptions(), token).ConfigureAwait(false));
                        }
                        break;
                    }
                case DirectorySubmissionTypeEnum.ClaimInstructor:
                    {
                        submission.TargetType = TargetTypeEnum.Instructor;
                        if (request.Fields != null && request.Fields.Count > 0) throw ApiErrorException.Field("fields", "A claim takes no fields; explain it in note.");
                        if (note == null || note.Length < 10) throw ApiErrorException.Field("note", "Explain the claim (10 to 1000 characters).");
                        Instructor target = await RequireReadableInstructorAsync(actor, request.TargetId, token).ConfigureAwait(false);
                        if (target.LinkedUserId != null) throw new ApiErrorException(409, "AlreadyClaimed", "This entry is already linked to an account.");
                        if (await _Services.Database.DirectoryListings.Instructors.ReadByLinkedUserAsync(userId, token).ConfigureAwait(false) != null)
                            throw new ApiErrorException(409, "AlreadyClaimed", "Your account is already linked to an entry.");
                        if (await _Services.Database.DirectoryListings.Submissions.HasPendingClaimAsync(userId, target.Id, token).ConfigureAwait(false))
                            throw new ApiErrorException(409, "AlreadyClaimed", "You already have a pending claim on this entry.");
                        submission.TargetId = target.Id;
                        submission.TargetName = target.DisplayName;
                        break;
                    }
                default:
                    {
                        submission.TargetType = TargetTypeEnum.Venue;
                        if (request.Fields != null && request.Fields.Count > 0) throw ApiErrorException.Field("fields", "A closure report takes no fields; add a note.");
                        Venue target = await RequireReadableVenueAsync(actor, request.TargetId, token).ConfigureAwait(false);
                        if (target.Status != VenueStatusEnum.Active) throw new ApiErrorException(409, "AlreadyClosed", "This venue is not active.");
                        submission.TargetId = target.Id;
                        submission.TargetName = target.Name;
                        break;
                    }
            }
            Dictionary<string, string?> text = DirectorySubmissionFields.FilterText(submission.Fields);
            text["note"] = note;
            await _Services.WordFilter.CheckAsync(actor, TargetTypeEnum.DirectorySubmission, text, token).ConfigureAwait(false);

            if (duplicates.Count > 0 && !request.ConfirmNotDuplicate)
            {
                DirectorySubmissionView draft = await ViewAsync(actor, submission, false, token).ConfigureAwait(false);
                draft.Id = String.Empty;
                draft.Status = DirectorySubmissionStatusEnum.Draft;
                draft.PossibleDuplicates = new BoundedPreview<object> { Objects = duplicates.Take(MaxDuplicates).ToList(), TotalRecords = duplicateCount };
                TrellisKitTelemetry.Add(DirectoryTelemetry.Submissions, 1, "type", type.ToString(), "action", "submit", "outcome", "draft");
                return draft;
            }
            try
            {
                submission = await _Services.Database.DirectoryListings.Submissions.CreateAsync(submission, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw new ApiErrorException(409, "AlreadyClaimed", "You already have a pending claim on this entry.");
            }
            _Services.Limits.EnforceMember(actor, "directory-submissions", perDay, RateLimitService.Day);
            if (request.Labels != null || request.Tags != null) await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.DirectorySubmission, submission.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            await NotifyStaffAsync(actor, submission, token).ConfigureAwait(false);
            await RefreshPendingGaugeAsync(token).ConfigureAwait(false);
            TrellisKitTelemetry.Add(DirectoryTelemetry.Submissions, 1, "type", type.ToString(), "action", "submit", "outcome", "ok");
            DirectorySubmissionView view = await ViewAsync(actor, submission, false, token).ConfigureAwait(false);
            if (duplicates.Count > 0) view.PossibleDuplicates = new BoundedPreview<object> { Objects = duplicates.Take(MaxDuplicates).ToList(), TotalRecords = duplicateCount };
            return view;
        }

        /// <summary>
        /// POST /api/v1/me/directory/submissions/enumerate: the caller's own submissions (filters status, type).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        public async Task<EnumerationResult<DirectorySubmissionView>> MineAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string userId = actor.RequireUserId();
            EnumerationRequest request = _Services.Enumeration.Prepare(query, DirectoryCollections.MySubmissions, false);
            DirectorySubmissionFilter filter = Filter(request.Query, false);
            filter.SubmitterUserId = userId;
            EnumerationResult<DirectorySubmission> page = await _Services.Database.DirectoryListings.Submissions.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            return page.With(await ViewsAsync(actor, page.Objects, false, token).ConfigureAwait(false));
        }

        /// <summary>
        /// POST /api/v1/directory/submissions/{id}/withdraw: the submitter withdraws a pending submission; its unattached
        /// media is deleted.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Submission.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Submission.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">404 NotFound, 409 NotPending.</exception>
        public async Task<DirectorySubmissionView> WithdrawAsync(ContentActor actor, string id, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string userId = actor.RequireUserId();
            DirectorySubmission submission = await _Services.Database.DirectoryListings.Submissions.ReadAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Submission");
            if (submission.SubmitterUserId != userId) throw ApiErrorException.NotFound("Submission");
            if (submission.Status != DirectorySubmissionStatusEnum.Pending ||
                !await _Services.Database.DirectoryListings.Submissions.DecideAsync(submission.Id, DirectorySubmissionStatusEnum.Withdrawn, null, null, DateTime.UtcNow, token).ConfigureAwait(false))
                throw new ApiErrorException(409, "NotPending", "Only a pending submission can be withdrawn.");
            await CleanupMediaAsync(submission, false, token).ConfigureAwait(false);
            await RefreshPendingGaugeAsync(token).ConfigureAwait(false);
            TrellisKitTelemetry.Add(DirectoryTelemetry.Submissions, 1, "type", submission.Type.ToString(), "action", "withdraw", "outcome", "ok");
            DirectorySubmission fresh = await _Services.Database.DirectoryListings.Submissions.ReadAsync(submission.Id, token).ConfigureAwait(false) ?? submission;
            return await ViewAsync(actor, fresh, false, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/directory/submissions/enumerate (directory staff): filters status (default Pending), type,
        /// submitterUserId, targetId; oldest first by default.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden.</exception>
        public async Task<EnumerationResult<DirectorySubmissionView>> AdminListAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            DirectoryAccess.RequireStaff(actor);
            EnumerationRequest request = _Services.Enumeration.Prepare(query, DirectoryCollections.AdminSubmissions, true);
            DirectorySubmissionFilter filter = Filter(request.Query, true);
            EnumerationResult<DirectorySubmission> page = await _Services.Database.DirectoryListings.Submissions.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            return page.With(await ViewsAsync(actor, page.Objects, true, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/admin/directory/submissions/{id} (directory staff): with a fresh diff against the current target
        /// (loading it is what an approval's stale check compares against) and the possible duplicates.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Submission.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Submission.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<DirectorySubmissionView> AdminGetAsync(ContentActor actor, string id, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            DirectoryAccess.RequireStaff(actor);
            DirectorySubmission submission = await _Services.Database.DirectoryListings.Submissions.ReadAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Submission");
            if (submission.Status == DirectorySubmissionStatusEnum.Pending) submission = await RefreshAsync(submission, token).ConfigureAwait(false);
            DirectorySubmissionView view = await ViewAsync(actor, submission, true, token).ConfigureAwait(false);
            if (submission.Status == DirectorySubmissionStatusEnum.Pending && (submission.Type == DirectorySubmissionTypeEnum.NewVenue || submission.Type == DirectorySubmissionTypeEnum.NewInstructor))
                view.PossibleDuplicates = await DuplicatesOfAsync(actor, submission, token).ConfigureAwait(false);
            return view;
        }

        /// <summary>
        /// PUT /api/v1/admin/directory/submissions/{id} (directory staff): reviewer overrides before approval.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Submission.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Submission.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, InvalidGeo; 403 Forbidden; 404 NotFound; 409 NotPending.</exception>
        public async Task<DirectorySubmissionView> OverrideAsync(ContentActor actor, string id, SubmissionOverrideRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            DirectoryAccess.RequireStaff(actor);
            DirectorySubmission submission = await _Services.Database.DirectoryListings.Submissions.ReadAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Submission");
            if (submission.Status != DirectorySubmissionStatusEnum.Pending) throw new ApiErrorException(409, "NotPending", "Only a pending submission can be changed.");
            List<DirectorySubmissionField> rows = new List<DirectorySubmissionField>();
            bool hasFields = request.Fields != null && request.Fields.Count > 0;
            if (hasFields && (submission.Type == DirectorySubmissionTypeEnum.ClaimInstructor || submission.Type == DirectorySubmissionTypeEnum.ReportClosed))
                throw ApiErrorException.Field("fields", "This submission type has no fields.");
            if (hasFields && submission.TargetType == TargetTypeEnum.Venue)
            {
                VenueRequest fields = DirectorySubmissionFields.Venue(request.Fields, out HashSet<string> present);
                GeoRequest? fieldPin = DirectorySubmissionFields.TakePin(fields, present);
                if (fieldPin != null && request.Geo == null) request.Geo = fieldPin;
                VenueRequest merged = DirectorySubmissionFields.VenueRequestOf(submission.Fields, out HashSet<string> had);
                Venue? target = submission.TargetId == null ? null : await _Services.Database.DirectoryListings.Venues.ReadAsync(submission.TargetId, token).ConfigureAwait(false);
                Venue proposed = await _Services.Directory.Venues.BuildAsync(actor, fields, target ?? await BuildVenueAsync(actor, submission, merged, false, token).ConfigureAwait(false), present, submission.SubmitterUserId, false, token).ConfigureAwait(false);
                rows = DirectorySubmissionFields.Rows(proposed, present);
                if (target != null) DirectorySubmissionFields.FillCurrent(rows, DirectorySubmissionFields.Values(target));
            }
            else if (hasFields)
            {
                InstructorRequest fields = DirectorySubmissionFields.Instructor(request.Fields, out HashSet<string> present);
                Instructor? target = submission.TargetId == null ? null : await _Services.Database.DirectoryListings.Instructors.ReadAsync(submission.TargetId, token).ConfigureAwait(false);
                InstructorRequest merged = DirectorySubmissionFields.InstructorRequestOf(submission.Fields, out HashSet<string> had);
                Instructor baseline = target ?? await _Services.Directory.Instructors.BuildAsync(actor, merged, null, null, submission.SubmitterUserId, token).ConfigureAwait(false);
                Instructor proposed = await _Services.Directory.Instructors.BuildAsync(actor, fields, baseline, present, submission.SubmitterUserId, token).ConfigureAwait(false);
                List<string> venueIds = present.Contains("venueIds") ? await ReadableVenueIdsAsync(actor, fields.VenueIds, token).ConfigureAwait(false) : new List<string>();
                rows = DirectorySubmissionFields.Rows(proposed, venueIds, present);
                if (target != null) DirectorySubmissionFields.FillCurrent(rows, DirectorySubmissionFields.Values(target));
            }
            bool setGeo = request.Geo != null;
            if (setGeo && (request.Geo!.Lat != null || request.Geo.Lng != null))
            {
                if (request.Geo.Lat == null || request.Geo.Lat < -90 || request.Geo.Lat > 90) throw DirectoryFilters.InvalidGeo("geo.lat", "Latitude must be -90 to 90.");
                if (request.Geo.Lng == null || request.Geo.Lng < -180 || request.Geo.Lng > 180) throw DirectoryFilters.InvalidGeo("geo.lng", "Longitude must be -180 to 180.");
            }
            if (!await _Services.Database.DirectoryListings.Submissions.OverrideAsync(submission.Id, rows, setGeo, request.Geo?.Lat, request.Geo?.Lng, DateTime.UtcNow, token).ConfigureAwait(false))
                throw new ApiErrorException(409, "NotPending", "Only a pending submission can be changed.");
            return await AdminGetAsync(actor, submission.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/directory/submissions/{id}/approve (directory staff): applies the change in one transaction
        /// (create or update the entry, attach media, link venues, link the claimant, close the venue), audited as
        /// directory.submission.approve, and notifies the submitter.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="id">Submission.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The submission and the entry.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or GeocodeFailed; 403 Forbidden; 404 NotFound; 409 SubmissionStale, NotPending, AlreadyClaimed.</exception>
        public async Task<SubmissionApprovalView> ApproveAsync(ContentActor actor, RequestInfo info, string id, SubmissionApproveRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            DirectoryAccess.RequireStaff(actor);
            string? note = DirectoryText.Text(request.ReviewNote, 1000, "reviewNote", new Dictionary<string, string>());
            if (note != null && note.Length > 1000) throw ApiErrorException.Field("reviewNote", "At most 1000 characters.");
            DirectorySubmission submission = await _Services.Database.DirectoryListings.Submissions.ReadAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Submission");
            if (submission.Status != DirectorySubmissionStatusEnum.Pending) throw new ApiErrorException(409, "NotPending", "This submission is no longer pending.");
            if (request.Geo != null && (request.Geo.Lat == null || request.Geo.Lng == null || request.Geo.Lat < -90 || request.Geo.Lat > 90 || request.Geo.Lng < -180 || request.Geo.Lng > 180))
                throw DirectoryFilters.InvalidGeo("geo", "Latitude -90 to 90 and longitude -180 to 180 are required.");
            DateTime now = DateTime.UtcNow;
            List<string> apply = new List<string>();
            string? targetTable = null;
            string? resultId = submission.TargetId;
            Venue? venue = null;
            Instructor? instructor = null;
            using (Activity? activity = TrellisKitTelemetry.StartSpan("directory.submission.approve"))
            {
                activity?.SetTag("submission.id", submission.Id);
                switch (submission.Type)
                {
                    case DirectorySubmissionTypeEnum.NewVenue:
                    case DirectorySubmissionTypeEnum.EditVenue:
                        {
                            VenueRequest fields = DirectorySubmissionFields.VenueRequestOf(submission.Fields, out HashSet<string> present);
                            GeoRequest? pin = request.Geo ?? (submission.ProposedLat != null && submission.ProposedLng != null ? new GeoRequest { Lat = submission.ProposedLat, Lng = submission.ProposedLng } : null);
                            if (pin != null)
                            {
                                fields.Geo = pin;
                                present.Add("geo");
                            }
                            if (submission.Type == DirectorySubmissionTypeEnum.NewVenue)
                            {
                                fields.AddressVisibility ??= _Services.Settings.Directory.SubmissionDefaultAddressVisibility;
                                venue = await BuildVenueAsync(actor, submission, fields, true, token).ConfigureAwait(false);
                                venue.CreatedByUserId = submission.SubmitterUserId;
                                apply.AddRange(_Services.Database.DirectoryListings.Venues.CreateStatements(venue));
                            }
                            else
                            {
                                Venue existing = await _Services.Database.DirectoryListings.Venues.ReadAsync(submission.TargetId ?? String.Empty, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Venue");
                                venue = await _Services.Directory.Venues.BuildAsync(actor, fields, existing, present, submission.SubmitterUserId, true, token).ConfigureAwait(false);
                                apply.AddRange(_Services.Database.DirectoryListings.Venues.UpdateStatements(venue, existing.Slug));
                                targetTable = "venues";
                            }
                            resultId = venue.Id;
                            break;
                        }
                    case DirectorySubmissionTypeEnum.NewInstructor:
                    case DirectorySubmissionTypeEnum.EditInstructor:
                        {
                            InstructorRequest fields = DirectorySubmissionFields.InstructorRequestOf(submission.Fields, out HashSet<string> present);
                            Instructor? existing = submission.Type == DirectorySubmissionTypeEnum.EditInstructor
                                ? await _Services.Database.DirectoryListings.Instructors.ReadAsync(submission.TargetId ?? String.Empty, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Instructor")
                                : null;
                            instructor = await _Services.Directory.Instructors.BuildAsync(actor, fields, existing, existing == null ? null : present, submission.SubmitterUserId, token).ConfigureAwait(false);
                            if (existing == null)
                            {
                                instructor.CreatedByUserId = submission.SubmitterUserId;
                                apply.AddRange(_Services.Database.DirectoryListings.Instructors.CreateStatements(instructor));
                            }
                            else
                            {
                                apply.AddRange(_Services.Database.DirectoryListings.Instructors.UpdateStatements(instructor, existing.Slug));
                                targetTable = "instructors";
                            }
                            List<Venue> venues = await _Services.Database.DirectoryListings.Venues.ReadManyAsync(fields.VenueIds ?? new List<string>(), token).ConfigureAwait(false);
                            foreach (Venue linked in venues) apply.AddRange(_Services.Database.DirectoryListings.Venues.LinkStatements(linked.Id, instructor.Id, VenueInstructorRoleEnum.Regular, now));
                            resultId = instructor.Id;
                            break;
                        }
                    case DirectorySubmissionTypeEnum.ClaimInstructor:
                        {
                            instructor = await _Services.Database.DirectoryListings.Instructors.ReadAsync(submission.TargetId ?? String.Empty, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Instructor");
                            if (submission.SubmitterUserId == null) throw new ApiErrorException(409, "NotPending", "The claimant's account no longer exists.");
                            if (instructor.LinkedUserId != null) throw new ApiErrorException(409, "AlreadyClaimed", "This entry is already linked to an account.");
                            if (await _Services.Database.DirectoryListings.Instructors.ReadByLinkedUserAsync(submission.SubmitterUserId, token).ConfigureAwait(false) != null)
                                throw new ApiErrorException(409, "AlreadyClaimed", "The claimant is already linked to another entry.");
                            apply.Add(_Services.Database.DirectoryListings.Instructors.LinkUserStatement(instructor.Id, submission.SubmitterUserId, now));
                            break;
                        }
                    default:
                        {
                            venue = await _Services.Database.DirectoryListings.Venues.ReadAsync(submission.TargetId ?? String.Empty, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Venue");
                            apply.Add(_Services.Database.DirectoryListings.Venues.CloseStatement(venue.Id, "Reported closed" + (String.IsNullOrWhiteSpace(submission.Note) ? String.Empty : ": " + submission.Note), now));
                            break;
                        }
                }
                try
                {
                    await _Services.Database.DirectoryListings.Submissions.ApproveAsync(submission, apply, targetTable, actor.UserId, note, resultId, now, token).ConfigureAwait(false);
                }
                catch (DuplicateRecordException)
                {
                    TrellisKitTelemetry.Add(DirectoryTelemetry.Submissions, 1, "type", submission.Type.ToString(), "action", "approve", "outcome", "conflict");
                    if (submission.Type == DirectorySubmissionTypeEnum.ClaimInstructor) throw new ApiErrorException(409, "AlreadyClaimed", "The claimant is already linked to another entry.");
                    throw new ApiErrorException(409, "SlugInUse", "The slug of the new entry is in use; set another slug in a reviewer override.");
                }
                catch (ApiErrorException e) when (e.Error == "SubmissionStale")
                {
                    TrellisKitTelemetry.Add(DirectoryTelemetry.Submissions, 1, "type", submission.Type.ToString(), "action", "approve", "outcome", "stale");
                    throw;
                }
            }
            ContentActor submitter = await SubmitterActorAsync(submission, actor, token).ConfigureAwait(false);
            object? target = null;
            if (venue != null)
            {
                Venue stored = await _Services.Database.DirectoryListings.Venues.ReadAsync(venue.Id, token).ConfigureAwait(false) ?? venue;
                if (submission.Type != DirectorySubmissionTypeEnum.ReportClosed) await _Services.Directory.Venues.AfterSaveAsync(submitter, stored, null, null, token).ConfigureAwait(false);
                else await _Services.Directory.ChangedAsync(TargetTypeEnum.Venue, stored.Id, token).ConfigureAwait(false);
                target = await _Services.Directory.Views.VenueAsync(actor, stored, new DirectoryViewOptions { Admin = true, Detail = true }, token).ConfigureAwait(false);
            }
            else if (instructor != null)
            {
                Instructor stored = await _Services.Database.DirectoryListings.Instructors.ReadAsync(instructor.Id, token).ConfigureAwait(false) ?? instructor;
                if (submission.Type != DirectorySubmissionTypeEnum.ClaimInstructor) await _Services.Directory.Instructors.AfterSaveAsync(submitter, stored, null, null, token).ConfigureAwait(false);
                else
                {
                    await _Services.Directory.ChangedAsync(TargetTypeEnum.Instructor, stored.Id, token).ConfigureAwait(false);
                    if (submission.SubmitterUserId != null) await _Services.Directory.Instructors.NotifyLinkedAsync(stored, submission.SubmitterUserId, actor.UserId, token).ConfigureAwait(false);
                }
                target = await _Services.Directory.Views.InstructorAsync(actor, stored, new DirectoryViewOptions { Admin = true, Detail = true }, token).ConfigureAwait(false);
            }
            await CleanupMediaAsync(submission, true, token).ConfigureAwait(false);
            DirectorySubmission fresh = await _Services.Database.DirectoryListings.Submissions.ReadAsync(submission.Id, token).ConfigureAwait(false) ?? submission;
            await _Services.Audit.WriteAsync(info, "directory.submission.approve", submission.TargetType.ToString(), resultId ?? submission.Id, submission.Type + " " + submission.Id, token).ConfigureAwait(false);
            if (request.NotifySubmitter) await NotifyDecisionAsync(fresh, true, actor.UserId, token).ConfigureAwait(false);
            await RefreshPendingGaugeAsync(token).ConfigureAwait(false);
            TrellisKitTelemetry.Add(DirectoryTelemetry.Submissions, 1, "type", submission.Type.ToString(), "action", "approve", "outcome", "ok");
            return new SubmissionApprovalView { Submission = await ViewAsync(actor, fresh, true, token).ConfigureAwait(false), Target = target };
        }

        /// <summary>
        /// POST /api/v1/admin/directory/submissions/{id}/reject (directory staff): reviewNote required (3 to 1000);
        /// unattached media is deleted; audited as directory.submission.reject.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="id">Submission.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Submission.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound, 409 NotPending.</exception>
        public async Task<DirectorySubmissionView> RejectAsync(ContentActor actor, RequestInfo info, string id, SubmissionRejectRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            DirectoryAccess.RequireStaff(actor);
            string note = (request.ReviewNote ?? String.Empty).Trim();
            if (note.Length < 3 || note.Length > 1000) throw ApiErrorException.Field("reviewNote", "Required, 3 to 1000 characters.");
            DirectorySubmission submission = await _Services.Database.DirectoryListings.Submissions.ReadAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Submission");
            if (!await _Services.Database.DirectoryListings.Submissions.DecideAsync(submission.Id, DirectorySubmissionStatusEnum.Rejected, actor.UserId, note, DateTime.UtcNow, token).ConfigureAwait(false))
                throw new ApiErrorException(409, "NotPending", "This submission is no longer pending.");
            await CleanupMediaAsync(submission, false, token).ConfigureAwait(false);
            DirectorySubmission fresh = await _Services.Database.DirectoryListings.Submissions.ReadAsync(submission.Id, token).ConfigureAwait(false) ?? submission;
            await _Services.Audit.WriteAsync(info, "directory.submission.reject", submission.TargetType.ToString(), submission.TargetId ?? submission.Id, submission.Type + " " + submission.Id, token).ConfigureAwait(false);
            if (request.NotifySubmitter) await NotifyDecisionAsync(fresh, false, actor.UserId, token).ConfigureAwait(false);
            await RefreshPendingGaugeAsync(token).ConfigureAwait(false);
            TrellisKitTelemetry.Add(DirectoryTelemetry.Submissions, 1, "type", submission.Type.ToString(), "action", "reject", "outcome", "ok");
            return await ViewAsync(actor, fresh, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Create a ReportClosed submission from a PermanentlyClosed report (the engagement report hook calls this).
        /// Skipped silently when submissions are off, the venue is not active, or the reporter is at the pending limit.
        /// </summary>
        /// <param name="actor">Reporter.</param>
        /// <param name="venueId">Venue.</param>
        /// <param name="note">Report details, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The submission identifier, or null when skipped.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        public async Task<string?> FromClosedReportAsync(ContentActor actor, string venueId, string? note, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            try
            {
                string trimmed = (note ?? String.Empty).Trim();
                DirectorySubmissionView view = await SubmitAsync(actor, new DirectorySubmissionRequest
                {
                    Type = DirectorySubmissionTypeEnum.ReportClosed,
                    TargetId = venueId,
                    Note = trimmed.Length == 0 ? null : (trimmed.Length > 1000 ? trimmed.Substring(0, 1000) : trimmed)
                }, token).ConfigureAwait(false);
                return String.IsNullOrEmpty(view.Id) ? null : view.Id;
            }
            catch (ApiErrorException)
            {
                return null;
            }
        }

        /// <summary>
        /// One SubmissionDueWorker pass: announce newly overdue pending submissions to directory staff once
        /// (SubmissionOverdue).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Submissions announced.</returns>
        public async Task<int> OverduePassAsync(CancellationToken token = default)
        {
            List<DirectorySubmission> overdue = await _Services.Database.DirectoryListings.Submissions.ClaimOverdueAsync(DateTime.UtcNow, 50, token).ConfigureAwait(false);
            await RefreshPendingGaugeAsync(token).ConfigureAwait(false);
            if (overdue.Count == 0) return 0;
            List<string> staff = await StaffIdsAsync(token).ConfigureAwait(false);
            string title = "A directory submission is past the " + _Services.Settings.Directory.ReviewTargetHours.ToString(CultureInfo.InvariantCulture) + " hour review target";
            foreach (DirectorySubmission submission in overdue)
            {
                token.ThrowIfCancellationRequested();
                foreach (string userId in staff)
                {
                    await _Services.Notifications.CreateAsync(new NotificationDraft
                    {
                        RecipientUserId = userId,
                        Type = NotificationTypeEnum.SubmissionOverdue,
                        TargetType = TargetTypeEnum.DirectorySubmission,
                        TargetId = submission.Id,
                        TitleText = title,
                        Path = StaffPath
                    }, token).ConfigureAwait(false);
                }
            }
            return overdue.Count;
        }

        /// <summary>
        /// Account deletion: pending submissions are withdrawn (their media deleted) and every submission is anonymized.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Submissions anonymized.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        public async Task<int> EraseForAccountAsync(string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            string? after = null;
            while (true)
            {
                List<DirectorySubmission> pending = await _Services.Database.DirectoryListings.Submissions.ListByUserAsync(userId, true, after, 100, token).ConfigureAwait(false);
                if (pending.Count == 0) break;
                foreach (DirectorySubmission submission in pending)
                {
                    await _Services.Database.DirectoryListings.Submissions.DecideAsync(submission.Id, DirectorySubmissionStatusEnum.Withdrawn, null, null, DateTime.UtcNow, token).ConfigureAwait(false);
                    await CleanupMediaAsync(submission, false, token).ConfigureAwait(false);
                }
                after = pending[pending.Count - 1].Id;
            }
            int anonymized = await _Services.Database.DirectoryListings.Submissions.AnonymizeAsync(userId, DateTime.UtcNow, token).ConfigureAwait(false);
            await RefreshPendingGaugeAsync(token).ConfigureAwait(false);
            return anonymized;
        }

        #endregion

        #region Private-Methods

        private int DailyLimit(ContentActor actor)
        {
            int hours = _Services.Settings.Comments.NewAccountHours;
            if (hours <= 0 || actor.User == null) return _Services.Settings.Directory.SubmissionsPerDay;
            bool isNew = DateTime.UtcNow - actor.User.CreatedUtc < TimeSpan.FromHours(hours);
            return isNew ? _Services.Settings.Directory.NewAccountSubmissionsPerDay : _Services.Settings.Directory.SubmissionsPerDay;
        }

        private async Task<Venue> RequireReadableVenueAsync(ContentActor actor, string? id, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(id)) throw ApiErrorException.Field("targetId", "Required.");
            Venue venue = await _Services.Directory.Venues.FindAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Venue");
            if (DirectoryViewBuilder.Level(actor, venue, new DirectoryViewOptions()) != 2) throw ApiErrorException.NotFound("Venue");
            return venue;
        }

        private async Task<Instructor> RequireReadableInstructorAsync(ContentActor actor, string? id, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(id)) throw ApiErrorException.Field("targetId", "Required.");
            Instructor instructor = await _Services.Directory.Instructors.FindAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Instructor");
            if (DirectoryViewBuilder.Level(actor, instructor, new DirectoryViewOptions()) != 2) throw ApiErrorException.NotFound("Instructor");
            return instructor;
        }

        private async Task<List<string>> ReadableVenueIdsAsync(ContentActor actor, List<string>? ids, CancellationToken token)
        {
            List<string> list = (ids ?? new List<string>()).Where(i => !String.IsNullOrWhiteSpace(i)).Select(i => i.Trim()).Distinct(StringComparer.Ordinal).Take(20).ToList();
            if (list.Count == 0) return list;
            List<Venue> venues = await _Services.Database.DirectoryListings.Venues.ReadManyAsync(list, token).ConfigureAwait(false);
            Dictionary<string, Venue> byId = venues.ToDictionary(v => v.Id, StringComparer.Ordinal);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            for (int i = 0; i < list.Count; i++)
                if (!byId.TryGetValue(list[i], out Venue? v) || DirectoryViewBuilder.Level(actor, v, new DirectoryViewOptions()) != 2) errors["fields.venueIds[" + i + "]"] = "Not found.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            return list;
        }

        private Task<Venue> BuildVenueAsync(ContentActor actor, DirectorySubmission submission, VenueRequest fields, bool geocodeInline, CancellationToken token)
        {
            return _Services.Directory.Venues.BuildAsync(actor, fields, null, null, submission.SubmitterUserId, geocodeInline, token);
        }

        private async Task<List<Venue>> VenueDuplicatesAsync(Venue proposed, CancellationToken token)
        {
            DirectorySettings settings = _Services.Settings.Directory;
            GeoBox? box = null;
            if (proposed.Lat != null && proposed.Lng != null)
                box = GeoMath.NearBoxes(new GeoNear { Lat = proposed.Lat.Value, Lng = proposed.Lng.Value, RadiusKm = Math.Max(0.1, settings.DuplicateDistanceKm) }).First();
            List<Venue> candidates = await _Services.Database.DirectoryListings.Venues.DuplicateCandidatesAsync(DirectoryText.Words(proposed.Name), box, 200, token).ConfigureAwait(false);
            return candidates
                .Where(c => c.Id != proposed.Id)
                .Select(c => new { Venue = c, Score = DirectoryText.Similarity(proposed.Name, c.Name) })
                .Where(x => x.Score >= settings.DuplicateNameSimilarity && Near(proposed, x.Venue, settings.DuplicateDistanceKm))
                .OrderByDescending(x => x.Score).ThenBy(x => x.Venue.Id, StringComparer.Ordinal)
                .Take(MaxDuplicates).Select(x => x.Venue).ToList();
        }

        private static bool Near(Venue a, Venue b, double km)
        {
            if (a.Lat != null && a.Lng != null && b.Lat != null && b.Lng != null) return GeoMath.HaversineKm(a.Lat.Value, a.Lng.Value, b.Lat.Value, b.Lng.Value) <= km;
            if (!String.IsNullOrWhiteSpace(a.Locality) && !String.IsNullOrWhiteSpace(b.Locality)) return String.Equals(a.Locality.Trim(), b.Locality.Trim(), StringComparison.OrdinalIgnoreCase);
            return true;
        }

        private async Task<List<Instructor>> InstructorDuplicatesAsync(Instructor proposed, CancellationToken token)
        {
            List<string> words = DirectoryText.Words(proposed.DisplayName);
            if (words.Count == 0) return new List<Instructor>();
            List<Instructor> candidates = await _Services.Database.DirectoryListings.Instructors.DuplicateCandidatesAsync(words, 200, token).ConfigureAwait(false);
            double threshold = _Services.Settings.Directory.DuplicateNameSimilarity;
            return candidates
                .Where(c => c.Id != proposed.Id)
                .Select(c => new { Instructor = c, Score = DirectoryText.Similarity(proposed.DisplayName, c.DisplayName) })
                .Where(x => x.Score >= threshold)
                .OrderByDescending(x => x.Score).ThenBy(x => x.Instructor.Id, StringComparer.Ordinal)
                .Take(MaxDuplicates).Select(x => x.Instructor).ToList();
        }

        private async Task<BoundedPreview<object>> DuplicatesOfAsync(ContentActor actor, DirectorySubmission submission, CancellationToken token)
        {
            List<object> objects = new List<object>();
            if (submission.TargetType == TargetTypeEnum.Venue)
            {
                VenueRequest fields = DirectorySubmissionFields.VenueRequestOf(submission.Fields, out HashSet<string> _);
                Venue probe = new Venue { Id = String.Empty, Name = fields.Name ?? submission.TargetName ?? String.Empty, Locality = fields.Address?.Locality, Lat = submission.ProposedLat, Lng = submission.ProposedLng };
                objects.AddRange(await _Services.Directory.Views.VenueSummariesAsync(actor, await VenueDuplicatesAsync(probe, token).ConfigureAwait(false), new DirectoryViewOptions(), token).ConfigureAwait(false));
            }
            else
            {
                InstructorRequest fields = DirectorySubmissionFields.InstructorRequestOf(submission.Fields, out HashSet<string> _);
                Instructor probe = new Instructor { Id = String.Empty, DisplayName = fields.DisplayName ?? submission.TargetName ?? String.Empty };
                objects.AddRange(await _Services.Directory.Views.InstructorSummariesAsync(actor, await InstructorDuplicatesAsync(probe, token).ConfigureAwait(false), new DirectoryViewOptions(), token).ConfigureAwait(false));
            }
            return new BoundedPreview<object> { Objects = objects, TotalRecords = objects.Count };
        }

        private async Task<DirectorySubmission> RefreshAsync(DirectorySubmission submission, CancellationToken token)
        {
            if (submission.TargetId == null) return submission;
            Dictionary<string, List<string?>>? current = null;
            DateTime? updated = null;
            if (submission.Type == DirectorySubmissionTypeEnum.EditVenue)
            {
                Venue? venue = await _Services.Database.DirectoryListings.Venues.ReadAsync(submission.TargetId, token).ConfigureAwait(false);
                if (venue == null) return submission;
                current = DirectorySubmissionFields.Values(venue);
                updated = venue.UpdatedUtc;
            }
            else if (submission.Type == DirectorySubmissionTypeEnum.EditInstructor)
            {
                Instructor? instructor = await _Services.Database.DirectoryListings.Instructors.ReadAsync(submission.TargetId, token).ConfigureAwait(false);
                if (instructor == null) return submission;
                current = DirectorySubmissionFields.Values(instructor);
                current["venueIds"] = (await _Services.Database.DirectoryListings.Instructors.VenueIdsAsync(instructor.Id, 50, token).ConfigureAwait(false)).Cast<string?>().ToList();
                updated = instructor.UpdatedUtc;
            }
            if (current == null) return submission;
            DirectorySubmissionFields.FillCurrent(submission.Fields, current);
            await _Services.Database.DirectoryListings.Submissions.RefreshAsync(submission.Id, submission.Fields, updated, token).ConfigureAwait(false);
            submission.TargetUpdatedUtc = updated;
            return submission;
        }

        private async Task CleanupMediaAsync(DirectorySubmission submission, bool approved, CancellationToken token)
        {
            if (submission.SubmitterUserId == null && !approved) return;
            List<string> ids = submission.Fields.Where(f => (f.Field == "photoMediaIds" || f.Field == "photoMediaId") && f.ProposedValue != null).Select(f => f.ProposedValue!).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return;
            foreach (MediaAttachment media in await _Services.Database.Media.ReadManyAsync(ids, token).ConfigureAwait(false))
            {
                if (media.IsDeleted || media.IsAttached) continue;
                if (submission.SubmitterUserId != null && media.OwnerUserId != submission.SubmitterUserId) continue;
                await _Services.Media.DeleteMediaAsync(media, token).ConfigureAwait(false);
            }
        }

        private async Task<ContentActor> SubmitterActorAsync(DirectorySubmission submission, ContentActor reviewer, CancellationToken token)
        {
            if (submission.SubmitterUserId == null) return reviewer;
            try
            {
                ContentActor submitter = await _Services.Actors.ForUserAsync(submission.SubmitterUserId, token).ConfigureAwait(false);
                return reviewer.IsSystemAdmin ? reviewer : submitter;
            }
            catch (ApiErrorException)
            {
                return reviewer;
            }
        }

        private async Task NotifyStaffAsync(ContentActor actor, DirectorySubmission submission, CancellationToken token)
        {
            string name = actor.User?.DisplayName ?? "A member";
            await _Services.Email.NotifyStaffAsync(StaffDirectorySubmissionEmail.TemplateKey, "directory_submission:" + submission.Id, token,
                ("submissionType", submission.Type.ToString()), ("targetName", submission.TargetName ?? String.Empty), ("submitterName", name),
                ("adminLink", _Services.Email.Link(StaffPath)), (StaffEmailDetails.Token, await StaffDetailsAsync(actor, submission, token).ConfigureAwait(false))).ConfigureAwait(false);
        }

        private async Task<string> StaffDetailsAsync(ContentActor actor, DirectorySubmission submission, CancellationToken token)
        {
            // Staff email details (docs/EMAIL.md): the submission, the submitter with their 90-day history, and the request.
            try
            {
                StaffEmailDetailsService builder = _Services.StaffEmailDetails;
                StaffEmailSection section = new StaffEmailSection("Submission")
                    .Row("Type", submission.Type.ToString())
                    .Row("Target", submission.TargetName)
                    .Row("Target id", submission.TargetId)
                    .Row("Note", StaffEmailDetailsService.Excerpt(submission.Note, 300))
                    .Row("Proposed map point", submission.ProposedLat.HasValue && submission.ProposedLng.HasValue
                        ? submission.ProposedLat.Value.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture) + ", " + submission.ProposedLng.Value.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture) : null);
                foreach (DirectorySubmissionField field in submission.Fields.OrderBy(f => f.Position).Take(12))
                    section.Row(field.Field, StaffEmailDetailsService.Excerpt(field.ProposedValue, 200));
                StaffEmailSection? submitter = await builder.MemberAsync(submission.SubmitterUserId, "Submitter", token).ConfigureAwait(false);
                if (submitter != null)
                    submitter.Row("Submissions in 90 days", StaffEmailDetailsService.History(await _Services.Database.DirectoryListings.Submissions.StatusCountsByUserSinceAsync(submission.SubmitterUserId!, builder.Clock().AddDays(-90), token).ConfigureAwait(false), "Approved"));
                StaffEmailDetails sections = new StaffEmailDetails();
                sections.Add(section);
                sections.Add(submitter);
                sections.Add(await builder.RequestAsync(actor.Request, token).ConfigureAwait(false));
                return sections.Serialize();
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Services.Logger.Warn("[DirectorySubmissionService] staff details for " + submission.Id + " unavailable: " + e.Message);
                return String.Empty;
            }
        }

        private async Task NotifyDecisionAsync(DirectorySubmission submission, bool approved, string? reviewerUserId, CancellationToken token)
        {
            if (submission.SubmitterUserId == null) return;
            string target = submission.TargetName ?? "a directory entry";
            string what = Describe(submission.Type, target);
            string title = "Your " + what + (approved ? " was approved" : " was not approved");
            string? path = approved && submission.ResultId != null ? (submission.TargetType == TargetTypeEnum.Instructor ? "/instructors/" : "/venues/") + submission.ResultId : "/me/directory/submissions";
            await _Services.Notifications.CreateAsync(new NotificationDraft
            {
                RecipientUserId = submission.SubmitterUserId,
                Type = NotificationTypeEnum.SubmissionDecision,
                ActorUserId = reviewerUserId == submission.SubmitterUserId ? null : reviewerUserId,
                TargetType = TargetTypeEnum.DirectorySubmission,
                TargetId = submission.Id,
                TitleText = title,
                Body = submission.ReviewerNote,
                Path = path
            }, token).ConfigureAwait(false);
            User? user = await _Services.Database.Users.ReadAsync(submission.SubmitterUserId, token).ConfigureAwait(false);
            await _Services.Email.SendToMemberAsync(DirectorySubmissionDecisionEmail.TemplateKey, user, "directory_decision:" + submission.Id, token,
                ("targetName", target), ("decision", approved ? "approved" : "not approved"), ("reviewNote", submission.ReviewerNote ?? String.Empty), ("link", _Services.Email.MemberLink(path))).ConfigureAwait(false);
        }

        private static string Describe(DirectorySubmissionTypeEnum type, string target)
        {
            switch (type)
            {
                case DirectorySubmissionTypeEnum.NewVenue:
                case DirectorySubmissionTypeEnum.NewInstructor:
                    return "suggestion of " + target;
                case DirectorySubmissionTypeEnum.ClaimInstructor:
                    return "claim of " + target;
                case DirectorySubmissionTypeEnum.ReportClosed:
                    return "closure report for " + target;
                default:
                    return "suggested edit to " + target;
            }
        }

        private async Task<List<string>> StaffIdsAsync(CancellationToken token)
        {
            List<string> ids = new List<string>();
            foreach (RoleEnum role in new[] { RoleEnum.DirectoryModerator, RoleEnum.SystemAdmin })
                ids.AddRange((await _Services.Database.RoleAssignments.EnumerateByRoleAsync(role, 50, token).ConfigureAwait(false)).Select(r => r.UserId));
            return ids.Distinct(StringComparer.Ordinal).Take(50).ToList();
        }

        private async Task RefreshPendingGaugeAsync(CancellationToken token)
        {
            long pending = await _Services.Database.DirectoryListings.Submissions.CountPendingAsync(token).ConfigureAwait(false);
            DirectoryTelemetry.SetPending(_Services.PropertyId, pending);
        }

        private static DirectorySubmissionFilter Filter(EnumerationQuery q, bool admin)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            DirectorySubmissionFilter filter = new DirectorySubmissionFilter
            {
                Statuses = DirectoryFilters.Enums<DirectorySubmissionStatusEnum>(q, "status", errors),
                Types = DirectoryFilters.Enums<DirectorySubmissionTypeEnum>(q, "type", errors)
            };
            if (admin)
            {
                if (!q.HasFilter("status")) filter.Statuses = new List<DirectorySubmissionStatusEnum> { DirectorySubmissionStatusEnum.Pending };
                filter.SubmitterUserId = q.FilterString("submitterUserId");
                filter.TargetId = q.FilterString("targetId");
            }
            DirectoryFilters.ThrowIfAny(errors);
            return filter;
        }

        private async Task<List<DirectorySubmissionView>> ViewsAsync(ContentActor actor, IReadOnlyList<DirectorySubmission> submissions, bool staff, CancellationToken token)
        {
            List<DirectorySubmissionView> result = new List<DirectorySubmissionView>();
            if (submissions.Count == 0) return result;
            Dictionary<string, UserSummary> users = await _Services.Summaries.BuildAsync(submissions.Select(s => s.SubmitterUserId).Concat(staff ? submissions.Select(s => s.ReviewerUserId) : Enumerable.Empty<string?>()), token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.DirectorySubmission, submissions.Select(s => s.Id), staff, token).ConfigureAwait(false);
            List<string> instructorIds = submissions.Where(s => s.Type == DirectorySubmissionTypeEnum.EditInstructor && s.TargetId != null).Select(s => s.TargetId!).Distinct(StringComparer.Ordinal).ToList();
            Dictionary<string, Instructor> instructors = instructorIds.Count == 0 ? new Dictionary<string, Instructor>(StringComparer.Ordinal) :
                (await _Services.Database.DirectoryListings.Instructors.ReadManyAsync(instructorIds, token).ConfigureAwait(false)).ToDictionary(i => i.Id, StringComparer.Ordinal);
            DateTime now = DateTime.UtcNow;
            foreach (DirectorySubmission s in submissions)
            {
                metadata.TryGetValue(s.Id, out ObjectMetadata? m);
                DirectorySubmissionView view = new DirectorySubmissionView
                {
                    Id = s.Id,
                    Type = s.Type,
                    Status = s.Status,
                    Target = new SubmissionTargetView { Type = s.TargetType, Id = s.TargetId ?? s.ResultId, Name = s.TargetName },
                    Submitter = s.SubmitterUserId == null ? UserSummary.Deleted() : (users.TryGetValue(s.SubmitterUserId, out UserSummary? u) ? u : UserSummary.Deleted()),
                    Note = s.Note,
                    Changes = DirectorySubmissionFields.Changes(s),
                    ProposedGeo = s.ProposedLat != null && s.ProposedLng != null ? new GeoView { Lat = GeoMath.Round(s.ProposedLat.Value, 6), Lng = GeoMath.Round(s.ProposedLng.Value, 6), Precision = GeoPrecisionEnum.Exact } : null,
                    IsLinkedMember = s.Type == DirectorySubmissionTypeEnum.EditInstructor && s.TargetId != null && instructors.TryGetValue(s.TargetId, out Instructor? ins) && ins.LinkedUserId != null && ins.LinkedUserId == s.SubmitterUserId,
                    IsOverdue = s.Status == DirectorySubmissionStatusEnum.Pending && s.DueUtc <= now,
                    DueUtc = s.DueUtc,
                    Reviewer = staff && s.ReviewerUserId != null && users.TryGetValue(s.ReviewerUserId, out UserSummary? r) ? r : null,
                    ReviewNote = s.ReviewerNote,
                    DecidedUtc = s.DecidedUtc,
                    Labels = m?.Labels ?? new List<string>(),
                    Tags = m?.Tags ?? new Dictionary<string, string>(),
                    CreatedUtc = s.CreatedUtc,
                    UpdatedUtc = s.UpdatedUtc
                };
                result.Add(view);
            }
            return result;
        }

        private async Task<DirectorySubmissionView> ViewAsync(ContentActor actor, DirectorySubmission submission, bool staff, CancellationToken token)
        {
            List<DirectorySubmissionView> views = await ViewsAsync(actor, new[] { submission }, staff, token).ConfigureAwait(false);
            return views[0];
        }

        #endregion
    }
}
