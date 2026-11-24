namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Admin sign-up code routes. SystemAdmin or the break-glass token.
    /// </summary>
    public class AdminSignupCodeRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/admin/signup-codes";
        private const string _Tag = "Admin: sign-up codes";
        private const string _Admin = " SystemAdmin or the admin token.";
        private const string _CsvPaging = "Bounded: accepts the list's enumeration query and returns at most enumeration.maxResultsAdmin rows (the default page size here). When more rows remain the X-Continuation-Token response header carries the token for the next call (send it as continuationToken); X-Total-Records has the total.";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public AdminSignupCodeRoutes(PropertyDirectory directory)
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
            EnumerationRoutes.Add(post, _Base, PhaseOneCollections.SignupCodes, "SignupCode",
                () => Doc("List sign-up codes", _Tag, "Search matches code and name. Filters: status (Active, Scheduled, Expired, Exhausted, Inactive), batchId; the old tag filter is the standard labels filter." + _Admin),
                async (ctx, query) => await _Directory.For(ctx).SignupCodes.ListAsync(query, ctx.Token).ConfigureAwait(false));
            post.Static.Add(HttpMethod.POST, _Base, CreateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Create a sign-up code", _Tag, "Omit code to generate one." + _Admin)
                    .Body("{ code?, name, description?, notes?, grantType (DurationMonths, FixedEndDate, Lifetime, or Discount), grantMonths?, grantEndUtc?, discountPercent? or discountAmountCents? (Discount), discountDuration? (Once or Forever), maxRedemptions?, startsUtc?, expiresUtc?, labels?, tags? }")
                    .Resp(201, "SignupCode").Resp(400, "ValidationFailed").Resp(409, "CodeInUse"));
            post.Static.Add(HttpMethod.GET, _Base + "/summary", SummaryAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Sign-up code summary", _Tag, "Totals across every code; conversionRate is a 0 to 1 fraction." + _Admin).Returns("SignupCodeSummary"));
            post.Static.Add(HttpMethod.GET, _Base + "/export.csv", ExportCodesAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Export codes as CSV", _Tag, _CsvPaging + _Admin)
                    .EnumerationQueryParams(PhaseOneCollections.SignupCodes.OrderingNames(), "status, batchId").File("text/csv", "One CSV page with a header row; X-Continuation-Token names the next page").Resp(400, "ValidationFailed or InvalidContinuationToken"));
            post.Static.Add(HttpMethod.POST, _Base + "/batches", BatchAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Create a batch of single-use codes", _Tag, "1 to 1000 generated codes." + _Admin)
                    .Body("{ name, quantity, grantType, grantMonths?, grantEndUtc?, discountPercent?, discountAmountCents?, discountDuration?, expiresUtc?, labels?, tags? }; labels and tags go on the batch and on every code").Resp(201, "{ batchId, codes, labels, tags }").Resp(400, "ValidationFailed"));
            post.Parameter.Add(HttpMethod.GET, _Base + "/{id}", GetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a sign-up code", _Tag, "Adds redemptionsByDay (last 90 days) and outcomes." + _Admin).PathParam("id", "Code identifier").Bounded("SignupCodeDetail", "redemptionsByDay holds exactly 90 days").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/{id}", UpdateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Update a sign-up code", _Tag, "Code and grant fields only while redemptionCount is 0; maxRedemptions cannot go below redemptionCount." + _Admin)
                    .PathParam("id", "Code identifier").Body("Partial code; labels and tags replace the stored ones when present").Resp(200, "SignupCode").Resp(400, "ValidationFailed").Resp(404, "NotFound").Resp(409, "CodeLocked or CodeInUse"));
            post.Parameter.Add(HttpMethod.DELETE, _Base + "/{id}", DeleteAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete a sign-up code", _Tag, "Only a code that has never been redeemed; a redeemed code keeps its history and can be deactivated instead." + _Admin)
                    .PathParam("id", "Code identifier").Resp(204, "Deleted").Resp(404, "NotFound").Resp(409, "CodeRedeemed"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/deactivate", DeactivateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Deactivate a sign-up code", _Tag, "Stops new redemptions; existing memberships continue." + _Admin).PathParam("id", "Code identifier").Resp(200, "SignupCode").Resp(404, "NotFound"));
            EnumerationRoutes.Add(post, _Base + "/{id}/redemptions", PhaseOneCollections.Redemptions, "Redemption",
                () => Doc("List a code's redemptions", _Tag, "Search matches member name and email. Filter: status (Active, Expired, Revoked)." + _Admin).Resp(404, "NotFound"),
                async (ctx, query) => await _Directory.For(ctx).SignupCodes.ListRedemptionsAsync(RouteHelper.Param(ctx, "id"), query, ctx.Token).ConfigureAwait(false), "id");
            post.Parameter.Add(HttpMethod.GET, _Base + "/{id}/redemptions/export.csv", ExportRedemptionsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Export a code's redemptions as CSV", _Tag, _CsvPaging + _Admin).PathParam("id", "Code identifier")
                    .EnumerationQueryParams(PhaseOneCollections.Redemptions.OrderingNames(), "status").File("text/csv", "One CSV page with a header row; X-Continuation-Token names the next page").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/revoke-all", RevokeAllAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Revoke every redemption of a code", _Tag, _Admin.Trim()).PathParam("id", "Code identifier").Body("{ reason }").Resp(200, "{ revokedCount }").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, Constants.ApiPrefix + "/admin/signup-code-redemptions/{id}/revoke", RevokeAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Revoke one redemption", _Tag, "Ends the member's free membership and notifies them." + _Admin).PathParam("id", "Redemption identifier")
                    .Body("{ reason }").Resp(200, "Redemption").Resp(400, "ValidationFailed").Resp(404, "NotFound"));
        }

        #endregion

        #region Private-Methods

        private RequestInfo Info(HttpContextBase ctx)
        {
            return RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
        }

        private async Task CreateAsync(HttpContextBase ctx)
        {
            SignupCodeCreateRequest request = RouteHelper.ReadJson<SignupCodeCreateRequest>(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).SignupCodes.CreateAsync(request, Info(ctx), actor, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            await _Directory.For(ctx).SignupCodes.DeleteAsync(RouteHelper.Param(ctx, "id"), Info(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task SummaryAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).SignupCodes.SummaryAsync(ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ExportCodesAsync(HttpContextBase ctx)
        {
            EnumerationQuery query = RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx);
            await RouteHelper.SendCsvPageAsync(ctx, "signup-codes.csv", await _Directory.For(ctx).SignupCodes.ExportCodesCsvAsync(query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task BatchAsync(HttpContextBase ctx)
        {
            SignupCodeBatchRequest request = RouteHelper.ReadJson<SignupCodeBatchRequest>(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).SignupCodes.CreateBatchAsync(request, Info(ctx), actor, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GetAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).SignupCodes.GetAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase ctx)
        {
            SignupCodeUpdateRequest request = RouteHelper.ReadJson<SignupCodeUpdateRequest>(ctx, out HashSet<string> present);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).SignupCodes.UpdateAsync(RouteHelper.Param(ctx, "id"), request, present, Info(ctx), actor, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeactivateAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).SignupCodes.DeactivateAsync(RouteHelper.Param(ctx, "id"), Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ExportRedemptionsAsync(HttpContextBase ctx)
        {
            EnumerationQuery query = RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx, "id");
            CsvPage page = await _Directory.For(ctx).SignupCodes.ExportRedemptionsCsvAsync(RouteHelper.Param(ctx, "id"), query, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendCsvPageAsync(ctx, "signup-code-redemptions.csv", page).ConfigureAwait(false);
        }

        private async Task RevokeAllAsync(HttpContextBase ctx)
        {
            ReasonRequest request = RouteHelper.ReadJson<ReasonRequest>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).SignupCodes.RevokeAllAsync(RouteHelper.Param(ctx, "id"), request.Reason, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task RevokeAsync(HttpContextBase ctx)
        {
            ReasonRequest request = RouteHelper.ReadJson<ReasonRequest>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).SignupCodes.RevokeRedemptionAsync(RouteHelper.Param(ctx, "id"), request.Reason, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
