namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Admin member routes (contract section 7). SystemAdmin or the break-glass token.
    /// </summary>
    public class AdminMemberRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/admin/members";
        private const string _Tag = "Admin: members";
        private const string _Admin = " SystemAdmin or the admin token.";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public AdminMemberRoutes(PropertyDirectory directory)
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
            EnumerationRoutes.Add(post, _Base, PhaseOneCollections.AdminMembers, "AdminMemberListItem",
                () => Doc("List members", _Tag, "Search matches displayName, handle, and email. Filters: membership (Active, Inactive, Paid, Free), rank (a key from profile.rank.values), role, banned. Deleted accounts are excluded." + _Admin).Resp(403, "Forbidden"),
                async (ctx, query) => await _Directory.For(ctx).AdminMembers.ListAsync(query, ctx.Token).ConfigureAwait(false));
            post.Parameter.Add(HttpMethod.GET, _Base + "/{id}", DetailAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a member", _Tag, "Me fields (with staff labels and tags) plus ban state and bounded previews (at most 5 each, with totalRecords and the enumerate path) of subscriptions, redemptions, comps, sessions, and membershipHistory." + _Admin)
                    .PathParam("id", "Member identifier").Bounded("AdminMemberDetail", "previews hold at most 5 objects; goals, rankHistory, signInMethods, and roles are bounded by the property configuration, the provider count, and 20 role assignments").Resp(404, "NotFound"));
            Nested(post, "subscriptions", PhaseOneCollections.Subscriptions, "AdminSubscriptionItem", "List a member's subscriptions", "Filters: store, status.",
                async (ctx, query) => await _Directory.For(ctx).AdminMembers.EnumerateSubscriptionsAsync(Id(ctx), query, ctx.Token).ConfigureAwait(false));
            Nested(post, "redemptions", PhaseOneCollections.Redemptions, "Redemption", "List a member's sign-up code redemptions", "Filter: status (Active, Expired, Revoked).",
                async (ctx, query) => await _Directory.For(ctx).AdminMembers.EnumerateRedemptionsAsync(Id(ctx), query, ctx.Token).ConfigureAwait(false));
            Nested(post, "comps", PhaseOneCollections.AdminEntitlements, "Comp", "List a member's comps", "AdminComp entitlements only. Filter: status (Active, Scheduled, Expired, Revoked).",
                async (ctx, query) => await _Directory.For(ctx).AdminMembers.EnumerateCompsAsync(Id(ctx), query, ctx.Token).ConfigureAwait(false));
            Nested(post, "sessions", PhaseOneCollections.Sessions, "Session", "List a member's sessions", "Active sessions; filter includeInactive=true adds revoked and expired ones.",
                async (ctx, query) => await _Directory.For(ctx).AdminMembers.EnumerateSessionsAsync(Id(ctx), query, ctx.Token).ConfigureAwait(false));
            Nested(post, "membership-history", PhaseOneCollections.AdminEntitlements, "MembershipHistoryItem", "List a member's membership history", "Every entitlement. Filters: source, status.",
                async (ctx, query) => await _Directory.For(ctx).AdminMembers.EnumerateMembershipHistoryAsync(Id(ctx), query, ctx.Token).ConfigureAwait(false));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/{id}/flags", FlagsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Member("Change permission flags", "Only present flags change.").Body("{ canPostToFeed?, canPromoteToFeed?, canUploadVideo?, canSendDirectMessages? }"));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/{id}/roles", RolesAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Member("Replace roles", "Replaces non-implicit roles. You cannot remove your own SystemAdmin role.").Body("{ roles: [{ role, resourceType, resourceId }] } (at most 20)").Resp(409, "CannotRemoveOwnAdmin"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/comp", CompAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Member("Grant a free membership", "DurationMonths, FixedEndDate, or Lifetime. Optional labels and tags are stored on the comp entitlement.").Body("{ grantType, months?, endUtc?, reason, labels?, tags? }"));
            post.Parameter.Add(HttpMethod.DELETE, _Base + "/{id}/comp/{compId}", EndCompAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Member("End a comp", "Revokes the comp entitlement.").PathParam("compId", "Comp identifier").Body("{ reason }"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/ban", BanAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Member("Ban a member", "Bans and revokes every session.").Body("{ reason }").Resp(409, "CannotBanSelf"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/unban", UnbanAsync, RouteHelper.HandleExceptionAsync, openApiMetadata: Member("Lift a ban", "Lets the member sign in again."));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/mute", MuteAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Member("Mute a member", "untilUtc null mutes indefinitely.").Body("{ untilUtc, reason }"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/unmute", UnmuteAsync, RouteHelper.HandleExceptionAsync, openApiMetadata: Member("Lift a mute", "Clears the mute."));
            post.Parameter.Add(HttpMethod.DELETE, _Base + "/{id}", DeleteMemberAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete a member's account", _Tag, "Anonymizes the account exactly as a self-service deletion (sessions revoked, sign-in methods and personal data removed, Stripe canceled at period end) and emails the member. Store subscriptions must be canceled by the member; storeSubscriptionNote says so." + _Admin)
                    .PathParam("id", "Member identifier").Body("{ confirm: \"DELETE\", reason }").Resp(202, "DeleteAccountResponse").Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "CannotDeleteSelf or CannotDeleteAdmin"));
            post.Parameter.Add(HttpMethod.DELETE, _Base + "/{id}/sessions", SessionsAsync, RouteHelper.HandleExceptionAsync, openApiMetadata: Member("Revoke every session", "Signs the member out everywhere."));
        }

        #endregion

        #region Private-Methods

        private static OpenApiRouteMetadata Member(string summary, string description)
        {
            return Doc(summary, _Tag, description + " Returns the member detail." + _Admin).PathParam("id", "Member identifier")
                .Bounded("AdminMemberDetail", "previews hold at most 5 objects; goals, rankHistory, signInMethods, and roles are bounded by the property configuration, the provider count, and 20 role assignments").Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound");
        }

        private RequestInfo Info(HttpContextBase ctx)
        {
            return RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
        }

        private static string Id(HttpContextBase ctx)
        {
            return RouteHelper.Param(ctx, "id");
        }

        private void Nested(WatsonWebserver.Core.Routing.RoutingGroup group, string list, EnumerationCollection collection, string item, string summary, string description,
            Func<HttpContextBase, EnumerationQuery, Task<object>> handler)
        {
            EnumerationRoutes.Add(group, _Base + "/{id}/" + list, collection, item, () => Doc(summary, _Tag, description + _Admin).Resp(404, "NotFound"), handler, "id");
        }

        private async Task DetailAsync(HttpContextBase ctx)
        {
            await Send(ctx, await _Directory.For(ctx).AdminMembers.DetailAsync(Id(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task FlagsAsync(HttpContextBase ctx)
        {
            MemberFlagsRequest request = RouteHelper.ReadJson<MemberFlagsRequest>(ctx);
            await Send(ctx, await _Directory.For(ctx).AdminMembers.UpdateFlagsAsync(Id(ctx), request, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task RolesAsync(HttpContextBase ctx)
        {
            MemberRolesRequest request = RouteHelper.ReadJson<MemberRolesRequest>(ctx);
            await Send(ctx, await _Directory.For(ctx).AdminMembers.ReplaceRolesAsync(Id(ctx), request, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task CompAsync(HttpContextBase ctx)
        {
            CompGrantRequest request = RouteHelper.ReadJson<CompGrantRequest>(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await Send(ctx, await _Directory.For(ctx).AdminMembers.GrantCompAsync(Id(ctx), request, Info(ctx), actor, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task EndCompAsync(HttpContextBase ctx)
        {
            ReasonRequest request = RouteHelper.ReadJson<ReasonRequest>(ctx);
            await Send(ctx, await _Directory.For(ctx).AdminMembers.EndCompAsync(Id(ctx), RouteHelper.Param(ctx, "compId"), request.Reason, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteMemberAsync(HttpContextBase ctx)
        {
            AdminDeleteMemberRequest request = RouteHelper.ReadJson<AdminDeleteMemberRequest>(ctx);
            if (!String.Equals(request.Confirm, "DELETE", StringComparison.Ordinal)) throw ApiErrorException.Field("confirm", "Type DELETE to confirm.");
            DeleteAccountResponse response = await _Directory.For(ctx).Accounts.AdminDeleteAccountAsync(Id(ctx), request.Reason, Info(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 202, response).ConfigureAwait(false);
        }

        private async Task BanAsync(HttpContextBase ctx)
        {
            ReasonRequest request = RouteHelper.ReadJson<ReasonRequest>(ctx);
            await Send(ctx, await _Directory.For(ctx).AdminMembers.BanAsync(Id(ctx), request.Reason, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UnbanAsync(HttpContextBase ctx)
        {
            await Send(ctx, await _Directory.For(ctx).AdminMembers.UnbanAsync(Id(ctx), Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task MuteAsync(HttpContextBase ctx)
        {
            MuteRequest request = RouteHelper.ReadJson<MuteRequest>(ctx);
            await Send(ctx, await _Directory.For(ctx).AdminMembers.MuteAsync(Id(ctx), request, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UnmuteAsync(HttpContextBase ctx)
        {
            await Send(ctx, await _Directory.For(ctx).AdminMembers.UnmuteAsync(Id(ctx), Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task SessionsAsync(HttpContextBase ctx)
        {
            await Send(ctx, await _Directory.For(ctx).AdminMembers.RevokeSessionsAsync(Id(ctx), Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private static Task Send(HttpContextBase ctx, AdminMemberDetail detail)
        {
            return RouteHelper.SendJsonAsync(ctx, 200, detail);
        }

        #endregion
    }
}
