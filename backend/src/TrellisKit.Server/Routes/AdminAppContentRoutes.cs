namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Admin remote strings, banners, and remote assets routes (docs/LIVE_UPDATES_CONTRACT.md section 14): drafts that
    /// publish with POST /api/v1/admin/app/publish, history, archive and restore, and asset content uploads. SystemAdmin,
    /// ReleaseManager, or the admin token.
    /// </summary>
    public class AdminAppContentRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/admin/app";
        private const string _Tag = "Admin: app configuration";
        private const string _Admin = " SystemAdmin, ReleaseManager, or the admin token.";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public AdminAppContentRoutes(PropertyDirectory directory)
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
            const string stringItem = "RemoteString";
            const string bannerItem = "Banner";
            const string assetItem = "RemoteAsset";

            post.Static.Add(HttpMethod.POST, _Base + "/strings/enumerate", StringsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate remote strings", _Tag, "Engine string catalog keys joined with their overrides: key, client, defaultValue, tokens, maxLength, id, value, publishedValue, notes, overridden, pendingDelete, state, hasDraftChanges, publishedInVersion, eTag. Filters client (web, mobile), overridden, hasDraftChanges; search over key, default, and value; ordering KeyAscending (default)." + _Admin)
                    .Body("EnumerationQuery", false).Enumeration(stringItem, "KeyAscending, CreatedDescending, CreatedAscending", "client, overridden, hasDraftChanges", false).Resp(400, "ValidationFailed"));
            post.Parameter.Add(HttpMethod.GET, _Base + "/strings/{key}", GetStringAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a remote string", _Tag, "{ string, history { objects (max 10), totalRecords, enumerate } }." + _Admin)
                    .PathParam("key", "Catalog key, for example web.common.retry").Bounded("RemoteStringDetail", "history preview at most 10 with totalRecords and the enumerate path").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/strings/{key}", PutStringAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Override a string", _Tag, "Body { value, notes?, labels?, tags? }: a draft override; only the key's {tokens}, at most its maxLength, word filtered." + _Admin)
                    .PathParam("key", "Catalog key").Body("RemoteStringRequest").Returns(stringItem).Resp(400, "UnknownStringKey, InvalidStringTokens, or ValidationFailed"));
            post.Parameter.Add(HttpMethod.DELETE, _Base + "/strings/{key}", DeleteStringAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Remove a string override", _Tag, "Discards the draft; when only a published override exists, drafts its removal (pendingDelete) for the next publish." + _Admin)
                    .PathParam("key", "Catalog key").Returns(stringItem).Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/strings/{key}/history/enumerate", StringHistoryAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate a string's published revisions", _Tag, "Newest first (VersionDescending)." + _Admin)
                    .PathParam("key", "Catalog key").Body("EnumerationQuery", false).Enumeration(stringItem, "VersionDescending, CreatedDescending, CreatedAscending", String.Empty, false).Resp(404, "NotFound"));

            post.Static.Add(HttpMethod.POST, _Base + "/banners/enumerate", BannersAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate banners", _Tag, "The draft of each banner when one is pending, else its published revision. Filters active, placement, style, state, hasDraftChanges; search over title and body; orderings PriorityDescending (default), UpdatedDescending, CreatedDescending, CreatedAscending." + _Admin)
                    .Body("EnumerationQuery", false).Enumeration(bannerItem, "PriorityDescending, UpdatedDescending, CreatedDescending, CreatedAscending", "active, placement, style, state, hasDraftChanges", false).Resp(400, "ValidationFailed"));
            post.Static.Add(HttpMethod.POST, _Base + "/banners", CreateBannerAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Create a banner", _Tag, "Body { title, body?, style?, placement?, dismissible?, priority?, startsUtc?, endsUtc?, link?, imageAssetId?, clients?, rules?, labels?, tags? } as a draft." + _Admin)
                    .Body("BannerRequest").Returns(bannerItem, 201).Resp(400, "ValidationFailed or InvalidTargetingRule"));
            post.Parameter.Add(HttpMethod.GET, _Base + "/banners/{id}", GetBannerAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a banner", _Tag, "{ banner, published, history { objects (max 10), totalRecords, enumerate } }; ETag header for If-Match." + _Admin)
                    .PathParam("id", "Banner id").Bounded("BannerDetail", "history preview at most 10 with totalRecords and the enumerate path").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/banners/{id}", UpdateBannerAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Update a banner draft", _Tag, "Absent fields are unchanged; rules replace the whole list. If-Match required." + _Admin)
                    .PathParam("id", "Banner id").Body("Partial BannerRequest").Returns(bannerItem).Resp(400, "ValidationFailed or InvalidTargetingRule").Resp(404, "NotFound").Resp(412, "PreconditionFailed"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/banners/{id}/archive", ArchiveBannerAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Archive a banner", _Tag, "Draft change: removed from manifests at the next publish." + _Admin).PathParam("id", "Banner id").Body("Body", false).Returns(bannerItem).Resp(404, "NotFound").Resp(409, "Conflict"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/banners/{id}/restore", RestoreBannerAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Restore an archived banner", _Tag, "Draft change." + _Admin).PathParam("id", "Banner id").Body("Body", false).Returns(bannerItem).Resp(404, "NotFound").Resp(409, "Conflict"));
            post.Parameter.Add(HttpMethod.DELETE, _Base + "/banners/{id}", DeleteBannerAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete a banner", _Tag, "Only banners that were never published." + _Admin).PathParam("id", "Banner id").Resp(204, "Deleted").Resp(404, "NotFound").Resp(409, "Conflict"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/banners/{id}/history/enumerate", BannerHistoryAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate a banner's published revisions", _Tag, "Newest first (VersionDescending)." + _Admin)
                    .PathParam("id", "Banner id").Body("EnumerationQuery", false).Enumeration(bannerItem, "VersionDescending, CreatedDescending, CreatedAscending", String.Empty, false).Resp(404, "NotFound"));

            post.Static.Add(HttpMethod.POST, _Base + "/assets/enumerate", AssetsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate remote assets", _Tag, "The draft of each asset when one is pending. Filters state, hasDraftChanges; search over key and alt text; orderings KeyAscending (default), UpdatedDescending, CreatedDescending, CreatedAscending." + _Admin)
                    .Body("EnumerationQuery", false).Enumeration(assetItem, "KeyAscending, UpdatedDescending, CreatedDescending, CreatedAscending", "state, hasDraftChanges", false).Resp(400, "ValidationFailed"));
            post.Static.Add(HttpMethod.POST, _Base + "/assets", CreateAssetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Create a remote asset", _Tag, "Body { key, altText?, labels?, tags? } as a draft; upload the image with PUT .../content." + _Admin)
                    .Body("RemoteAssetRequest").Returns(assetItem, 201).Resp(400, "ValidationFailed").Resp(409, "Conflict"));
            post.Parameter.Add(HttpMethod.GET, _Base + "/assets/{id}", GetAssetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a remote asset", _Tag, "{ asset, published, history { objects (max 10), totalRecords, enumerate } }; ETag header for If-Match." + _Admin)
                    .PathParam("id", "Asset id").Bounded("RemoteAssetDetail", "history preview at most 10 with totalRecords and the enumerate path").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/assets/{id}", UpdateAssetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Update a remote asset draft", _Tag, "Body { key?, altText?, labels?, tags? }; the key cannot change after the first publish. If-Match required." + _Admin)
                    .PathParam("id", "Asset id").Body("RemoteAssetRequest").Returns(assetItem).Resp(400, "ValidationFailed").Resp(404, "NotFound").Resp(409, "Conflict").Resp(412, "PreconditionFailed"));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/assets/{id}/content", ContentAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Upload a remote asset image", _Tag, "Raw body with Content-Type image/png, image/jpeg, or image/webp, at most appConfig.maxAssetMegabytes; re-encoded to WebP without metadata and stored content addressed." + _Admin)
                    .PathParam("id", "Asset id").Body("Raw image bytes").Returns(assetItem).Resp(400, "ValidationFailed").Resp(404, "NotFound").Resp(413, "TooLarge"));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/assets/{id}/dark-content", DarkContentAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Upload a remote asset dark variant", _Tag, "Same as the content route, for the dark variant." + _Admin)
                    .PathParam("id", "Asset id").Body("Raw image bytes").Returns(assetItem).Resp(400, "ValidationFailed").Resp(404, "NotFound").Resp(413, "TooLarge"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/assets/{id}/archive", ArchiveAssetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Archive a remote asset", _Tag, "Draft change." + _Admin).PathParam("id", "Asset id").Body("Body", false).Returns(assetItem).Resp(404, "NotFound").Resp(409, "Conflict"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/assets/{id}/restore", RestoreAssetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Restore an archived remote asset", _Tag, "Draft change." + _Admin).PathParam("id", "Asset id").Body("Body", false).Returns(assetItem).Resp(404, "NotFound").Resp(409, "Conflict"));
            post.Parameter.Add(HttpMethod.DELETE, _Base + "/assets/{id}", DeleteAssetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete a remote asset", _Tag, "Only assets that were never published." + _Admin).PathParam("id", "Asset id").Resp(204, "Deleted").Resp(404, "NotFound").Resp(409, "Conflict"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/assets/{id}/history/enumerate", AssetHistoryAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate a remote asset's published revisions", _Tag, "Newest first (VersionDescending)." + _Admin)
                    .PathParam("id", "Asset id").Body("EnumerationQuery", false).Enumeration(assetItem, "VersionDescending, CreatedDescending, CreatedAscending", String.Empty, false).Resp(404, "NotFound"));
        }

        #endregion

        #region Private-Methods

        private RequestInfo Info(HttpContextBase ctx)
        {
            return RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
        }

        private AppContentService Content(HttpContextBase ctx)
        {
            return _Directory.For(ctx).LiveUpdates.Content;
        }

        private string Origin(HttpContextBase ctx)
        {
            return MediaRoutes.RequestOrigin(ctx, _Directory.For(ctx).Settings.Rest.Ssl);
        }

        private async Task StringsAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await Content(ctx).EnumerateStringsAsync(RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GetStringAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await Content(ctx).GetStringAsync(RouteHelper.Param(ctx, "key"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task PutStringAsync(HttpContextBase ctx)
        {
            RemoteStringRequest request = RouteHelper.ReadJson<RemoteStringRequest>(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await Content(ctx).PutStringAsync(actor, RouteHelper.Param(ctx, "key"), request, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteStringAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await Content(ctx).DeleteStringAsync(RouteHelper.Param(ctx, "key"), Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task StringHistoryAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await Content(ctx).EnumerateStringHistoryAsync(RouteHelper.Param(ctx, "key"), RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task BannersAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await Content(ctx).EnumerateBannersAsync(RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task CreateBannerAsync(HttpContextBase ctx)
        {
            BannerRequest request = RouteHelper.ReadJson<BannerRequest>(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            BannerView view = await Content(ctx).CreateBannerAsync(actor, request, Info(ctx), ctx.Token).ConfigureAwait(false);
            ctx.Response.Headers["ETag"] = view.ETag;
            await RouteHelper.SendJsonAsync(ctx, 201, view).ConfigureAwait(false);
        }

        private async Task GetBannerAsync(HttpContextBase ctx)
        {
            BannerDetailView view = await Content(ctx).GetBannerAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            ctx.Response.Headers["ETag"] = view.Banner.ETag;
            await RouteHelper.SendJsonAsync(ctx, 200, view).ConfigureAwait(false);
        }

        private async Task UpdateBannerAsync(HttpContextBase ctx)
        {
            BannerRequest request = RouteHelper.ReadJson<BannerRequest>(ctx, out HashSet<string> present);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            BannerView view = await Content(ctx).UpdateBannerAsync(actor, RouteHelper.Param(ctx, "id"), request, present, ctx.Request.RetrieveHeaderValue("If-Match"), Info(ctx), ctx.Token).ConfigureAwait(false);
            ctx.Response.Headers["ETag"] = view.ETag;
            await RouteHelper.SendJsonAsync(ctx, 200, view).ConfigureAwait(false);
        }

        private async Task ArchiveBannerAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await Content(ctx).ArchiveBannerAsync(RouteHelper.Param(ctx, "id"), true, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task RestoreBannerAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await Content(ctx).ArchiveBannerAsync(RouteHelper.Param(ctx, "id"), false, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteBannerAsync(HttpContextBase ctx)
        {
            await Content(ctx).DeleteBannerAsync(RouteHelper.Param(ctx, "id"), Info(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task BannerHistoryAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await Content(ctx).EnumerateBannerHistoryAsync(RouteHelper.Param(ctx, "id"), RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task AssetsAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await Content(ctx).EnumerateAssetsAsync(RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx), Origin(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task CreateAssetAsync(HttpContextBase ctx)
        {
            RemoteAssetRequest request = RouteHelper.ReadJson<RemoteAssetRequest>(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            RemoteAssetView view = await Content(ctx).CreateAssetAsync(actor, request, Origin(ctx), Info(ctx), ctx.Token).ConfigureAwait(false);
            ctx.Response.Headers["ETag"] = view.ETag;
            await RouteHelper.SendJsonAsync(ctx, 201, view).ConfigureAwait(false);
        }

        private async Task GetAssetAsync(HttpContextBase ctx)
        {
            RemoteAssetDetailView view = await Content(ctx).GetAssetAsync(RouteHelper.Param(ctx, "id"), Origin(ctx), ctx.Token).ConfigureAwait(false);
            ctx.Response.Headers["ETag"] = view.Asset.ETag;
            await RouteHelper.SendJsonAsync(ctx, 200, view).ConfigureAwait(false);
        }

        private async Task UpdateAssetAsync(HttpContextBase ctx)
        {
            RemoteAssetRequest request = RouteHelper.ReadJson<RemoteAssetRequest>(ctx, out HashSet<string> present);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            RemoteAssetView view = await Content(ctx).UpdateAssetAsync(actor, RouteHelper.Param(ctx, "id"), request, present, ctx.Request.RetrieveHeaderValue("If-Match"), Origin(ctx), Info(ctx), ctx.Token).ConfigureAwait(false);
            ctx.Response.Headers["ETag"] = view.ETag;
            await RouteHelper.SendJsonAsync(ctx, 200, view).ConfigureAwait(false);
        }

        private Task ContentAsync(HttpContextBase ctx)
        {
            return UploadAsync(ctx, false);
        }

        private Task DarkContentAsync(HttpContextBase ctx)
        {
            return UploadAsync(ctx, true);
        }

        private async Task UploadAsync(HttpContextBase ctx, bool dark)
        {
            Stream body = ctx.Request.ChunkedTransfer ? new ChunkedRequestStream(ctx.Request) : ctx.Request.Data;
            RemoteAssetView view = await Content(ctx).UploadAssetContentAsync(RouteHelper.Param(ctx, "id"), body, ctx.Request.ContentType, ctx.Request.ContentLength, dark, Origin(ctx), Info(ctx), ctx.Token).ConfigureAwait(false);
            ctx.Response.Headers["ETag"] = view.ETag;
            await RouteHelper.SendJsonAsync(ctx, 200, view).ConfigureAwait(false);
        }

        private async Task ArchiveAssetAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await Content(ctx).ArchiveAssetAsync(RouteHelper.Param(ctx, "id"), true, Origin(ctx), Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task RestoreAssetAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await Content(ctx).ArchiveAssetAsync(RouteHelper.Param(ctx, "id"), false, Origin(ctx), Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteAssetAsync(HttpContextBase ctx)
        {
            await Content(ctx).DeleteAssetAsync(RouteHelper.Param(ctx, "id"), Info(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task AssetHistoryAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await Content(ctx).EnumerateAssetHistoryAsync(RouteHelper.Param(ctx, "id"), RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx), Origin(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
