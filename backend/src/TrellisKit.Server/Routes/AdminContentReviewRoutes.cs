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
    /// Content review queue routes (docs/INTEGRATION.md content review contract, SystemAdmin or the admin token): the
    /// content changelog with its review state, one row, the queue summary, and the queue actions, which run through the
    /// existing moderation and member actions.
    /// </summary>
    public class AdminContentReviewRoutes
    {
        #region Private-Members

        private const string _Tag = "Admin: content review";
        private const string _Base = Constants.ApiPrefix + "/admin/content-review";
        private const string _Admin = " SystemAdmin or the admin token.";
        private const string _Item = "ContentChange { id, targetType, targetId, contextType, contextId, action: Create | Update | Delete, actor: UserSummary | null, author: UserSummary | null, " +
            "client: Web | Mobile | Api | System, snapshot, mediaRefs, reviewStatus: Pending | Clean | Flagged | Dismissed | Actioned, reviewedUtc, reviewedBy, reviewerKind: Model | Administrator | System, " +
            "categories (SexualContent, HarassmentAndHate, Violence, IllegalActivity, SelfHarm, Spam, Scam, PersonalData, Profanity), severity: None | Low | Medium | High | Critical, reviewerNote, reviewAttempts, " +
            "lastError, notifiedUtc, moderationActionId, moderationTargetType, createdUtc, updatedUtc, labels, tags }";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">directory is null.</exception>
        public AdminContentReviewRoutes(PropertyDirectory directory)
        {
            _Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the routes.
        /// </summary>
        /// <param name="server">Webserver.</param>
        /// <exception cref="ArgumentNullException">server is null.</exception>
        public void Register(Webserver server)
        {
            ArgumentNullException.ThrowIfNull(server);
            WatsonWebserver.Core.Routing.RoutingGroup post = server.Routes.PostAuthentication;
            post.Static.Add(HttpMethod.GET, _Base + "/summary", SummaryAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Content review summary", _Tag, "{ pending, flagged, flaggedLast24Hours, failing, oldestPendingUtc, reviewerReady, reviewerProblem (AiOff, NoExecutors, JobsOff, JobDisabled, or null), " +
                    "reviewerMessage, lastRunStatus, lastRunUtc, nextRunUtc }. Rows stay Pending while the reviewer is not ready, and reviewerMessage says why." + _Admin)
                    .Returns("ContentReviewSummary").Resp(403, "Forbidden"));
            EnumerationRoutes.Add(post, _Base, ContentReviewCollections.Queue, _Item,
                () => Doc("List the content review queue", _Tag, "One row per create, edit, and delete of a member contribution, written in the same transaction as the change. " +
                    "Orderings CreatedDescending (default), CreatedAscending, SeverityDescending, ReviewedDescending. Filters status, category, severity, minimumSeverity, targetType, targetId, client, action, " +
                    "actorUserId, authorUserId (list filters take arrays or comma-separated values). Search covers the snapshot, the reviewer note, and the target id. Exact totals." + _Admin),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).ContentReview.EnumerateAsync(actor, query, ctx.Token)));
            post.Parameter.Add(HttpMethod.GET, _Base + "/{id}", GetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a content change", _Tag, _Item + "." + _Admin).PathParam("id", "Content change identifier (chg_...)")
                    .Bounded("ContentChange", "categories at most the 8 categories, mediaRefs at most 50, labels and tags 50 each").Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/action", ActAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Act on a content change", _Tag, "Body { action: Dismiss | Hide | Remove | Warn | Mute | Ban, reason (3 to 500; optional for Dismiss), note? (at most 2000), untilUtc? (Mute; null is indefinite), " +
                    "notifyAuthor? (Hide and Remove) }. Dismiss marks the row Dismissed. Hide and Remove run POST /api/v1/admin/moderation/actions on the content (409 NotModeratable when the type has no hide and remove); " +
                    "Warn records a Warn moderation action and notifies the author; Mute and Ban run the member actions on the author (409 NoAuthor without one). Every other action marks the row Actioned. " +
                    "Audited as contentReview.<action>." + _Admin)
                    .PathParam("id", "Content change identifier").Body("ContentReviewActionRequest").Returns("ContentReviewActionResult { change: ContentChange, action, moderationActionId }")
                    .Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "NotModeratable, NoAuthor, InvalidModerationState, or CannotBanSelf"));
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

        private async Task<object> RunAsync<T>(HttpContextBase ctx, Func<ContentActor, Task<T>> body) where T : class
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            return await body(actor).ConfigureAwait(false);
        }

        private async Task SummaryAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).ContentReview.SummaryAsync(actor, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GetAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).ContentReview.GetAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ActAsync(HttpContextBase ctx)
        {
            ContentReviewActionRequest request = RouteHelper.ReadJson<ContentReviewActionRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).ContentReview.ActAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
