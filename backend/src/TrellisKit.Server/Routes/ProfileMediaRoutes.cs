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

    /// <summary>Profile photo/video galleries. Upload bytes use the existing media upload routes.</summary>
    public class ProfileMediaRoutes
    {
        private readonly PropertyDirectory _Directory;
        private const string _Base = Constants.ApiPrefix + "/users/{id}/media";
        /// <summary>Bind to the property resolver.</summary>
        public ProfileMediaRoutes(PropertyDirectory directory) { _Directory = directory ?? throw new ArgumentNullException(nameof(directory)); }
        /// <summary>Register bounded reads, owner attachment, and owner deletion.</summary>
        public void Register(Webserver server)
        {
            WatsonWebserver.Core.Routing.RoutingGroup post = server.Routes.PostAuthentication;
            EnumerationRoutes.Add(post, _Base, ProfileMediaCollections.Items, "ProfileMediaItem",
                () => Doc("List profile multimedia", "Members", "Signed-in profile readers only. Blocked users and deleted/banned profiles are unavailable. Filters kind (Image or Video), labels and tags. Moderated items are visible to feed moderators. Uploads also appear on the owner's public page when enabled. Public file access is rechecked per request.").PathParam("id", "Profile owner"),
                async (ctx, query) =>
                {
                    ApplicationServices services = _Directory.For(ctx);
                    ContentActor actor = await services.Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
                    return await services.ProfileMedia.ListAsync(actor, RouteHelper.Param(ctx, "id"), query, ctx.Token).ConfigureAwait(false);
                }, "id");
            post.Parameter.Add(HttpMethod.POST, _Base, AddAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Add profile multimedia", "Members", "Only the profile owner. Body { mediaIds (1 to 10 own unattached Image/Video uploads), labels?, tags? }. Honors posting, media, video and mute permissions. Report items with targetType ProfileMedia.").PathParam("id", "Profile owner").Body("ProfileMediaAddRequest").Returns("ProfileMediaAddResponse", 201).Resp(403, "Forbidden").Resp(409, "MediaNotAttachable"));
            post.Parameter.Add(HttpMethod.DELETE, _Base + "/{itemId}", DeleteAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete profile multimedia", "Members", "Only the profile owner; purges the attached media. Staff use audited moderation actions on ProfileMedia.").PathParam("id", "Profile owner").PathParam("itemId", "Gallery item").Resp(204, "Deleted").Resp(403, "Forbidden").Resp(404, "NotFound"));
        }
        private async Task AddAsync(HttpContextBase ctx)
        {
            ApplicationServices services = _Directory.For(ctx);
            ContentActor actor = await services.Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            ProfileMediaAddRequest request = RouteHelper.ReadJson<ProfileMediaAddRequest>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 201, await services.ProfileMedia.AddAsync(actor, RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }
        private async Task DeleteAsync(HttpContextBase ctx)
        {
            ApplicationServices services = _Directory.For(ctx);
            ContentActor actor = await services.Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await services.ProfileMedia.DeleteAsync(actor, RouteHelper.Param(ctx, "id"), RouteHelper.Param(ctx, "itemId"), ctx.Token).ConfigureAwait(false);
            ctx.Response.StatusCode = 204;
            await ctx.Response.Send(ctx.Token).ConfigureAwait(false);
        }
    }
}
