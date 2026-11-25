namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Media routes: Phase 1 avatars (anonymous), Phase 2 upload sessions, complete, status, alt text, delete, and the
    /// media grant (Member), plus the Local mode signed upload routes and the file route (anonymous; they verify their
    /// own signature, cookie, or bearer token).
    /// </summary>
    public class MediaRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/media";
        private const string _Tag = "Media";
        private const string _Member = " Member.";
        private const string _CacheControl = "private, max-age=31536000, immutable";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public MediaRoutes(PropertyDirectory directory)
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
            WatsonWebserver.Core.Routing.RoutingGroup pre = server.Routes.PreAuthentication;
            WatsonWebserver.Core.Routing.RoutingGroup post = server.Routes.PostAuthentication;

            pre.Parameter.Add(HttpMethod.GET, _Base + "/avatars/{file}", GetAvatarAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read an avatar image", _Tag, "Image bytes with a long immutable cache lifetime (file names change on every upload). Anonymous in Phase 1.")
                    .PathParam("file", "File name from avatarUrl").File("image/*", "Image").Resp(404, "NotFound"));

            pre.Parameter.Add(HttpMethod.PUT, _Base + "/local-uploads/{id}", LocalSingleAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Local mode single upload", _Tag, "Signed URL from an upload session (exp, sig). Send the declared Content-Type and the raw bytes; no Authorization header. Anonymous (signature-verified); Local mode only.")
                    .PathParam("id", "Media identifier").QueryParam("exp", "Unix expiry").QueryParam("sig", "Signature")
                    .Resp(200, "Stored; ETag header carries the quoted MD5").Resp(403, "InvalidSignature").Resp(404, "NotFound").Resp(409, "UploadIncomplete (session closed)").Resp(410, "UploadExpired").Resp(413, "TooLarge").Resp(415, "UnsupportedMediaType"));
            pre.Parameter.Add(HttpMethod.PUT, _Base + "/local-uploads/{id}/parts/{partNumber}", LocalPartAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Local mode part upload", _Tag, "Signed URL of one multipart part. Every part except the last is exactly partSizeBytes. Anonymous (signature-verified); Local mode only.")
                    .PathParam("id", "Media identifier").PathParam("partNumber", "One-based part number").QueryParam("exp", "Unix expiry").QueryParam("sig", "Signature")
                    .Resp(200, "Stored; ETag header carries the quoted MD5").Resp(403, "InvalidSignature").Resp(404, "NotFound").Resp(410, "UploadExpired").Resp(413, "TooLarge"));

            pre.Parameter.Add(HttpMethod.GET, _Base + "/gallery/{id}/{file}", GetGalleryFileAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a public gallery output", _Tag, "Anonymous. Re-checks the public venue/profile page and live item on every request. Processed outputs only, no-store, Range supported in Local and S3 storage.")
                    .PathParam("id", "Media identifier").PathParam("file", "Flat output file name").File("application/octet-stream", "Media bytes").Resp(206, "Partial content").Resp(404, "NotFound").Resp(416, "RangeNotSatisfiable"));
            pre.Parameter.Add(HttpMethod.GET, _Base + "/files/public/{id}/{file}", GetPublicFileAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a public media file", _Tag, "Public copies of media on Public posts. Anonymous; Local mode only (production serves /public/media/ and /public/video/ from the media CDN). Supports Range.")
                    .PathParam("id", "Media identifier").PathParam("file", "Flat file name").File("application/octet-stream", "Media bytes").Resp(206, "Partial content").Resp(404, "NotFound").Resp(416, "RangeNotSatisfiable"));
            pre.Parameter.Add(HttpMethod.GET, _Base + "/files/link-previews/{hash}/{file}", GetLinkPreviewFileAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a link preview image", _Tag, "Re-encoded preview image of a fetched page. Anonymous (the content comes from public web pages); Local mode only.")
                    .PathParam("hash", "URL hash").PathParam("file", "card.jpg").File("image/jpeg", "Image").Resp(404, "NotFound"));
            pre.Parameter.Add(HttpMethod.GET, _Base + "/private/{token}/{file}", GetPrivateFileAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a private media file", _Tag, "Ticket and direct message attachments (purpose Ticket or Message) through per-object URLs from their payloads: the token names one media item, its scope, the viewer, and an expiry of messaging.mediaUrlMinutes. Every request re-checks that the viewer may still see the scope. Never readable through the media grant cookie. HLS segments resolve relative to the playlist under the same token. Supports Range. Anonymous (token-verified).")
                    .PathParam("token", "Per-object URL token").PathParam("file", "Flat file name").File("application/octet-stream", "Media bytes").Resp(206, "Partial content")
                    .Resp(403, "InvalidSignature (forged, foreign, or expired URL, or the viewer lost access)").Resp(404, "NotFound").Resp(416, "RangeNotSatisfiable"));
            pre.Parameter.Add(HttpMethod.GET, _Base + "/files/{id}/{file}", GetFileAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a media file", _Tag, "Variants, posters, HLS playlists and segments, and passthrough MP4 (images folder, then transcoded). Accepts the tk_media grant cookie (browser cookie or a plain Cookie header) or a bearer access token whose session has a membership. Supports Range (206). Local mode only.")
                    .PathParam("id", "Media identifier").PathParam("file", "Flat file name").File("application/octet-stream", "Media bytes").Resp(206, "Partial content").Resp(403, "InvalidSignature (no valid grant)").Resp(404, "NotFound").Resp(416, "RangeNotSatisfiable"));

            post.Static.Add(HttpMethod.POST, _Base + "/uploads", CreateUploadAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Create an upload session", _Tag, "Images (image/jpeg, image/png, image/webp) upload with one PUT; videos (video/mp4, video/quicktime) above media.multipartThresholdMegabytes upload in parts. Optional labels and tags." + _Member)
                    .Body("{ kind, contentType, sizeBytes, fileName?, durationSeconds? (video), labels?, tags? }").Returns("UploadSession", 201)
                    .Resp(400, "ValidationFailed or VideoTooLong").Resp(403, "Muted, PostingNotAllowed, MediaNotAllowed, or VideoUploadNotAllowed").Resp(413, "TooLarge").Resp(415, "UnsupportedMediaType")
                    .Resp(429, "RateLimited (media.uploadsPerHour or the daily quota), with Retry-After").Resp(503, "IntegrationNotConfigured"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/upload-urls", UploadUrlsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Refresh upload URLs", _Tag, "Fresh URLs plus uploadedParts (the parts storage already has) so an interrupted client uploads only the missing parts. Owner, status Uploading." + _Member)
                    .PathParam("id", "Media identifier").Returns("UploadUrls").Resp(404, "NotFound").Resp(409, "UploadIncomplete (no longer Uploading)").Resp(410, "UploadExpired"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/complete", CompleteAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Complete an upload", _Tag, "Checks parts and size, sniffs the format, reads the video duration, and queues processing. Calling it again on a Processing, Ready, or Failed item returns 200 unchanged. Owner." + _Member)
                    .PathParam("id", "Media identifier").Body("{ parts: [{ partNumber, etag }] } (multipart only)", false).Returns("Media", 202).Resp(200, "Media (already completed)")
                    .Resp(400, "ValidationFailed or VideoTooLong").Resp(404, "NotFound").Resp(409, "UploadIncomplete").Resp(410, "UploadExpired").Resp(415, "UnsupportedMediaType"));
            post.Static.Add(HttpMethod.POST, _Base + "/grant", GrantAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Grant media access", _Tag, "Sets the media cookies (CloudFront signed cookies in S3 mode, tk_media in Local mode) and returns them for mobile clients, which send them as a Cookie header." + _Member)
                    .Returns("MediaGrant").Resp(429, "RateLimited").Resp(503, "IntegrationNotConfigured"));
            EnumerationRoutes.Add(post, _Base, AdministrativeCatalogs.MediaBatch, "MediaView",
                () => Doc("Enumerate requested media", _Tag, "filters.ids is a comma-separated string or array of at most 20 requested ids. Returns only visible objects, with standard paging, search and metadata filters."),
                async (ctx, query) =>
                {
                    ApplicationServices services = _Directory.For(ctx);
                    ContentActor actor = await services.Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
                    services.Media.EnforceRead(actor);
                    EnumerationRequest request = services.Enumeration.Prepare(query, AdministrativeCatalogs.MediaBatch, false, actor.UserId ?? "anonymous", 20);
                    JsonElement value = request.Query.Filters.FirstOrDefault(pair => String.Equals(pair.Key, "ids", StringComparison.OrdinalIgnoreCase)).Value;
                    List<string> supplied = value.ValueKind switch
                    {
                        JsonValueKind.Undefined or JsonValueKind.Null => new List<string>(),
                        JsonValueKind.String => new List<string> { value.GetString() ?? String.Empty },
                        JsonValueKind.Array when value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String) => value.EnumerateArray().Select(item => item.GetString() ?? String.Empty).ToList(),
                        _ => throw new ApiErrorException(400, "ValidationFailed", "filters.ids must be a string or an array of strings.")
                    };
                    List<string> ids = supplied.SelectMany(item => item.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Distinct(StringComparer.Ordinal).ToList();
                    MediaBatchResponse result = await services.Media.GetManyAsync(actor, ids, ctx.Token).ConfigureAwait(false);
                    return CatalogEnumeration.Page(request, result.Items, item => item.Id, item => item.CreatedUtc, item => item.Id + " " + item.Kind + " " + item.Status, item => item.Labels, item => item.Tags);
                });
            post.Parameter.Add(HttpMethod.GET, _Base + "/{id}", GetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read media", _Tag, "Owner, SystemAdmin, or any member who can see the content it is attached to." + _Member)
                    .PathParam("id", "Media identifier").Returns("Media").Resp(404, "NotFound").Resp(429, "RateLimited"));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/{id}", UpdateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Update alt text, labels, and tags", _Tag, "Allowed in any status and after attachment. Owner or SystemAdmin." + _Member)
                    .PathParam("id", "Media identifier").Body("{ altText?, labels?, tags? }").Returns("Media").Resp(400, "ValidationFailed or ContentFiltered").Resp(403, "Muted").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.DELETE, _Base + "/{id}", DeleteAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete media", _Tag, "Owner (unattached media only) or SystemAdmin (detaches it first). Deletes every stored object including the original." + _Member)
                    .PathParam("id", "Media identifier").Resp(204, "Deleted").Resp(403, "Forbidden (attached; edit the post or comment instead)").Resp(404, "NotFound"));
        }

        /// <summary>
        /// Origin of a request for absolute Local mode URLs (scheme from rest.ssl or X-Forwarded-Proto; host from
        /// X-Forwarded-Host or Host).
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="ssl">Whether the server itself listens with TLS.</param>
        /// <returns>Origin, for example http://127.0.0.1:8000, or empty when the request has no Host.</returns>
        public static string RequestOrigin(HttpContextBase ctx, bool ssl)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            string? proto = ctx.Request.RetrieveHeaderValue("X-Forwarded-Proto");
            string scheme = ssl || String.Equals(proto?.Split(',')[0].Trim(), "https", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
            string? host = ctx.Request.RetrieveHeaderValue("X-Forwarded-Host");
            if (String.IsNullOrWhiteSpace(host)) host = ctx.Request.RetrieveHeaderValue("Host");
            if (String.IsNullOrWhiteSpace(host)) return String.Empty;
            return scheme + "://" + host.Split(',')[0].Trim();
        }

        /// <summary>
        /// A cookie value from the Cookie request header (works for browsers and for mobile clients that send the
        /// header themselves).
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="name">Cookie name.</param>
        /// <returns>Value, or null.</returns>
        public static string? ReadCookie(HttpContextBase ctx, string name)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            string? header = ctx.Request.RetrieveHeaderValue("Cookie");
            if (String.IsNullOrEmpty(header)) return null;
            foreach (string part in header.Split(';'))
            {
                int equals = part.IndexOf('=');
                if (equals <= 0) continue;
                if (String.Equals(part.Substring(0, equals).Trim(), name, StringComparison.Ordinal)) return part.Substring(equals + 1).Trim().Trim('"');
            }
            return null;
        }

        /// <summary>
        /// Serve a local media file with the media headers and Range support (used by the signed lesson download route).
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="path">Local path, or null for 404.</param>
        /// <param name="file">File name (content type and headers).</param>
        /// <returns>Task.</returns>
        public static Task ServeFileAsync(HttpContextBase ctx, string? path, string file)
        {
            return ServeAsync(ctx, path, file);
        }

        /// <summary>
        /// Set-Cookie header value for a media cookie (Secure when the cookie asks for it, HttpOnly, SameSite=Lax).
        /// </summary>
        /// <param name="cookie">Cookie.</param>
        /// <returns>Header value.</returns>
        /// <exception cref="ArgumentNullException">The cookie is null.</exception>
        public static string SetCookieValue(MediaCookie cookie)
        {
            ArgumentNullException.ThrowIfNull(cookie);
            long maxAge = Math.Max(0, (long)(cookie.ExpiresUtc - DateTime.UtcNow).TotalSeconds);
            return cookie.Name + "=" + cookie.Value + (cookie.Domain == null ? String.Empty : "; Domain=" + cookie.Domain) + "; Path=" + cookie.Path +
                "; Expires=" + cookie.ExpiresUtc.ToString("R", CultureInfo.InvariantCulture) + "; Max-Age=" + maxAge.ToString(CultureInfo.InvariantCulture) +
                (cookie.Secure ? "; Secure" : String.Empty) + "; HttpOnly; SameSite=Lax";
        }

        #endregion

        #region Private-Methods

        private async Task GetAvatarAsync(HttpContextBase ctx)
        {
            string file = RouteHelper.Param(ctx, "file");
            string? path = _Directory.For(ctx).Avatars.Resolve(file);
            if (path == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Avatar not found.").ConfigureAwait(false);
                return;
            }
            byte[] data = await File.ReadAllBytesAsync(path, ctx.Token).ConfigureAwait(false);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = AvatarStore.ContentTypeFor(Path.GetExtension(path).TrimStart('.'));
            ctx.Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            await ctx.Response.Send(data, ctx.Token).ConfigureAwait(false);
        }

        private Task LocalSingleAsync(HttpContextBase ctx)
        {
            return LocalUploadAsync(ctx, null);
        }

        private async Task LocalPartAsync(HttpContextBase ctx)
        {
            string raw = RouteHelper.Param(ctx, "partNumber");
            if (!Int32.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int part) || part < 1) throw Error("NotFound", 404, "Upload part not found.");
            await LocalUploadAsync(ctx, part).ConfigureAwait(false);
        }

        private async Task LocalUploadAsync(HttpContextBase ctx, int? partNumber)
        {
            if (ctx.Request.ChunkedTransfer) throw Error("LengthRequired", 411, "Send the body with a Content-Length.");
            string id = RouteHelper.Param(ctx, "id");
            string path = ctx.Request.Url.RawWithoutQuery;
            StoredPart stored = await _Directory.For(ctx).Media.ReceiveLocalUploadAsync(id, partNumber, path, RouteHelper.Query(ctx, "exp"), RouteHelper.Query(ctx, "sig"),
                ctx.Request.ContentType, ctx.Request.ContentLength, ctx.Request.Data, ctx.Token).ConfigureAwait(false);
            ctx.Response.Headers["ETag"] = stored.ETag;
            await RouteHelper.SendEmptyAsync(ctx, 200).ConfigureAwait(false);
        }

        private async Task GetFileAsync(HttpContextBase ctx)
        {
            string? bearer = null;
            string? authorization = ctx.Request.RetrieveHeaderValue("Authorization");
            if (authorization != null && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) bearer = authorization.Substring(7).Trim();
            if (!await _Directory.For(ctx).Media.CanReadFilesAsync(ReadCookie(ctx, Constants.MediaCookieName), bearer, ctx.Token).ConfigureAwait(false))
            {
                await RouteHelper.SendErrorAsync(ctx, 403, "InvalidSignature", "A media grant (POST /api/v1/media/grant) or a member access token is required.").ConfigureAwait(false);
                return;
            }
            string file = RouteHelper.Param(ctx, "file");
            if (await _Directory.For(ctx).Media.IsPrivateAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false))
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "File not found.").ConfigureAwait(false);
                return;
            }
            await ServeAsync(ctx, _Directory.For(ctx).Media.ResolveLocalFile(RouteHelper.Param(ctx, "id"), file, false), file).ConfigureAwait(false);
        }

        private async Task GetGalleryFileAsync(HttpContextBase ctx)
        {
            ApplicationServices services = _Directory.For(ctx);
            MediaAttachment? item = await services.Database.Media.ReadAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            if (item == null || !await services.PublicPages.GalleryMediaVisibleAsync(item, ctx.Token).ConfigureAwait(false)) throw ApiErrorException.NotFound("File");
            PrivateMediaFile resolved = await services.Media.ResolveGalleryFileAsync(item.Id, RouteHelper.Param(ctx, "file"), ctx.Token).ConfigureAwait(false);
            await ServeObjectAsync(ctx, services.Media, resolved, "no-store").ConfigureAwait(false);
        }

        private async Task GetPrivateFileAsync(HttpContextBase ctx)
        {
            MediaService media = _Directory.For(ctx).Media;
            PrivateMediaFile resolved = await media.ResolvePrivateFileAsync(RouteHelper.Param(ctx, "token"), RouteHelper.Param(ctx, "file"), ctx.Token).ConfigureAwait(false);
            long maxAge = Math.Max(0, Math.Min(3600, (long)(resolved.ExpiresUtc - DateTime.UtcNow).TotalSeconds));
            await ServeObjectAsync(ctx, media, resolved, "private, max-age=" + maxAge.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
        }

        private static async Task ServeObjectAsync(HttpContextBase ctx, MediaService media, PrivateMediaFile resolved, string cacheControl)
        {
            long length = resolved.SizeBytes;
            ctx.Response.Headers["Accept-Ranges"] = "bytes";
            ctx.Response.Headers["Cache-Control"] = cacheControl;
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
            ctx.Response.ContentType = resolved.ContentType;
            long start = 0;
            long end = length - 1;
            string? range = ctx.Request.RetrieveHeaderValue("Range");
            if (!String.IsNullOrWhiteSpace(range))
            {
                if (!TryParseRange(range, length, out start, out end))
                {
                    ctx.Response.Headers["Content-Range"] = "bytes */" + length.ToString(CultureInfo.InvariantCulture);
                    await RouteHelper.SendErrorAsync(ctx, 416, "RangeNotSatisfiable", "The requested range is not satisfiable.").ConfigureAwait(false);
                    return;
                }
                ctx.Response.StatusCode = 206;
                ctx.Response.Headers["Content-Range"] = "bytes " + start.ToString(CultureInfo.InvariantCulture) + "-" + end.ToString(CultureInfo.InvariantCulture) + "/" + length.ToString(CultureInfo.InvariantCulture);
            }
            else ctx.Response.StatusCode = 200;
            long count = length == 0 ? 0 : end - start + 1;
            using (ObjectRangeStream stream = new ObjectRangeStream((offset, size, token) => media.ReadPrivateRangeAsync(resolved, offset, size, token), start, start + count))
            {
                await ctx.Response.Send(count, stream, ctx.Token).ConfigureAwait(false);
            }
        }

        private async Task GetPublicFileAsync(HttpContextBase ctx)
        {
            string file = RouteHelper.Param(ctx, "file");
            await ServeAsync(ctx, _Directory.For(ctx).Media.ResolveLocalFile(RouteHelper.Param(ctx, "id"), file, true), file).ConfigureAwait(false);
        }

        private async Task GetLinkPreviewFileAsync(HttpContextBase ctx)
        {
            string file = RouteHelper.Param(ctx, "file");
            await ServeAsync(ctx, _Directory.For(ctx).Media.ResolveLinkPreviewFile(RouteHelper.Param(ctx, "hash"), file), file).ConfigureAwait(false);
        }

        private static async Task ServeAsync(HttpContextBase ctx, string? path, string file)
        {
            if (path == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "File not found.").ConfigureAwait(false);
                return;
            }
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            {
                long length = stream.Length;
                ctx.Response.Headers["Accept-Ranges"] = "bytes";
                ctx.Response.Headers["Cache-Control"] = _CacheControl;
                ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
                ctx.Response.ContentType = MediaKeys.ContentTypeFor(file);
                if (file.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    // A PDF must never run script in the media origin (docs/PHASE4_CONTRACT.md section 5.3).
                    ctx.Response.Headers["Content-Disposition"] = "attachment; filename=\"" + file + "\"";
                    ctx.Response.Headers["Content-Security-Policy"] = "sandbox";
                }
                string? range = ctx.Request.RetrieveHeaderValue("Range");
                if (String.IsNullOrWhiteSpace(range))
                {
                    ctx.Response.StatusCode = 200;
                    await ctx.Response.Send(length, stream, ctx.Token).ConfigureAwait(false);
                    return;
                }
                if (!TryParseRange(range, length, out long start, out long end))
                {
                    ctx.Response.Headers["Content-Range"] = "bytes */" + length.ToString(CultureInfo.InvariantCulture);
                    await RouteHelper.SendErrorAsync(ctx, 416, "RangeNotSatisfiable", "The requested range is not satisfiable.").ConfigureAwait(false);
                    return;
                }
                stream.Position = start;
                ctx.Response.StatusCode = 206;
                ctx.Response.Headers["Content-Range"] = "bytes " + start.ToString(CultureInfo.InvariantCulture) + "-" + end.ToString(CultureInfo.InvariantCulture) + "/" + length.ToString(CultureInfo.InvariantCulture);
                await ctx.Response.Send(end - start + 1, stream, ctx.Token).ConfigureAwait(false);
            }
        }

        private static bool TryParseRange(string header, long length, out long start, out long end)
        {
            start = 0;
            end = length - 1;
            string value = header.Trim();
            if (!value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) || length == 0) return false;
            string spec = value.Substring(6).Split(',')[0].Trim();
            int dash = spec.IndexOf('-');
            if (dash < 0) return false;
            string first = spec.Substring(0, dash).Trim();
            string last = spec.Substring(dash + 1).Trim();
            if (first.Length == 0)
            {
                if (!Int64.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out long suffix) || suffix <= 0) return false;
                start = Math.Max(0, length - suffix);
                return true;
            }
            if (!Int64.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out start) || start >= length) return false;
            if (last.Length > 0)
            {
                if (!Int64.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out end) || end < start) return false;
                end = Math.Min(end, length - 1);
            }
            return true;
        }

        private static ApiErrorException Error(string error, int status, string message)
        {
            return new ApiErrorException(status, error, message);
        }

        private async Task CreateUploadAsync(HttpContextBase ctx)
        {
            MediaUploadRequest request = RouteHelper.ReadJson<MediaUploadRequest>(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            UploadSessionResponse session = await _Directory.For(ctx).Media.CreateUploadAsync(actor, request, RequestOrigin(ctx, _Directory.For(ctx).Settings.Rest.Ssl), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, session).ConfigureAwait(false);
        }

        private async Task UploadUrlsAsync(HttpContextBase ctx)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            UploadUrlsResponse response = await _Directory.For(ctx).Media.RefreshUploadUrlsAsync(actor, RouteHelper.Param(ctx, "id"), RequestOrigin(ctx, _Directory.For(ctx).Settings.Rest.Ssl), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, response).ConfigureAwait(false);
        }

        private async Task CompleteAsync(HttpContextBase ctx)
        {
            MediaCompleteRequest request = RouteHelper.ReadJson<MediaCompleteRequest>(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            MediaCompleteOutcome outcome = await _Directory.For(ctx).Media.CompleteAsync(actor, RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, outcome.Accepted ? 202 : 200, outcome.Media).ConfigureAwait(false);
        }

        private async Task GrantAsync(HttpContextBase ctx)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            RequestInfo info = RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
            MediaGrantResponse grant = await _Directory.For(ctx).Media.GrantAsync(actor, info.IsInsecureLoopback, ctx.Token).ConfigureAwait(false);
            foreach (MediaCookie cookie in grant.Cookies) ctx.Response.Headers.Add("Set-Cookie", SetCookieValue(cookie));
            ctx.Response.Headers["Cache-Control"] = "no-store";
            await RouteHelper.SendJsonAsync(ctx, 200, grant).ConfigureAwait(false);
        }

        private async Task BatchAsync(HttpContextBase ctx)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            _Directory.For(ctx).Media.EnforceRead(actor);
            List<string> ids = (RouteHelper.Query(ctx, "ids") ?? String.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            MediaBatchResponse response = await _Directory.For(ctx).Media.GetManyAsync(actor, ids, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, response).ConfigureAwait(false);
        }

        private async Task GetAsync(HttpContextBase ctx)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            _Directory.For(ctx).Media.EnforceRead(actor);
            MediaView media = await _Directory.For(ctx).Media.GetAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, media).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase ctx)
        {
            MediaUpdateRequest request = RouteHelper.ReadJson<MediaUpdateRequest>(ctx, out HashSet<string> present);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            MediaView media = await _Directory.For(ctx).Media.UpdateAsync(actor, RouteHelper.Param(ctx, "id"), request, present, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, media).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await _Directory.For(ctx).Media.DeleteAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        #endregion
    }
}
