namespace TrellisKit.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Member reports (Phase 2 contract section 9.1): POST /api/v1/reports (Member group; muted members and members
    /// without canPost may report).
    /// </summary>
    public class ReportRoutes
    {
        #region Private-Members

        private const string _Tag = "Reports";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public ReportRoutes(PropertyDirectory directory)
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
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, Constants.ApiPrefix + "/reports", CreateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Report content or a member", _Tag, "Body { targetType: FeedPost | Comment | User, targetId, reason: Spam | Harassment | HateSpeech | SexualContent | Violence | SelfHarm | Impersonation | IntellectualProperty | Other, details? (at most 1000; at least 10 for Other), labels?, tags? (stored on the new report; staff: entries from non-staff are ignored) }. " +
                    "Returns { id, status, createdUtc, labels, tags }: 201 for a new report, 200 with the caller's existing Open report on the same target. Reaching moderation.autoHideReportThreshold distinct reporters hides a post or comment (never SystemAdmin content). Members, including muted and restricted members.")
                    .Body("ReportRequest").Returns("ReportCreated", 201).Resp(200, "Existing Open report").Resp(400, "ValidationFailed or CannotReportSelf").Resp(403, "Forbidden (admin token)")
                    .Resp(404, "NotFound (unknown target or one the caller cannot see)").Resp(429, "RateLimited (moderation.reportsPerHour)"));
        }

        #endregion

        #region Private-Methods

        private async Task CreateAsync(HttpContextBase ctx)
        {
            ReportRequest request = RouteHelper.ReadJson<ReportRequest>(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            ReportCreateOutcome outcome = await _Directory.For(ctx).Reports.CreateAsync(actor, request, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, outcome.Created ? 201 : 200, outcome.Report).ConfigureAwait(false);
        }

        #endregion
    }
}
