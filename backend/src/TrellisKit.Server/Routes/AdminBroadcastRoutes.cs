namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Admin broadcast routes (docs/PHASE5_CONTRACT.md sections 8.1 and 8.2, as-built note D): segments and the preview,
    /// broadcasts, test sends, scheduling, sending, cancel, and deliveries. SystemAdmin and Communicator; Essential
    /// broadcasts are SystemAdmin only.
    /// </summary>
    public class AdminBroadcastRoutes
    {
        #region Private-Members

        private const string _Tag = "Admin broadcasts";
        private const string _Base = Constants.ApiPrefix + "/admin/broadcasts";
        private const string _Segments = _Base + "/segments";
        private const string _Broadcast = "Broadcast { id, title, channels { inApp, push, email }, inApp { title, body, path }, push { title, body }, email { subject, preheader, body, ctaLabel, ctaPath }, segment { id, name, filters }, category, essentialReason, status, sendAtUtc, stats, createdBy, labels, tags, createdUtc, updatedUtc, startedUtc, sentUtc, canceledUtc }";
        private const string _Segment = "BroadcastSegment { id, name, description, filters [{ field, operator, value }], createdBy, labels, tags, createdUtc, updatedUtc }";
        private const string _Delivery = "BroadcastDelivery { id, broadcastId, user: UserSummary, inAppStatus, pushStatus, emailStatus, labels, tags, createdUtc, updatedUtc }";
        private const string _Who = " SystemAdmin or Communicator.";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public AdminBroadcastRoutes(PropertyDirectory directory)
        {
            _Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="server">Watson server.</param>
        /// <exception cref="ArgumentNullException">The server is null.</exception>
        public void Register(Webserver server)
        {
            ArgumentNullException.ThrowIfNull(server);
            WatsonWebserver.Core.Routing.RoutingGroup post = server.Routes.PostAuthentication;

            // Segments (static paths before the broadcast id routes).
            EnumerationRoutes.Add(post, _Segments, BroadcastCollections.Segments, _Segment,
                () => Doc("List broadcast segments", _Tag, "Saved audiences, newest first; search over name and description; label and tag filters." + _Who),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Broadcasts.Segments.EnumerateAsync(actor, query, ctx.Token)));
            post.Static.Add(HttpMethod.POST, _Segments, CreateSegmentAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Create a broadcast segment", _Tag, "Body { name (1 to 100), description?, filters [{ field, operator, value }] (AND; at most 20), labels, tags }. Fields: membership (eq Active, NotActive, Any), membershipSource (in Subscription, SignupCode, AdminComp), store (in Apple, Google, Stripe), profile.<fieldKey> (member directory operators), labels (any or all, staff: labels included), tags.<key> (eq value or exists true), signedUpAfterUtc and signedUpBeforeUtc (eq ISO time), activeWithinDays and inactiveForDays (eq days), hasPushToken and hasVerifiedEmail (eq boolean), role (in staff roles), eventRsvp (operator going, interested, waitlisted, or any; value event id) and seriesFollowers (eq series id) when the events tables exist. Audited broadcast.segment.create." + _Who)
                    .Body("BroadcastSegmentRequest").Returns(_Segment, 201).Resp(400, "ValidationFailed or SegmentInvalid").Resp(403, "Forbidden"));
            post.Static.Add(HttpMethod.POST, _Segments + "/preview", PreviewAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Preview a broadcast audience", _Tag, "Body { segmentId } or { segment: { filters } }, and category? (Essential ignores the Announcements preferences in the channel counts). Recipients are always age attested, not banned, not deleted, and members or staff in FullyGated. Returns { count, byChannel { inApp, push, email }, sample { objects (at most 10 UserSummary), totalRecords } }." + _Who)
                    .Body("BroadcastPreviewRequest", false).Returns("BroadcastPreview").Resp(400, "ValidationFailed or SegmentInvalid").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.GET, _Segments + "/{id}", GetSegmentAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a broadcast segment", _Tag, "A saved segment with its filters." + _Who)
                    .PathParam("id", "Segment identifier").Bounded(_Segment, "filters at most 20 with at most 20 values each; labels and tags 50 each").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, _Segments + "/{id}", UpdateSegmentAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Edit a broadcast segment", _Tag, "Any create field; omitted fields stay; filters, when sent, replace them. Broadcasts that copied the segment keep their copies. Audited broadcast.segment.update." + _Who)
                    .PathParam("id", "Segment identifier").Body("BroadcastSegmentRequest").Returns(_Segment).Resp(400, "ValidationFailed or SegmentInvalid").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.DELETE, _Segments + "/{id}", DeleteSegmentAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete a broadcast segment", _Tag, "Broadcasts keep their own copies of the filters. Audited broadcast.segment.delete." + _Who)
                    .PathParam("id", "Segment identifier").Resp(204, "Deleted").Resp(404, "NotFound"));

            // Broadcasts.
            EnumerationRoutes.Add(post, _Base, BroadcastCollections.Broadcasts, _Broadcast,
                () => Doc("List broadcasts", _Tag, "Newest first by default; filters status (one or a list: Draft, Scheduled, Sending, Sent, Canceled) and channel (inApp, push, email); search over the title; label and tag filters." + _Who),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Broadcasts.Broadcasts.EnumerateAsync(actor, query, ctx.Token)));
            post.Static.Add(HttpMethod.POST, _Base, CreateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Create a broadcast", _Tag, "Body { title, channels { inApp, push, email } (default in-app only; push needs inApp), inApp { title?, body }, push { title?, body? }, email { subject?, preheader?, body?, ctaLabel?, ctaPath? }, segmentId (a saved segment, copied in) or segment { filters } (inline), category (Optional or Essential: SystemAdmin only, with essentialReason), labels, tags }. Creates a Draft. Audited broadcast.create (and broadcast.essential)." + _Who)
                    .Body("BroadcastRequest").Returns(_Broadcast, 201).Resp(400, "ValidationFailed or SegmentInvalid").Resp(403, "Forbidden").Resp(404, "NotFound (segmentId)"));
            post.Parameter.Add(HttpMethod.GET, _Base + "/{id}", GetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a broadcast", _Tag, "With its audience filters and delivery counters (stats come from delivery records, push receipts, and email delivery webhooks; no open or click tracking)." + _Who)
                    .PathParam("id", "Broadcast identifier").Bounded(_Broadcast, "segment.filters at most 20 with at most 20 values each; labels and tags 50 each").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/{id}", UpdateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Edit a broadcast", _Tag, "Drafts and Scheduled broadcasts only; any create field; omitted fields stay. Essential broadcasts are SystemAdmin only." + _Who)
                    .PathParam("id", "Broadcast identifier").Body("BroadcastRequest").Returns(_Broadcast).Resp(400, "ValidationFailed or SegmentInvalid").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "BroadcastNotEditable"));
            post.Parameter.Add(HttpMethod.DELETE, _Base + "/{id}", DeleteAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete a broadcast", _Tag, "Drafts only (cancel a scheduled broadcast)." + _Who)
                    .PathParam("id", "Broadcast identifier").Resp(204, "Deleted").Resp(404, "NotFound").Resp(409, "BroadcastNotEditable"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/test", TestAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Send a test", _Tag, "Body { channels? [inApp, push, email] } (default the broadcast's channels; push needs inApp). Delivers a copy marked Test to the caller only (Announcements preferences do not apply; suppressions do) and returns its announcementId. Does not count toward broadcasts.maxPerDay. Audited broadcast.test." + _Who)
                    .PathParam("id", "Broadcast identifier").Body("BroadcastTestRequest", false).Returns("{ accepted, channels, announcementId }", 202).Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/schedule", ScheduleAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Schedule a broadcast", _Tag, "Body { sendAtUtc } at least 5 minutes and at most 90 days ahead. Drafts and Scheduled broadcasts. The UTC day of sendAtUtc must have room under broadcasts.maxPerDay. Audited broadcast.schedule." + _Who)
                    .PathParam("id", "Broadcast identifier").Body("BroadcastScheduleRequest").Returns(_Broadcast).Resp(400, "ValidationFailed or SegmentInvalid").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "BroadcastNotEditable or BroadcastLimitReached"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/send", SendAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Send a broadcast now", _Tag, "Body { confirmCount }: must equal the current preview count within 5 percent. At most broadcasts.maxRecipients recipients and broadcasts.maxPerDay broadcasts per UTC day (SystemAdmin included). The send worker delivers in batches. Audited broadcast.send." + _Who)
                    .PathParam("id", "Broadcast identifier").Body("BroadcastSendRequest").Returns(_Broadcast).Resp(400, "ValidationFailed or SegmentInvalid").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "BroadcastNotEditable or BroadcastLimitReached"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/cancel", CancelAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Cancel a broadcast", _Tag, "A Draft, Scheduled, or Sending broadcast becomes Canceled; remaining batches stop. Audited broadcast.cancel." + _Who)
                    .PathParam("id", "Broadcast identifier").Returns(_Broadcast).Resp(404, "NotFound").Resp(409, "BroadcastNotEditable"));
            EnumerationRoutes.Add(post, _Base + "/{id}/deliveries", BroadcastCollections.Deliveries, _Delivery,
                () => Doc("List a broadcast's deliveries", _Tag, "One per recipient with a status per channel, newest first. Filters channel (inApp, push, email) and status (Pending, Created, Queued, Sent, Delivered, Failed, Suppressed, SkippedPreference, NoToken, NoAddress, NotRequested)." + _Who),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Broadcasts.Broadcasts.DeliveriesAsync(actor, RouteHelper.Param(ctx, "id"), query, ctx.Token)), "id");
        }

        #endregion

        #region Private-Methods

        private RequestInfo Info(HttpContextBase ctx)
        {
            return RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
        }

        private async Task<object> RunAsync<T>(HttpContextBase ctx, Func<ContentActor, Task<T>> body) where T : class
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            return await body(actor).ConfigureAwait(false);
        }

        private Task<ContentActor> ActorAsync(HttpContextBase ctx)
        {
            return _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token);
        }

        private async Task CreateSegmentAsync(HttpContextBase ctx)
        {
            BroadcastSegmentRequest request = RouteHelper.ReadJson<BroadcastSegmentRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Broadcasts.Segments.CreateAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task PreviewAsync(HttpContextBase ctx)
        {
            BroadcastPreviewRequest request = RouteHelper.ReadJson<BroadcastPreviewRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Broadcasts.Segments.PreviewAsync(actor, request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GetSegmentAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Broadcasts.Segments.GetAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateSegmentAsync(HttpContextBase ctx)
        {
            BroadcastSegmentRequest request = RouteHelper.ReadJson<BroadcastSegmentRequest>(ctx, out HashSet<string> present);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Broadcasts.Segments.UpdateAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, present, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteSegmentAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await _Directory.For(ctx).Broadcasts.Segments.DeleteAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase ctx)
        {
            BroadcastRequest request = RouteHelper.ReadJson<BroadcastRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Broadcasts.Broadcasts.CreateAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GetAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Broadcasts.Broadcasts.GetAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase ctx)
        {
            BroadcastRequest request = RouteHelper.ReadJson<BroadcastRequest>(ctx, out HashSet<string> present);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Broadcasts.Broadcasts.UpdateAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, present, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await _Directory.For(ctx).Broadcasts.Broadcasts.DeleteAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task TestAsync(HttpContextBase ctx)
        {
            BroadcastTestRequest request = RouteHelper.ReadJson<BroadcastTestRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 202, await _Directory.For(ctx).Broadcasts.Broadcasts.TestAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ScheduleAsync(HttpContextBase ctx)
        {
            BroadcastScheduleRequest request = RouteHelper.ReadJson<BroadcastScheduleRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Broadcasts.Broadcasts.ScheduleAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task SendAsync(HttpContextBase ctx)
        {
            BroadcastSendRequest request = RouteHelper.ReadJson<BroadcastSendRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Broadcasts.Broadcasts.SendAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task CancelAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Broadcasts.Broadcasts.CancelAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
