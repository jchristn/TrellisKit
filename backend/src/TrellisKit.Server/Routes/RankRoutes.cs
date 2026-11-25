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
    /// Verified rank and lineage routes (docs/TRELLISKIT.md, Ranks, curricula, and attendance). Every route answers 404
    /// NotAvailable while ranks.enabled is off; staff routes check RankAwarder or SystemAdmin in the service.
    /// </summary>
    public class RankRoutes
    {
        #region Private-Members

        private const string _Tag = "Ranks";
        private const string _AdminTag = "Admin ranks";
        private const string _Awards = Constants.ApiPrefix + "/admin/ranks/awards";
        private const string _Awarders = " Rank awarders (RankAwarder or SystemAdmin); 403 Forbidden otherwise. 404 NotAvailable while ranks.enabled is off.";
        private const string _Promotion = "RankPromotionView";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public RankRoutes(PropertyDirectory directory)
        {
            _Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the routes (and the curriculum and attendance routes).
        /// </summary>
        /// <param name="server">Webserver.</param>
        /// <exception cref="ArgumentNullException">The server is null.</exception>
        public void Register(Webserver server)
        {
            ArgumentNullException.ThrowIfNull(server);
            WatsonWebserver.Core.Routing.RoutingGroup post = server.Routes.PostAuthentication;
            post.Static.Add(HttpMethod.GET, Constants.ApiPrefix + "/me/rank", async ctx =>
            {
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ranks.Ranks.MineAsync(actor, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Read my rank", _Tag, "The caller's effective rank per enabled Rank profile field (the verified rank while one stands, else the self-reported profile value), the next rank with time in grade, attendance, and curriculum progress against rankrequirements, the 20 most recent awards and revokes (historyTotal counts all), and lineage. 404 NotAvailable while ranks.enabled is off.")
                .Bounded("MyRankResponse", "fields at most 10 Rank fields; history at most 20 with historyTotal; lineage at most 100").Resp(403, "Forbidden (admin token)").Resp(404, "NotAvailable"));
            post.Parameter.Add(HttpMethod.GET, Constants.ApiPrefix + "/members/{id}/rank", async ctx =>
            {
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ranks.Ranks.MemberAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Read a member's rank", _Tag, "The member's effective rank and verified flag per Rank field the caller may see under the field's profile visibility, with lineage of those fields. Unknown, deleted, banned, and blocked members are 404. 404 NotAvailable while ranks.enabled is off.")
                .PathParam("id", "Member identifier").Bounded("MemberRankResponse", "ranks at most 10 Rank fields; lineage at most 100").Resp(403, "Forbidden").Resp(404, "NotFound or NotAvailable"));
            post.Static.Add(HttpMethod.PUT, Constants.ApiPrefix + "/me/lineage", async ctx =>
            {
                LineageRequest request = RouteHelper.ReadJson<LineageRequest>(ctx);
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ranks.Lineage.PutAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Replace my lineage", _Tag, "Body { fieldKey, entries }: entries [{ optionKey, promotedByUserId, promotedByName, promotedUtc }] in display order (at most 50; promotedByUserId or promotedByName, not both; names at most 120 characters). Unchanged entries keep their verification; a new entry naming a member promoter sends them a LineageVerificationRequested notification. Names are screened by the word filter and recorded in the content changelog. Audited lineage.update. 404 NotAvailable while ranks.enabled is off.")
                .Body("LineageRequest").Returns("LineageResponse").Resp(400, "ValidationFailed or ContentFiltered").Resp(403, "Forbidden or Muted").Resp(404, "NotAvailable"));
            post.Parameter.Add(HttpMethod.POST, Constants.ApiPrefix + "/admin/lineage/{id}/verify", async ctx =>
            {
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ranks.Lineage.VerifyAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Verify a lineage entry", _AdminTag, "The promoter named in the entry or a SystemAdmin verifies it; nobody verifies their own lineage. Audited lineage.verify. 404 NotAvailable while ranks.enabled is off.")
                .PathParam("id", "Lineage entry identifier").Returns("LineageEntryView").Resp(403, "Forbidden").Resp(404, "NotFound or NotAvailable"));

            post.Static.Add(HttpMethod.POST, _Awards, async ctx =>
            {
                RankAwardRequest request = RouteHelper.ReadJson<RankAwardRequest>(ctx);
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Ranks.Ranks.AwardAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Award a rank", _AdminTag, "Body { userId, fieldKey, optionKey, level, note, eventId, labels, tags }: fieldKey defaults to the first enabled Rank field; level 0 to the option's maxLevel (default 0); note at most 500 characters. The awarder needs a verified rank allowed by the field's rank.awarding rules (SystemAdmin is exempt); nobody awards themselves. The member's profile value becomes the awarded rank and is locked (409 RankVerified on their own edits) while the award stands. The member gets RankPromoted (in-app and push) and the rank_promoted email. Audited rank.award." + _Awarders)
                .Body("RankAwardRequest").Returns(_Promotion, 201).Resp(400, "ValidationFailed, UnknownRankOption, LevelOutOfRange, or ContentFiltered").Resp(403, "Forbidden, AwardAboveOwnRank, or SelfAward").Resp(404, "NotFound (member) or NotAvailable"));
            EnumerationRoutes.Add(post, _Awards, RankCollections.Promotions, _Promotion,
                () => Doc("List rank awards", _AdminTag, "Awards and revokes, newest first. Filters userId, fieldKey, optionKey, actorId, action (Award or Revoke), status (Active or Revoked award rows), fromUtc, toUtc, labels, tags; search over notes, reasons, and option keys." + _Awarders),
                async (ctx, query) =>
                {
                    ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                    return await _Directory.For(ctx).Ranks.Ranks.ListAwardsAsync(actor, query, ctx.Token).ConfigureAwait(false);
                });
            post.Parameter.Add(HttpMethod.POST, _Awards + "/{id}/revoke", async ctx =>
            {
                RankRevokeRequest request = RouteHelper.ReadJson<RankRevokeRequest>(ctx);
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ranks.Ranks.RevokeAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Revoke a rank award", _AdminTag, "Body { reason } (1 to 500 characters). SystemAdmin, the original awarder, or a RankAwarder the awarding rules allow. When the award is the member's current verified rank it becomes Revoked and the profile field is self-reported and editable again. Returns the revoke row. Audited rank.revoke." + _Awarders)
                .PathParam("id", "Award identifier").Body("RankRevokeRequest").Returns(_Promotion).Resp(400, "ValidationFailed or ContentFiltered").Resp(403, "Forbidden or SelfAward").Resp(404, "NotFound or NotAvailable").Resp(409, "AlreadyRevoked"));
            EnumerationRoutes.Add(post, Constants.ApiPrefix + "/admin/ranks/eligibility", RankCollections.Eligibility, "RankEligibilityView",
                () => Doc("List rank eligibility", _AdminTag, "Members (not deleted or banned) holding a value in the Rank field, with their current rank, next rank, and progress against rankrequirements (time in grade since the verified award, check-ins since it, required curriculum items of the next option). Filters fieldKey (default the first Rank field), optionKey (current option), eligibleOnly (default true: a page holds only eligible members and may hold fewer than maxResults objects while endOfResults is false), labels and tags of the member." + _Awarders),
                async (ctx, query) =>
                {
                    ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                    return await _Directory.For(ctx).Ranks.Ranks.EligibilityAsync(actor, query, ctx.Token).ConfigureAwait(false);
                });
            EnumerationRoutes.Add(post, Constants.ApiPrefix + "/admin/ranks/requirements", RankCollections.Requirements, "RankRequirement",
                () => Doc("List rank requirements", _AdminTag, "Requirements in position order. Filters fieldKey, optionKey, labels, tags." + _Awarders),
                async (ctx, query) =>
                {
                    ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                    return await _Directory.For(ctx).Ranks.Ranks.ListRequirementsAsync(actor, query, ctx.Token).ConfigureAwait(false);
                });
            post.Static.Add(HttpMethod.PUT, Constants.ApiPrefix + "/admin/ranks/requirements", async ctx =>
            {
                RankRequirementsRequest request = RouteHelper.ReadJson<RankRequirementsRequest>(ctx);
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ranks.Ranks.ReplaceRequirementsAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Replace rank requirements", _AdminTag, "Body { fieldKey, requirements }: requirements [{ optionKey, level, minimumDaysInGrade, minimumAttendance, notes, curriculumId, labels, tags }] (at most 200; level null covers every level of the option without its own row; minimumDaysInGrade 0 to 3650; minimumAttendance 0 to 10000; notes at most 1000 characters). Replaces every requirement of the field. Audited rank.requirements. SystemAdmin only; 403 Forbidden otherwise. 404 NotAvailable while ranks.enabled is off.")
                .Body("RankRequirementsRequest").Returns("RankRequirementsResponse").Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotAvailable"));

            new CurriculumRoutes(_Directory).Register(server);
            new AttendanceRoutes(_Directory).Register(server);
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
