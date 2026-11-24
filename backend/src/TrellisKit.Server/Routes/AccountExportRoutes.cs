namespace TrellisKit.Server.Routes
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Asynchronous account data export routes: request (signed in), status (signed in, owner only), and the signed
    /// download (anonymous; the HMAC signature in the URL authorizes it).
    /// </summary>
    public class AccountExportRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/me/export";
        private const string _Tag = "Account";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public AccountExportRoutes(PropertyDirectory directory)
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
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, _Base, RequestAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Request a data export", _Tag, "Queues an export of everything stored about the member. One export at a time; at most 3 requests per rolling day. Signed in.")
                    .Returns("AccountExportCreated", 202).Resp(409, "ExportInProgress").Resp(429, "RateLimited"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, _Base + "/{id}", StatusAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a data export", _Tag, "Status Pending, Running, Ready, Failed, or Expired. When Ready, downloadUrl is a signed link valid for 15 minutes; read the export again for a fresh link. Owner only. Signed in.")
                    .PathParam("id", "Export identifier").Returns("AccountExport").Resp(404, "NotFound"));
            server.Routes.PreAuthentication.Parameter.Add(HttpMethod.GET, _Base + "/{id}/download", DownloadAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Download a data export", _Tag, "Anonymous; authorized by the exp and sig query parameters from downloadUrl (valid account.exportDownloadLinkMinutes). The file is read from the object store, so any server instance serves it; stored files are deleted after account.exportRetentionHours.")
                    .PathParam("id", "Export identifier").QueryParam("exp", "Expiry (Unix seconds)").QueryParam("sig", "Signature")
                    .File("application/json", "The export file").Resp(403, "InvalidSignature (bad or expired link)").Resp(404, "NotFound (not ready, expired, or deleted)"));
        }

        #endregion

        #region Private-Methods

        private async Task RequestAsync(HttpContextBase ctx)
        {
            string userId = RouteHelper.RequireUserId(ctx);
            await RouteHelper.SendJsonAsync(ctx, 202, await _Directory.For(ctx).AccountExports.RequestAsync(userId, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task StatusAsync(HttpContextBase ctx)
        {
            string userId = RouteHelper.RequireUserId(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).AccountExports.ReadAsync(userId, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DownloadAsync(HttpContextBase ctx)
        {
            string id = RouteHelper.Param(ctx, "id");
            using (AccountExportDownload download = await _Directory.For(ctx).AccountExports.AuthorizeDownloadAsync(id, RouteHelper.Query(ctx, "exp"), RouteHelper.Query(ctx, "sig"), ctx.Token).ConfigureAwait(false))
            {
                FileStream stream = new FileStream(download.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
                await using (stream.ConfigureAwait(false))
                {
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.Headers["Content-Disposition"] = "attachment; filename=\"" + _Directory.For(ctx).Property.Property.EffectiveSlug + "-export-" + download.ExportId + ".json\"";
                    ctx.Response.Headers["Cache-Control"] = "private, no-store";
                    await ctx.Response.Send(stream.Length, stream, ctx.Token).ConfigureAwait(false);
                }
            }
        }

        #endregion
    }
}
