namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Metrics;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Directory admin routes (docs/PHASE4_CONTRACT.md section 3.9): venues, instructors, links, the submission review
    /// queue, import, regeocoding, export, metrics, programs, and settings. Directory staff (SystemAdmin or
    /// DirectoryModerator) unless noted; programs and settings are SystemAdmin only.
    /// </summary>
    public class AdminDirectoryRoutes
    {
        #region Private-Members

        private const string _Tag = "Admin directory";
        private const string _Venues = Constants.ApiPrefix + "/admin/venues";
        private const string _Instructors = Constants.ApiPrefix + "/admin/instructors";
        private const string _Base = Constants.ApiPrefix + "/admin/directory";
        private const string _AdminVenue = "AdminVenue (Venue plus openReportCount, pendingSubmissionCount, geoSource, geocodedUtc, needsPin, moderationState, closedReason, closedUtc; exact address and pin)";
        private const string _AdminInstructor = "AdminInstructor (Instructor plus openReportCount, pendingSubmissionCount, moderationState, linkedUtc)";
        private const string _Submission = "DirectorySubmission (with reviewer)";
        private const string _Staff = " Directory staff (SystemAdmin or DirectoryModerator).";
        private const string _Admin = " SystemAdmin.";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public AdminDirectoryRoutes(PropertyDirectory directory)
        {
            _Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the routes.
        /// </summary>
        /// <param name="server">Webserver.</param>
        /// <exception cref="ArgumentNullException">The server is null.</exception>
        public void Register(Webserver server)
        {
            ArgumentNullException.ThrowIfNull(server);
            WatsonWebserver.Core.Routing.RoutingGroup post = server.Routes.PostAuthentication;
            RegisterVenues(post);
            RegisterInstructors(post);
            RegisterSubmissions(post);
            RegisterOther(post);
        }

        #endregion

        #region Private-Methods

        private void RegisterVenues(WatsonWebserver.Core.Routing.RoutingGroup post)
        {
            EnumerationRoutes.Add(post, _Venues, DirectoryCollections.AdminVenues, _AdminVenue,
                () => Doc("List venues (admin)", _Tag, "Every status (filters.status Active, Closed, Archived), exact addresses, staff counters. Member filters plus geoSource (None finds venues that need a pin) and verified. Exact totals." + _Staff),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Directory.Venues.AdminListAsync(actor, query, ctx.Token)));
            post.Static.Add(HttpMethod.POST, _Venues, CreateVenueAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Create a venue", _Tag, "Body: the venue fields (name, slug?, affiliation, summary, description, address { line1, line2, locality, region, postalCode, countryCode }, addressVisibility (default Exact), geo { lat, lng } (a manual pin), geocode (true: geocode now and fail with GeocodeFailed when nothing matches), timeZone, phone, email, websiteUrl, socialLinks [{ kind, url }], scheduleText, programs, visitorPolicy, visitorPolicyNote, photoMediaIds (Ready images), audience (Public only by SystemAdmin), labels, tags). " +
                    "Without a pin the address is geocoded when a geocoder is configured; a provider failure leaves geo.source None (the venue needs a pin). Audited as venue.create." + _Staff)
                    .Body("VenueRequest").Returns(_AdminVenue, 201).Resp(400, "ValidationFailed, InvalidGeo, UnknownOption, ContentFiltered, or GeocodeFailed").Resp(403, "Forbidden").Resp(409, "SlugInUse or MediaNotAttachable").Resp(503, "IntegrationNotConfigured (geocode requested)"));
            post.Parameter.Add(HttpMethod.GET, _Venues + "/{id}", GetVenueAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a venue (admin)", _Tag, "Every status." + _Staff).PathParam("id", "Venue identifier or slug").Returns(_AdminVenue).Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, _Venues + "/{id}", UpdateVenueAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Edit a venue", _Tag, "Any create field; omitted fields stay. A new slug keeps the old one as a redirect. An address change geocodes again (replacing a manual pin) unless geo is sent. Audited as venue.update." + _Staff)
                    .PathParam("id", "Venue identifier").Body("VenueRequest").Returns(_AdminVenue).Resp(400, "ValidationFailed, InvalidGeo, UnknownOption, ContentFiltered, or GeocodeFailed").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "SlugInUse"));
            post.Parameter.Add(HttpMethod.DELETE, _Venues + "/{id}", DeleteVenueAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete a venue", _Tag, "Soft delete; instructor links removed. Audited as venue.delete." + _Staff).PathParam("id", "Venue identifier").Resp(204, "Deleted").Resp(403, "Forbidden").Resp(404, "NotFound"));
            AddVenueAction(post, "/verify", "Verify a venue", "Body { note? }. Audited as venue.verify.", ctx => VerifyAsync(ctx, true));
            AddVenueAction(post, "/unverify", "Unverify a venue", "Body { note? }. Audited as venue.unverify.", ctx => VerifyAsync(ctx, false));
            AddVenueAction(post, "/close", "Close a venue", "Body { reason? }. Permanently closed: kept for history, hidden from lists unless filters.includeClosed. Audited as venue.close.", ctx => StatusAsync(ctx, VenueStatusEnum.Closed));
            AddVenueAction(post, "/reopen", "Reopen a venue", "Body { reason? }. Audited as venue.close (reopened).", ctx => StatusAsync(ctx, VenueStatusEnum.Active));
            AddVenueAction(post, "/archive", "Archive a venue", "Body { reason? }. Archived venues are staff only. Audited as venue.archive.", ctx => StatusAsync(ctx, VenueStatusEnum.Archived));
            AddVenueAction(post, "/unarchive", "Unarchive a venue", "Body { reason? }. Audited as venue.archive (unarchived).", ctx => StatusAsync(ctx, VenueStatusEnum.Active));
            post.Parameter.Add(HttpMethod.PUT, _Venues + "/{id}/instructors/{instructorId}", LinkInstructorAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Link an instructor to a venue", _Tag, "Body { role?: Head | Regular | Guest } (default Regular); an existing link takes the new role." + _Staff)
                    .PathParam("id", "Venue identifier").PathParam("instructorId", "Instructor identifier").Body("VenueInstructorRequest", false).Returns("InstructorSummary (with role)").Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.DELETE, _Venues + "/{id}/instructors/{instructorId}", UnlinkInstructorAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Unlink an instructor from a venue", _Tag, "Removes the link." + _Staff)
                    .PathParam("id", "Venue identifier").PathParam("instructorId", "Instructor identifier").Resp(204, "Unlinked").Resp(403, "Forbidden").Resp(404, "NotFound"));
        }

        private void RegisterInstructors(WatsonWebserver.Core.Routing.RoutingGroup post)
        {
            EnumerationRoutes.Add(post, _Instructors, DirectoryCollections.AdminInstructors, _AdminInstructor,
                () => Doc("List instructors (admin)", _Tag, "Active and Archived (filters.status). Filters venueId, rank, claimed, linkedUserId, audience. Exact totals." + _Staff),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Directory.Instructors.AdminListAsync(actor, query, ctx.Token)));
            post.Static.Add(HttpMethod.POST, _Instructors, CreateInstructorAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Create an instructor", _Tag, "Body { displayName, slug?, bio, photoMediaId, rank, rankLevel (options of the configured Rank field), affiliation, websiteUrl, socialLinks, venueIds (linked as Regular), audience, labels, tags }. Audited as instructor.create." + _Staff)
                    .Body("InstructorRequest").Returns(_AdminInstructor, 201).Resp(400, "ValidationFailed, UnknownOption, or ContentFiltered").Resp(403, "Forbidden").Resp(404, "NotFound (a venue)").Resp(409, "SlugInUse"));
            post.Parameter.Add(HttpMethod.GET, _Instructors + "/{id}", GetInstructorAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read an instructor (admin)", _Tag, "Every status." + _Staff).PathParam("id", "Instructor identifier or slug").Returns(_AdminInstructor).Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, _Instructors + "/{id}", UpdateInstructorAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Edit an instructor", _Tag, "Any create field except venueIds; omitted fields stay. Audited as instructor.update." + _Staff)
                    .PathParam("id", "Instructor identifier").Body("InstructorRequest").Returns(_AdminInstructor).Resp(400, "ValidationFailed, UnknownOption, or ContentFiltered").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "SlugInUse"));
            post.Parameter.Add(HttpMethod.DELETE, _Instructors + "/{id}", DeleteInstructorAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete an instructor", _Tag, "Soft delete; venue links and the member link removed. Audited as instructor.delete." + _Staff).PathParam("id", "Instructor identifier").Resp(204, "Deleted").Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Instructors + "/{id}/link", LinkUserAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Link an instructor to a member", _Tag, "Body { userId }. The member gets InstructorLinked with an unlink action (POST /api/v1/me/instructor-links/{instructorId}/unlink). One entry per member. Audited as instructor.link." + _Staff)
                    .PathParam("id", "Instructor identifier").Body("InstructorLinkRequest").Returns(_AdminInstructor).Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "AlreadyClaimed"));
            AddInstructorAction(post, "/unlink", "Unlink an instructor from its member", "Audited as instructor.unlink.", (ctx, actor, info) => _Directory.For(ctx).Directory.Instructors.UnlinkAsync(actor, info, RouteHelper.Param(ctx, "id"), ctx.Token));
            AddInstructorAction(post, "/archive", "Archive an instructor", "Archived instructors are staff only. Audited as instructor.update.", (ctx, actor, info) => _Directory.For(ctx).Directory.Instructors.SetArchivedAsync(actor, info, RouteHelper.Param(ctx, "id"), true, ctx.Token));
            AddInstructorAction(post, "/unarchive", "Unarchive an instructor", "Audited as instructor.update.", (ctx, actor, info) => _Directory.For(ctx).Directory.Instructors.SetArchivedAsync(actor, info, RouteHelper.Param(ctx, "id"), false, ctx.Token));
        }

        private void RegisterSubmissions(WatsonWebserver.Core.Routing.RoutingGroup post)
        {
            string submissions = _Base + "/submissions";
            EnumerationRoutes.Add(post, submissions, DirectoryCollections.AdminSubmissions, _Submission,
                () => Doc("List directory submissions (review queue)", _Tag, "Filters status (default Pending), type, submitterUserId, targetId. Orderings CreatedAscending (default: oldest first, so overdue submissions lead) and CreatedDescending. Items carry isOverdue and the isLinkedMember fast-track badge." + _Staff),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Directory.Submissions.AdminListAsync(actor, query, ctx.Token)));
            post.Parameter.Add(HttpMethod.GET, submissions + "/{id}", GetSubmissionAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a directory submission", _Tag, "With a fresh changes diff against the current target (loading it is what approval's stale check compares against) and the possible duplicates of new entries." + _Staff)
                    .PathParam("id", "Submission identifier").Returns(_Submission).Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, submissions + "/{id}", OverrideAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Override a directory submission", _Tag, "Body { fields { ... }, geo? { lat, lng } }: reviewer overrides before approval (for example fix the pin or the name)." + _Staff)
                    .PathParam("id", "Submission identifier").Body("SubmissionOverrideRequest").Returns(_Submission).Resp(400, "ValidationFailed or InvalidGeo").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "NotPending"));
            post.Parameter.Add(HttpMethod.POST, submissions + "/{id}/approve", ApproveAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Approve a directory submission", _Tag, "Body { reviewNote?, notifySubmitter (default true), geo? { lat, lng } }. Applies the change in one transaction (create the entry or update the changed fields, attach approved media, link venues, link the claimant, close the venue). Answers { submission, target }. The submitter gets SubmissionDecision and the directory_submission_decision email. Audited as directory.submission.approve." + _Staff)
                    .PathParam("id", "Submission identifier").Body("SubmissionApproveRequest", false).Returns("{ submission, target }").Resp(400, "ValidationFailed, InvalidGeo, or GeocodeFailed").Resp(403, "Forbidden").Resp(404, "NotFound")
                    .Resp(409, "SubmissionStale (the target changed since the diff was loaded; reload), NotPending, AlreadyClaimed, or SlugInUse"));
            post.Parameter.Add(HttpMethod.POST, submissions + "/{id}/reject", RejectAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Reject a directory submission", _Tag, "Body { reviewNote (required, 3 to 1000), notifySubmitter (default true) }. Unattached submission media is deleted. Audited as directory.submission.reject." + _Staff)
                    .PathParam("id", "Submission identifier").Body("SubmissionRejectRequest").Returns(_Submission).Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "NotPending"));
        }

        private void RegisterOther(WatsonWebserver.Core.Routing.RoutingGroup post)
        {
            post.Static.Add(HttpMethod.POST, _Base + "/import", ImportAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Import venues", _Tag, "Body { rows [VenueRequest] (at most 1000; a row whose slug matches a venue updates it), dryRun (default true) }. Answers { dryRun, created, updated, skipped, errors { objects [{ row, field, message }] (at most 100), totalRecords } }. Geocoding runs in the background (GeocodeWorker). Audited as directory.import." + _Staff)
                    .Body("DirectoryImportRequest").Returns("DirectoryImportResponse").Resp(400, "ValidationFailed").Resp(403, "Forbidden"));
            post.Static.Add(HttpMethod.POST, _Base + "/regeocode", RegeocodeAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Regeocode venues", _Tag, "Body { venueIds? (at most 500; omitted means every venue), onlyMissing (default true) }. Queues a GeocodeWorker job; manual pins are kept unless the address changes. Answers 202 { queued }." + _Staff)
                    .Body("RegeocodeRequest", false).Returns("{ queued }", 202).Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(503, "IntegrationNotConfigured"));
            post.Static.Add(HttpMethod.GET, _Base + "/export.csv", ExportAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Export the directory", _Tag, "CSV of every live venue and instructor (all statuses), streamed one page at a time." + _Staff).File("text/csv", "Directory CSV").Resp(403, "Forbidden"));
            post.Static.Add(HttpMethod.GET, _Base + "/metrics", MetricsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Directory metrics", _Tag, "Query days (7 to 365, default 30). { activeVenues, closedVenues, archivedVenues, verifiedVenues, needsPin, instructors, claimedInstructors, pendingSubmissions, overdueSubmissions, submissionsLast30Days, approvedLast30Days, days, submissionsByDay, approvalsByDay, venuesCreatedByDay }; each daily series has one { date, count } per day of the window." + _Staff)
                    .QueryParam("days", "Window in days (7 to 365, default 30)").Enumeration("LegacyMetricRow", "CreatedDescending, CreatedAscending", "days, series", true).Resp(400, "ValidationFailed").Resp(403, "Forbidden"));
            post.Static.Add(HttpMethod.POST, _Base + "/metrics/enumerate", MetricsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Directory metrics", _Tag, "Query days (7 to 365, default 30). { activeVenues, closedVenues, archivedVenues, verifiedVenues, needsPin, instructors, claimedInstructors, pendingSubmissions, overdueSubmissions, submissionsLast30Days, approvedLast30Days, days, submissionsByDay, approvalsByDay, venuesCreatedByDay }; each daily series has one { date, count } per day of the window." + _Staff)
                    .QueryParam("days", "Window in days (7 to 365, default 30)").Body("EnumerationQuery", false).Enumeration("LegacyMetricRow", "CreatedDescending, CreatedAscending", "days, series", true).Resp(400, "ValidationFailed").Resp(403, "Forbidden"));
            EnumerationRoutes.Add(post, _Base + "/programs", DirectoryCollections.AdminPrograms, "DirectoryProgram { id, key, label, sortOrder, isRetired, labels, tags, createdUtc, updatedUtc }",
                () => Doc("List directory programs (admin)", _Tag, "Every program including retired ones (filters.includeRetired false hides them), in admin order (SortOrderAscending default; also LabelAscending). Exact totals." + _Staff),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Directory.Config.AdminEnumerateProgramsAsync(actor, query, ctx.Token)));
            post.Static.Add(HttpMethod.POST, _Base + "/programs", CreateProgramAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Create a directory program", _Tag, "Body { key (stable), label, labels, tags }. At most directory.maxPrograms. Audited as directory.settings.update." + _Admin)
                    .Body("DirectoryProgramRequest").Returns("DirectoryProgram", 201).Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(409, "ProgramKeyInUse or ProgramLimitReached"));
            post.Static.Add(HttpMethod.PUT, _Base + "/programs/order", OrderProgramsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Reorder directory programs", _Tag, "Body { programIds }: every non-retired program exactly once." + _Admin)
                    .Body("DirectoryProgramOrderRequest").Returns("{ updated }").Resp(400, "ValidationFailed").Resp(403, "Forbidden"));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/programs/{id}", UpdateProgramAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Edit a directory program", _Tag, "Body { label, labels, tags } (keys never change; sending a label restores a retired program)." + _Admin)
                    .PathParam("id", "Program identifier").Body("DirectoryProgramRequest").Returns("DirectoryProgram").Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.DELETE, _Base + "/programs/{id}", DeleteProgramAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete a directory program", _Tag, "A program in use is retired (hidden from forms and filters, values kept); an unused one is deleted. Answers { retired }." + _Admin)
                    .PathParam("id", "Program identifier").Returns("{ retired }").Resp(403, "Forbidden").Resp(404, "NotFound"));
            EnumerationRoutes.Add(post, _Base + "/settings", AdministrativeCatalogs.DirectorySettings, "PropertySettingField",
                () => Doc("Enumerate directory settings", _Tag, "Directory integration fields with config and geocoderConfigured context. Immutable fields use Unix epoch timestamps." + _Admin),
                async (ctx, query) =>
                {
                    ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                    ApplicationServices services = _Directory.For(ctx);
                    EnumerationRequest request = services.Enumeration.Prepare(query, AdministrativeCatalogs.DirectorySettings, true);
                    DirectorySettingsView result = await services.Directory.Config.SettingsAsync(actor, ctx.Token).ConfigureAwait(false);
                    return CatalogEnumerationResponse<PropertySettingField>.From(CatalogEnumeration.Page(request, result.Settings, field => field.Key, search: field => field.Key + " " + field.Label + " " + field.Description),
                        new Dictionary<string, object?> { ["config"] = result.Config, ["geocoderConfigured"] = result.GeocoderConfigured });
                });
            post.Static.Add(HttpMethod.PUT, _Base + "/settings", UpdateSettingsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Edit directory settings", _Tag, "Body { venueLabel?, venueLabelPlural?, instructorLabel?, instructorLabelPlural? (1 to 40), instructorRankFieldKey? (a Rank profile field; empty clears), submissions?, claims?, values { directory.*: value }, clear [directory.* keys] }. Secrets (directory.geocoder.apiKey, directory.map.proxyKey) are stored encrypted. Audited as directory.settings.update (key names only)." + _Admin)
                    .Body("DirectorySettingsRequest").Returns("DirectorySettings").Resp(400, "ValidationFailed").Resp(403, "Forbidden"));
        }

        private void AddVenueAction(WatsonWebserver.Core.Routing.RoutingGroup post, string suffix, string summary, string description, Func<HttpContextBase, Task> handler)
        {
            post.Parameter.Add(HttpMethod.POST, _Venues + "/{id}" + suffix, handler, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc(summary, _Tag, description + _Staff).PathParam("id", "Venue identifier").Body("{ note?, reason? }", false).Returns(_AdminVenue).Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound"));
        }

        private void AddInstructorAction<T>(WatsonWebserver.Core.Routing.RoutingGroup post, string suffix, string summary, string description, Func<HttpContextBase, ContentActor, RequestInfo, Task<T>> action) where T : class
        {
            post.Parameter.Add(HttpMethod.POST, _Instructors + "/{id}" + suffix, async ctx =>
            {
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await action(ctx, actor, Info(ctx)).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc(summary, _Tag, description + _Staff).PathParam("id", "Instructor identifier").Returns(_AdminInstructor).Resp(403, "Forbidden").Resp(404, "NotFound"));
        }

        private RequestInfo Info(HttpContextBase ctx)
        {
            return RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
        }

        private Task<ContentActor> ActorAsync(HttpContextBase ctx)
        {
            return _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token);
        }

        private async Task<object> RunAsync<T>(HttpContextBase ctx, Func<ContentActor, Task<T>> body) where T : class
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            return await body(actor).ConfigureAwait(false);
        }

        private async Task CreateVenueAsync(HttpContextBase ctx)
        {
            VenueRequest request = RouteHelper.ReadJson<VenueRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Directory.Venues.CreateAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GetVenueAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Directory.Venues.AdminGetAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateVenueAsync(HttpContextBase ctx)
        {
            VenueRequest request = RouteHelper.ReadJson<VenueRequest>(ctx, out HashSet<string> present);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Directory.Venues.UpdateAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, present, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteVenueAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await _Directory.For(ctx).Directory.Venues.DeleteAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task VerifyAsync(HttpContextBase ctx, bool verified)
        {
            DirectoryNoteRequest request = RouteHelper.ReadJson<DirectoryNoteRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Directory.Venues.VerifyAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), verified, request.Note, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task StatusAsync(HttpContextBase ctx, VenueStatusEnum status)
        {
            ReasonRequest request = RouteHelper.ReadJson<ReasonRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Directory.Venues.SetStatusAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), status, request.Reason, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task LinkInstructorAsync(HttpContextBase ctx)
        {
            VenueInstructorRequest request = RouteHelper.ReadJson<VenueInstructorRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Directory.Venues.LinkInstructorAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), RouteHelper.Param(ctx, "instructorId"), request.Role, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UnlinkInstructorAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await _Directory.For(ctx).Directory.Venues.UnlinkInstructorAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), RouteHelper.Param(ctx, "instructorId"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task CreateInstructorAsync(HttpContextBase ctx)
        {
            InstructorRequest request = RouteHelper.ReadJson<InstructorRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Directory.Instructors.CreateAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GetInstructorAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Directory.Instructors.AdminGetAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateInstructorAsync(HttpContextBase ctx)
        {
            InstructorRequest request = RouteHelper.ReadJson<InstructorRequest>(ctx, out HashSet<string> present);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Directory.Instructors.UpdateAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, present, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteInstructorAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await _Directory.For(ctx).Directory.Instructors.DeleteAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task LinkUserAsync(HttpContextBase ctx)
        {
            InstructorLinkRequest request = RouteHelper.ReadJson<InstructorLinkRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Directory.Instructors.LinkAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request.UserId, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GetSubmissionAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Directory.Submissions.AdminGetAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task OverrideAsync(HttpContextBase ctx)
        {
            SubmissionOverrideRequest request = RouteHelper.ReadJson<SubmissionOverrideRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Directory.Submissions.OverrideAsync(actor, RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ApproveAsync(HttpContextBase ctx)
        {
            SubmissionApproveRequest request = RouteHelper.ReadJson<SubmissionApproveRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Directory.Submissions.ApproveAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task RejectAsync(HttpContextBase ctx)
        {
            SubmissionRejectRequest request = RouteHelper.ReadJson<SubmissionRejectRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Directory.Submissions.RejectAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ImportAsync(HttpContextBase ctx)
        {
            DirectoryImportRequest request = RouteHelper.ReadJson<DirectoryImportRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Directory.Admin.ImportAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task RegeocodeAsync(HttpContextBase ctx)
        {
            RegeocodeRequest request = RouteHelper.ReadJson<RegeocodeRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 202, await _Directory.For(ctx).Directory.Admin.RegeocodeAsync(actor, request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ExportAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            DirectoryAccess.RequireStaff(actor);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/csv; charset=utf-8";
            ctx.Response.Headers["Content-Disposition"] = "attachment; filename=\"directory.csv\"";
            ctx.Response.ChunkedTransfer = true;
            await _Directory.For(ctx).Directory.Admin.ExportAsync(actor, async chunk =>
            {
                await ctx.Response.SendChunk(Encoding.UTF8.GetBytes(chunk), false, ctx.Token).ConfigureAwait(false);
            }, ctx.Token).ConfigureAwait(false);
            await ctx.Response.SendChunk(Array.Empty<byte>(), true, ctx.Token).ConfigureAwait(false);
        }

        private async Task MetricsAsync(HttpContextBase ctx)
        {
            EnumerationQuery query=ctx.Request.Method==HttpMethod.GET?RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx):RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            int days=AggregateEnumeration.Days(query);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            ApplicationServices services=_Directory.For(ctx);
            TrellisKit.Core.Responses.DirectoryMetricsView summary=await services.Directory.Admin.MetricsAsync(actor, days, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx,200,AggregateEnumeration.Daily(services,"directory-daily",query,summary)).ConfigureAwait(false);
        }

        private async Task CreateProgramAsync(HttpContextBase ctx)
        {
            DirectoryProgramRequest request = RouteHelper.ReadJson<DirectoryProgramRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Directory.Config.CreateProgramAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task OrderProgramsAsync(HttpContextBase ctx)
        {
            DirectoryProgramOrderRequest request = RouteHelper.ReadJson<DirectoryProgramOrderRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            int updated = await _Directory.For(ctx).Directory.Config.OrderProgramsAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, new Dictionary<string, int> { { "updated", updated } }).ConfigureAwait(false);
        }

        private async Task UpdateProgramAsync(HttpContextBase ctx)
        {
            DirectoryProgramRequest request = RouteHelper.ReadJson<DirectoryProgramRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Directory.Config.UpdateProgramAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteProgramAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            bool retired = await _Directory.For(ctx).Directory.Config.DeleteProgramAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, new Dictionary<string, bool> { { "retired", retired } }).ConfigureAwait(false);
        }

        private async Task GetSettingsAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Directory.Config.SettingsAsync(actor, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateSettingsAsync(HttpContextBase ctx)
        {
            DirectorySettingsRequest request = RouteHelper.ReadJson<DirectorySettingsRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Directory.Config.UpdateSettingsAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
