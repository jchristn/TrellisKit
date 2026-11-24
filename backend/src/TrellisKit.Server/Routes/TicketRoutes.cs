namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Member support ticket routes (docs/PHASE5_CONTRACT.md section 6.1): tickets, replies, close and reopen, the enabled
    /// ticket types, and ticket attachment uploads (purpose Ticket). Every route is in the SignedIn group, so any signed-in
    /// account reaches support in every access model (a lapsed or never-paid member can always ask for billing help).
    /// features.support off: 404 NotAvailable; the support kill switch: 503 FeatureDisabled.
    /// </summary>
    public class TicketRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/tickets";
        private const string _Tag = "Support";
        private const string _SignedIn = " Any signed-in account (every access model).";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public TicketRoutes(PropertyDirectory directory)
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

            post.Static.Add(HttpMethod.POST, _Base + "/uploads", UploadAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Upload a ticket attachment", _Tag, "The media upload session of POST /api/v1/media/uploads with purpose Ticket (forced), reachable by every signed-in account. Body { kind, contentType, sizeBytes, fileName, durationSeconds, scopeId, labels, tags }: scopeId is an existing own ticket, or omitted to attach the upload to a new ticket. Images only unless support.allowVideo. The file is private: never readable through the media grant; payloads carry per-object URLs." + _SignedIn)
                    .Body("MediaUploadRequest").Returns("UploadSession", 201).Resp(400, "ValidationFailed").Resp(403, "VideoUploadNotAllowed").Resp(404, "NotFound (scopeId) or NotAvailable").Resp(413, "TooLarge")
                    .Resp(415, "UnsupportedMediaType").Resp(429, "RateLimited, with Retry-After").Resp(503, "FeatureDisabled or IntegrationNotConfigured"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/uploads/{id}/upload-urls", UploadUrlsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Refresh ticket attachment upload URLs", _Tag, "As POST /api/v1/media/{id}/upload-urls for an own Ticket upload." + _SignedIn)
                    .PathParam("id", "Media identifier").Returns("UploadUrls").Resp(404, "NotFound").Resp(409, "UploadIncomplete").Resp(410, "UploadExpired"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/uploads/{id}/complete", CompleteAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Complete a ticket attachment upload", _Tag, "As POST /api/v1/media/{id}/complete for an own Ticket upload." + _SignedIn)
                    .PathParam("id", "Media identifier").Body("MediaCompleteRequest", false).Returns("Media", 202).Resp(200, "Media (already completed)").Resp(404, "NotFound").Resp(409, "UploadIncomplete").Resp(410, "UploadExpired"));

            post.Static.Add(HttpMethod.POST, _Base, CreateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Open a support ticket", _Tag, "Body { type, title, body, mediaIds, includeDiagnostics, labels, tags }: title 3 to 140 characters, body 10 to support.maxBodyLength (Markdown subset), at most support.maxAttachments uploads with purpose Ticket. With includeDiagnostics the device block comes from X-Client, X-Client-Version, X-Client-Update-Id, and the optional X-Client-Platform, X-Client-Os-Version, X-Client-Device-Model, X-Client-Locale, X-Client-Time-Zone headers. Word filter Block terms reject. At most support.maxOpenTicketsPerMember tickets not Closed; support.ticketsPerDay per day. Sends ticket_created and notifies support staff." + _SignedIn)
                    .Body("TicketCreateRequest").Returns("Ticket", 201).Resp(400, "ValidationFailed or ContentFiltered").Resp(404, "NotAvailable").Resp(409, "TicketLimitReached or MediaNotAttachable").Resp(429, "RateLimited, with Retry-After").Resp(503, "FeatureDisabled"));
            EnumerationRoutes.Add(post, _Base, SupportCollections.MemberTickets, "TicketSummary (Ticket without messages)",
                () => Doc("List my tickets", _Tag, "The caller's own tickets. Orderings LastActivityDescending (default), CreatedDescending; filters status (one or a list), type; search covers the title." + _SignedIn),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Support.Tickets.ListOwnAsync(actor, query, ctx.Token)));
            post.Parameter.Add(HttpMethod.GET, _Base + "/{id}", GetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read my ticket", _Tag, "An own ticket with its newest replies (at most 20, oldest first; internal notes are never returned). Any other ticket is 404." + _SignedIn)
                    .PathParam("id", "Ticket identifier").Bounded("Ticket", "messages preview of at most 20 with totalRecords and the enumerate path").Resp(404, "NotFound or NotAvailable"));
            EnumerationRoutes.Add(post, _Base + "/{id}/messages", SupportCollections.Messages, "TicketMessage { id, ticketId, kind, author, authorRole, authorName, body, attachments, labels, tags, createdUtc }",
                () => Doc("List my ticket's replies", _Tag, "Replies only (CreatedAscending default); attachments carry per-object URLs valid messaging.mediaUrlMinutes." + _SignedIn),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Support.Tickets.MessagesAsync(actor, RouteHelper.Param(ctx, "id"), query, ctx.Token)), "id");
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/messages", ReplyAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Reply to my ticket", _Tag, "Body { body, mediaIds }. A reply on WaitingOnMember or Resolved moves the ticket to Open; a Closed ticket reopens within support.reopenDays and is 409 TicketClosed after. support.repliesPerHour per hour. Notifies the assignee." + _SignedIn)
                    .PathParam("id", "Ticket identifier").Body("TicketReplyRequest").Returns("TicketMessage", 201).Resp(400, "ValidationFailed or ContentFiltered").Resp(404, "NotFound").Resp(409, "TicketClosed or MediaNotAttachable").Resp(429, "RateLimited, with Retry-After"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/close", CloseAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Close my ticket", _Tag, "Idempotent." + _SignedIn)
                    .PathParam("id", "Ticket identifier").Returns("Ticket").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/reopen", ReopenAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Reopen my ticket", _Tag, "Resolved, or Closed within support.reopenDays, moves to Open; other statuses are unchanged." + _SignedIn)
                    .PathParam("id", "Ticket identifier").Returns("Ticket").Resp(404, "NotFound").Resp(409, "TicketClosed (after support.reopenDays)"));
            EnumerationRoutes.Add(post, Constants.ApiPrefix + "/support/ticket-types", SupportCollections.TicketTypes, "TicketType { id, key, label, description, helpText, sortOrder, isRetired, labels, tags }",
                () => Doc("List ticket types", _Tag, "The enabled ticket types in order, with the description and the help text shown above the new ticket form. Seeded from property.json support.ticketTypes (engine defaults Support, Bug, Enhancement, Billing)." + _SignedIn),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Support.Types.ListAsync(actor, query, ctx.Token)));
        }

        #endregion

        #region Private-Methods

        private async Task<object> RunAsync<T>(HttpContextBase ctx, Func<ContentActor, Task<T>> body) where T : class
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            return await body(actor).ConfigureAwait(false);
        }

        private async Task<ContentActor> ActorAsync(HttpContextBase ctx)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            return actor;
        }

        private static string? Header(HttpContextBase ctx, string name)
        {
            string? value = ctx.Request.RetrieveHeaderValue(name);
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static TicketClientInfo Client(HttpContextBase ctx)
        {
            string? locale = Header(ctx, "X-Client-Locale");
            if (locale == null)
            {
                string? accept = Header(ctx, "Accept-Language");
                if (accept != null) locale = accept.Split(',')[0].Split(';')[0].Trim();
                if (String.IsNullOrEmpty(locale) || locale == "*") locale = null;
            }
            return new TicketClientInfo
            {
                Client = Header(ctx, "X-Client"),
                ClientVersion = Header(ctx, "X-Client-Version"),
                UpdateId = Header(ctx, "X-Client-Update-Id"),
                Platform = Header(ctx, "X-Client-Platform"),
                OsVersion = Header(ctx, "X-Client-Os-Version"),
                DeviceModel = Header(ctx, "X-Client-Device-Model"),
                Locale = locale,
                TimeZone = Header(ctx, "X-Client-Time-Zone")
            };
        }

        private async Task UploadAsync(HttpContextBase ctx)
        {
            MediaUploadRequest request = RouteHelper.ReadJson<MediaUploadRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            _Directory.For(ctx).Support.RequireEnabled();
            request.Purpose = MediaPurposeEnum.Ticket.ToString();
            UploadSessionResponse session = await _Directory.For(ctx).Media.CreateUploadAsync(actor, request, MediaRoutes.RequestOrigin(ctx, _Directory.For(ctx).Settings.Rest.Ssl), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, session).ConfigureAwait(false);
        }

        private async Task UploadUrlsAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            _Directory.For(ctx).Support.RequireEnabled();
            string id = RouteHelper.Param(ctx, "id");
            await _Directory.For(ctx).Media.RequirePurposeAsync(actor, id, MediaPurposeEnum.Ticket, ctx.Token).ConfigureAwait(false);
            UploadUrlsResponse response = await _Directory.For(ctx).Media.RefreshUploadUrlsAsync(actor, id, MediaRoutes.RequestOrigin(ctx, _Directory.For(ctx).Settings.Rest.Ssl), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, response).ConfigureAwait(false);
        }

        private async Task CompleteAsync(HttpContextBase ctx)
        {
            MediaCompleteRequest request = RouteHelper.ReadJson<MediaCompleteRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            _Directory.For(ctx).Support.RequireEnabled();
            string id = RouteHelper.Param(ctx, "id");
            await _Directory.For(ctx).Media.RequirePurposeAsync(actor, id, MediaPurposeEnum.Ticket, ctx.Token).ConfigureAwait(false);
            MediaCompleteOutcome outcome = await _Directory.For(ctx).Media.CompleteAsync(actor, id, request, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, outcome.Accepted ? 202 : 200, outcome.Media).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase ctx)
        {
            TicketCreateRequest request = RouteHelper.ReadJson<TicketCreateRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            TicketView ticket = await _Directory.For(ctx).Support.Tickets.CreateAsync(actor, request, Client(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, ticket).ConfigureAwait(false);
        }

        private async Task GetAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Support.Tickets.GetAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ReplyAsync(HttpContextBase ctx)
        {
            TicketReplyRequest request = RouteHelper.ReadJson<TicketReplyRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Support.Tickets.ReplyAsync(actor, RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task CloseAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Support.Tickets.CloseAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ReopenAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Support.Tickets.ReopenAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
