namespace TrellisKit.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Attendance routes (docs/TRELLISKIT.md, Ranks, curricula, and attendance): check-in to events and venue schedule
    /// sessions, rotating codes, the staff roster, and the caller's own check-ins. Every route answers 404 NotAvailable
    /// while ranks.enabled is off.
    /// </summary>
    public class AttendanceRoutes
    {
        #region Private-Members

        private const string _Tag = "Attendance";
        private const string _AdminTag = "Admin attendance";
        private const string _Awarders = " Rank awarders (RankAwarder or SystemAdmin); 403 Forbidden otherwise. 404 NotAvailable while ranks.enabled is off.";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public AttendanceRoutes(PropertyDirectory directory)
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
            post.Static.Add(HttpMethod.POST, Constants.ApiPrefix + "/attendance/check-in", async ctx =>
            {
                AttendanceCheckInRequest request = RouteHelper.ReadJson<AttendanceCheckInRequest>(ctx);
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Ranks.Attendance.CheckInAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Check in", _Tag, "Body { targetType, targetId, occurrenceDate, code, userId, method, labels, tags }. targetType Event (targetId an event; occurrenceDate optional and must be its start date in its time zone) or VenueSession (targetId {venueId}:{sessionIndex}, the zero-based position in the venue's schedule.sessions; occurrenceDate yyyy-MM-dd required and on the session's weekday). A member checks themselves in with the target's current rotating code (method QR or SelfWithCode) between attendance.checkInWindowBeforeMinutes before and attendance.checkInWindowAfterMinutes after the start (the session start on the date in the venue's time zone). A rank awarder checks another member in with userId (method Staff, no code, any time after the window opens). One check-in per member, target, and date. Audited attendance.checkIn. 404 NotAvailable while ranks.enabled is off.")
                .Body("AttendanceCheckInRequest").Returns("AttendanceView", 201).Resp(400, "ValidationFailed or InvalidCode").Resp(403, "Forbidden").Resp(404, "NotFound or NotAvailable").Resp(409, "AlreadyCheckedIn").Resp(422, "OutsideCheckInWindow"));
            post.Static.Add(HttpMethod.GET, Constants.ApiPrefix + "/attendance/check-in-code", async ctx =>
            {
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ranks.Attendance.CodeAsync(actor, RouteHelper.Query(ctx, "targetType"), RouteHelper.Query(ctx, "targetId"), RouteHelper.Query(ctx, "occurrenceDate"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Read a check-in code", _Tag, "The target occurrence's current six-digit code for the staff screen, with expiresUtc (it rotates every attendance.codeRotationSeconds, default 60; the previous code is accepted for one more period), qrPayload, and the check-in window." + _Awarders)
                .QueryParam("targetType", "Event or VenueSession").QueryParam("targetId", "Event identifier or {venueId}:{sessionIndex}").QueryParam("occurrenceDate", "yyyy-MM-dd (required for VenueSession)")
                .Returns("CheckInCodeResponse").Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound or NotAvailable"));
            EnumerationRoutes.Add(post, Constants.ApiPrefix + "/admin/attendance", AttendanceCollections.Roster, "AttendanceView",
                () => Doc("List attendance", _AdminTag, "Check-ins with member summaries, newest check-in first. Filters targetType, targetId, occurrenceDate (yyyy-MM-dd), userId, labels, tags." + _Awarders),
                async (ctx, query) =>
                {
                    ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                    return await _Directory.For(ctx).Ranks.Attendance.RosterAsync(actor, query, ctx.Token).ConfigureAwait(false);
                });
            post.Parameter.Add(HttpMethod.DELETE, Constants.ApiPrefix + "/admin/attendance/{id}", async ctx =>
            {
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await _Directory.For(ctx).Ranks.Attendance.DeleteAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
                await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Remove a check-in", _AdminTag, "Removes a check-in recorded by mistake. Audited attendance.delete." + _Awarders)
                .PathParam("id", "Check-in identifier").Resp(204, "Removed").Resp(403, "Forbidden").Resp(404, "NotFound or NotAvailable"));
            EnumerationRoutes.Add(post, Constants.ApiPrefix + "/me/attendance", AttendanceCollections.Mine, "AttendanceView",
                () => Doc("List my attendance", _Tag, "The caller's check-ins, newest first. Filters targetType, targetId, occurrenceDate, labels, tags. 404 NotAvailable while ranks.enabled is off."),
                async (ctx, query) =>
                {
                    ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                    return await _Directory.For(ctx).Ranks.Attendance.MineAsync(actor, query, ctx.Token).ConfigureAwait(false);
                });
        }

        #endregion

        #region Private-Methods

        private RequestInfo Info(HttpContextBase ctx)
        {
            return RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
        }

        private Task<ContentActor> ActorAsync(HttpContextBase ctx)
        {
            return _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token);
        }

        #endregion
    }
}
