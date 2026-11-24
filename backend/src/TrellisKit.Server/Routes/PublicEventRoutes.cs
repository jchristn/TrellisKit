namespace TrellisKit.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Anonymous event routes: public events (docs/PHASE4_CONTRACT.md section 4.9, events.publicListingsEnabled) and
    /// the personal calendar feed (section 4.5; the token in the path authenticates).
    /// </summary>
    public class PublicEventRoutes
    {
        #region Public-Members

        /// <summary>
        /// Cache-Control of public event responses.
        /// </summary>
        public const string CacheControl = "public, max-age=60";

        #endregion

        #region Private-Members

        private const string _Tag = "Public events";
        private const string _Base = Constants.ApiPrefix + "/public/events";
        private const string _Item = "PublicEvent";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public PublicEventRoutes(PropertyDirectory directory)
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
            WatsonWebserver.Core.Routing.RoutingGroup pre = server.Routes.PreAuthentication;
            string orderings = EventCollections.PublicEvents.OrderingNames();
            string filters = "category, format, startsAfterUtc, startsBeforeUtc";
            string description = "Upcoming Published events whose audience is Public, StartAscending by default (maxResults at most 20). Items are PublicEvent (EventSummary plus description, without myRsvp, viewer, attendee data, or join URLs). " +
                "Filters " + filters + ". Cache-Control: public, max-age=60; rate limited per IP (rateLimits.publicFeedPerMinute). 404 NotAvailable when events.publicListingsEnabled is off or features.events is false. Anonymous.";
            pre.Static.Add(HttpMethod.GET, _Base, ListAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("List public events", _Tag, description).Enumeration(_Item, orderings, filters).Resp(400, "ValidationFailed").Resp(404, "NotAvailable").Resp(429, "RateLimited"));
            pre.Static.Add(HttpMethod.POST, _Base + "/enumerate", EnumerateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate public events", _Tag, "EnumerationQuery body; same result as the GET form. " + description)
                    .Body("EnumerationQuery", false).Enumeration(_Item, orderings, filters, false).Resp(400, "ValidationFailed").Resp(404, "NotAvailable").Resp(429, "RateLimited"));
            pre.Parameter.Add(HttpMethod.GET, _Base + "/{id}", GetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a public event", _Tag, "One PublicEvent by identifier or slug. Cache-Control: public, max-age=60. Anonymous.")
                    .PathParam("id", "Event identifier or slug").Returns(_Item).Resp(404, "NotFound or NotAvailable").Resp(429, "RateLimited"));
            pre.Parameter.Add(HttpMethod.GET, Constants.ApiPrefix + "/calendar/{file}", FeedAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a personal calendar feed", _Tag, "GET /api/v1/calendar/{token}.ics with the token from /api/v1/me/calendar-feed (cfd_id.secret). The member's Going and Waitlisted events from 30 days ago to events.calendarFeedDays ahead (at most events.calendarFeedMaxEvents), one VEVENT per occurrence (UID per occurrence), never join URLs or attendee data. " +
                    "Cache-Control: private, max-age=900. 404 for an unknown, rotated, or disabled token (a ban, account deletion, or lost access disables it). Rate limited per token (events.icsPerHour). Anonymous.")
                    .PathParam("file", "Token followed by .ics").File("text/calendar", "VCALENDAR").Resp(404, "NotFound or NotAvailable").Resp(429, "RateLimited"));
        }

        #endregion

        #region Private-Methods

        private Task ListAsync(HttpContextBase ctx)
        {
            return SendListAsync(ctx, RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx));
        }

        private Task EnumerateAsync(HttpContextBase ctx)
        {
            return SendListAsync(ctx, RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx));
        }

        private async Task SendListAsync(HttpContextBase ctx, EnumerationQuery query)
        {
            ApplicationServices services = Require(ctx);
            RequestInfo info = RequestInfo.From(ctx, services.Settings.Rest);
            services.Limits.EnforceIp(info.SourceIp, "public.events", services.Settings.RateLimits.PublicFeedPerMinute, RateLimitService.Minute);
            object page = await services.Events.Catalog.PublicListAsync(services.Actors.Anonymous(info.SourceIp), query, ctx.Token).ConfigureAwait(false);
            ctx.Response.Headers["Cache-Control"] = CacheControl;
            await RouteHelper.SendJsonAsync(ctx, 200, page).ConfigureAwait(false);
        }

        private async Task GetAsync(HttpContextBase ctx)
        {
            ApplicationServices services = Require(ctx);
            RequestInfo info = RequestInfo.From(ctx, services.Settings.Rest);
            services.Limits.EnforceIp(info.SourceIp, "public.events", services.Settings.RateLimits.PublicFeedPerMinute, RateLimitService.Minute);
            object view = await services.Events.Catalog.PublicGetAsync(services.Actors.Anonymous(info.SourceIp), RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            ctx.Response.Headers["Cache-Control"] = CacheControl;
            await RouteHelper.SendJsonAsync(ctx, 200, view).ConfigureAwait(false);
        }

        private async Task FeedAsync(HttpContextBase ctx)
        {
            ApplicationServices services = Require(ctx);
            string file = RouteHelper.Param(ctx, "file");
            if (!file.EndsWith(".ics", StringComparison.OrdinalIgnoreCase)) throw ApiErrorException.NotFound("Calendar feed");
            IcsDocument doc = await services.Events.Calendar.FeedAsync(file, ctx.Token).ConfigureAwait(false);
            await EventRoutes.SendIcsAsync(ctx, doc).ConfigureAwait(false);
        }

        private ApplicationServices Require(HttpContextBase ctx)
        {
            ApplicationServices services = _Directory.For(ctx);
            if (!services.Property.Property.Features.Events) throw new ApiErrorException(404, "NotAvailable", "This route is not available for this property.");
            return services;
        }

        #endregion
    }
}
