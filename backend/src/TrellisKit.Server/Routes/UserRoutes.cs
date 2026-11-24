namespace TrellisKit.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Security;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Public member profiles (members only) and handle availability (signed in).
    /// </summary>
    public class UserRoutes
    {
        #region Private-Members

        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public UserRoutes(PropertyDirectory directory)
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
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, Constants.ApiPrefix + "/users/{id}", GetUserAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a member's public profile", "Members", "Active, non-deleted accounts only. Members only (age attested and an active membership).")
                    .PathParam("id", "Member identifier").Bounded("PublicProfile", "rankHistory holds at most profile.rankHistory.maxEntries entries; includes isStaff, isBlockedByMe, labels, tags").Resp(402, "MembershipRequired").Resp(403, "AgeAttestationRequired").Resp(404, "NotFound"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, Constants.ApiPrefix + "/handles/{handle}/availability", HandleAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Check handle availability", "Account", "Your own handle counts as available. Signed in.")
                    .PathParam("handle", "Handle").Returns("Availability").Resp(400, "ValidationFailed"));
        }

        #endregion

        #region Private-Methods

        private async Task GetUserAsync(HttpContextBase ctx)
        {
            RequestContext? context = ctx.Metadata as RequestContext;
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            string id = RouteHelper.Param(ctx, "id");
            PublicProfileResponse profile = await _Directory.For(ctx).Profiles.GetPublicAsync(id, ProfileValueService.ViewerOf(actor, id), ctx.Token).ConfigureAwait(false);
            profile.IsStaff = (await _Directory.For(ctx).Summaries.StaffAsync(new[] { profile.Id }, ctx.Token).ConfigureAwait(false)).Contains(profile.Id);
            profile.IsBlockedByMe = await _Directory.For(ctx).Blocks.HasBlockedAsync(context?.UserId, profile.Id, ctx.Token).ConfigureAwait(false);
            ObjectMetadata metadata = await _Directory.For(ctx).Metadata.LoadOneAsync(TargetTypeEnum.User, profile.Id, actor.IsStaffFor(TargetTypeEnum.User), ctx.Token).ConfigureAwait(false);
            profile.Labels = metadata.Labels;
            profile.Tags = metadata.Tags;
            await RouteHelper.SendJsonAsync(ctx, 200, profile).ConfigureAwait(false);
        }

        private async Task HandleAsync(HttpContextBase ctx)
        {
            AvailabilityResponse response = await _Directory.For(ctx).Profiles.HandleAvailabilityAsync(RouteHelper.Param(ctx, "handle"), (ctx.Metadata as RequestContext)?.UserId, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, response).ConfigureAwait(false);
        }

        #endregion
    }
}
