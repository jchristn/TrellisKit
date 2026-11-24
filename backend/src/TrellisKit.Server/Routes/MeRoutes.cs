namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Security;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Account routes for the signed-in member (contract section 3). Signed in; no membership required.
    /// </summary>
    public class MeRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/me";
        private const string _Tag = "Account";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public MeRoutes(PropertyDirectory directory)
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
            post.Static.Add(HttpMethod.POST, _Base + "/age-attestation", AttestAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Attest age", _Tag, "Required once for social sign-ups. Under the property minimum age (membership.minimumAge) closes the account and signs out. Signed in.")
                    .Body("{ dateOfBirth: YYYY-MM-DD, acceptTerms?, acceptedDocuments?: [{ key, version }] } (documents are recorded after a successful attestation; docs/TRELLISKIT.md section 14)").Resp(200, "Me").Resp(400, "ValidationFailed").Resp(403, "AgeRequirementNotMet"));
            post.Static.Add(HttpMethod.PUT, _Base + "/profile", ProfileAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Update the profile", _Tag, "Any of displayName, handle, bio, rank, rankLevel, affiliation, city, region, country, goals, goalsOther, rankHistory (replaces the list), labels and tags (the member's own public labels and tags; each replaces the stored set; staff: entries are ignored; muted members get 403 Muted). Signed in.")
                    .Body("Partial profile").Resp(200, "Me").Resp(400, "ValidationFailed (fieldErrors.handle is \"That handle is taken.\" when not unique)"));
            post.Static.Add(HttpMethod.PUT, _Base + "/avatar", PutAvatarAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Upload an avatar", _Tag, "Raw image body with Content-Type image/jpeg, image/png, or image/webp, at most storage.maxAvatarBytes (5 MB). Signed in.")
                    .Resp(200, "Me").Resp(400, "InvalidImage").Resp(413, "TooLarge"));
            post.Static.Add(HttpMethod.DELETE, _Base + "/avatar", DeleteAvatarAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Remove the avatar", _Tag, "Signed in.").Resp(200, "Me"));
            post.Static.Add(HttpMethod.PUT, _Base + "/password", PasswordAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Change or set the password", _Tag, "currentPassword is required when one is set. Other sessions are revoked. Signed in.")
                    .Body("{ currentPassword?, newPassword }").Resp(204, "Changed").Resp(400, "ValidationFailed").Resp(401, "InvalidCredentials"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/identities/{provider}", LinkAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Link a sign-in method", _Tag, "Same body as the matching /auth/{provider} route. Signed in.").PathParam("provider", "apple, google, or facebook")
                    .Body("Provider token body").Resp(200, "Me").Resp(401, "InvalidIdentityToken").Resp(409, "IdentityInUse").Resp(503, "IntegrationNotConfigured"));
            post.Parameter.Add(HttpMethod.DELETE, _Base + "/identities/{provider}", UnlinkAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Unlink a sign-in method", _Tag, "Signed in.").PathParam("provider", "apple, google, or facebook").Resp(200, "Me").Resp(409, "LastSignInMethod"));
            post.Static.Add(HttpMethod.GET, _Base + "/appearance", GetAppearanceAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Your appearance settings", _Tag, "{ palettePreset (your stored choice: an engine preset id, or null for the community palette), effectivePreset (the preset that applies: palettePreset while it is still allowed, else null), presetsAvailable }. " +
                    "The presets you may choose, with their colors, are GET /api/v1/property brand.presets. Signed in.").Returns("AppearanceView").Resp(404, "NotFound"));
            post.Static.Add(HttpMethod.PUT, _Base + "/appearance", PutAppearanceAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Change your appearance settings", _Tag, "Body { palettePreset }: one of GET /api/v1/property brand.presets[].id, or null (or empty) for the community palette. Stored on your account, so it follows you to every device. " +
                    "400 ValidationFailed with fieldErrors.palettePreset for an unknown preset or one the community does not allow. Signed in.").Body("AppearanceRequest").Returns("AppearanceView").Resp(400, "ValidationFailed").Resp(404, "NotFound"));
            post.Static.Add(HttpMethod.GET, _Base + "/export", ExportAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Export my data (moved)", _Tag, "Replaced by the asynchronous export: POST /api/v1/me/export, then poll GET /api/v1/me/export/{id}. Always 410 ExportMoved. Signed in.")
                    .File("application/json", "Never returned; this route always answers 410 ExportMoved").Resp(410, "ExportMoved"));
            post.Static.Add(HttpMethod.DELETE, _Base, DeleteAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete my account", _Tag, "Anonymizes the account, revokes sessions, and cancels Stripe at period end. Signed in.")
                    .Body("{ confirm: \"DELETE\" }").Resp(202, "{ message, storeSubscriptionNote }").Resp(400, "ValidationFailed without the confirmation"));
        }

        #endregion

        #region Private-Methods

        private RequestInfo Info(HttpContextBase ctx)
        {
            return RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
        }

        private async Task GetAppearanceAsync(HttpContextBase ctx)
        {
            AppearanceView view = await _Directory.For(ctx).Branding.AppearanceAsync(RouteHelper.RequireUserId(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, view).ConfigureAwait(false);
        }

        private async Task PutAppearanceAsync(HttpContextBase ctx)
        {
            string userId = RouteHelper.RequireUserId(ctx);
            AppearanceRequest request = RouteHelper.ReadJson<AppearanceRequest>(ctx);
            AppearanceView view = await _Directory.For(ctx).Branding.UpdateAppearanceAsync(userId, request, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, view).ConfigureAwait(false);
        }

        private async Task AttestAsync(HttpContextBase ctx)
        {
            AgeAttestationRequest request = RouteHelper.ReadJson<AgeAttestationRequest>(ctx);
            MeResponse me = await _Directory.For(ctx).Accounts.AttestAgeAsync(RouteHelper.RequireUserId(ctx), request, Info(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, me).ConfigureAwait(false);
        }

        private async Task ProfileAsync(HttpContextBase ctx)
        {
            ProfileUpdateRequest request = RouteHelper.ReadJson<ProfileUpdateRequest>(ctx, out HashSet<string> present);
            string userId = RouteHelper.RequireUserId(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            MeResponse me = await _Directory.For(ctx).Profiles.UpdateAsync(userId, request, present, actor, ctx.Token, RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest)).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, me).ConfigureAwait(false);
        }

        private async Task PutAvatarAsync(HttpContextBase ctx)
        {
            string userId = RouteHelper.RequireUserId(ctx);
            int max = _Directory.For(ctx).Settings.Storage.MaxAvatarBytes;
            // Bodies a little over the limit are read so the client sees the 413 instead of a reset connection;
            // anything far larger is refused before reading.
            if (ctx.Request.ContentLength > (long)max * 4) throw ProfileService.TooLarge(max);
            byte[] data = await ctx.Request.ReadBodyAsync(ctx.Token).ConfigureAwait(false) ?? Array.Empty<byte>();
            MeResponse me = await _Directory.For(ctx).Profiles.SetAvatarAsync(userId, ctx.Request.ContentType, data, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, me).ConfigureAwait(false);
        }

        private async Task DeleteAvatarAsync(HttpContextBase ctx)
        {
            MeResponse me = await _Directory.For(ctx).Profiles.DeleteAvatarAsync(RouteHelper.RequireUserId(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, me).ConfigureAwait(false);
        }

        private async Task PasswordAsync(HttpContextBase ctx)
        {
            ChangePasswordRequest request = RouteHelper.ReadJson<ChangePasswordRequest>(ctx);
            RequestContext? context = ctx.Metadata as RequestContext;
            await _Directory.For(ctx).Accounts.ChangePasswordAsync(RouteHelper.RequireUserId(ctx), context?.SessionId, request, Info(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task LinkAsync(HttpContextBase ctx)
        {
            string userId = RouteHelper.RequireUserId(ctx);
            ExternalProviderEnum provider = ParseProvider(RouteHelper.Param(ctx, "provider"));
            MeResponse me;
            switch (provider)
            {
                case ExternalProviderEnum.Apple:
                    AppleSignInRequest apple = RouteHelper.ReadJson<AppleSignInRequest>(ctx);
                    me = await _Directory.For(ctx).Accounts.LinkIdentityAsync(userId, provider, apple.IdentityToken, ExternalTokenTypeEnum.Jwt, apple.Nonce, Info(ctx), ctx.Token).ConfigureAwait(false);
                    break;
                case ExternalProviderEnum.Google:
                    GoogleSignInRequest google = RouteHelper.ReadJson<GoogleSignInRequest>(ctx);
                    me = await _Directory.For(ctx).Accounts.LinkIdentityAsync(userId, provider, google.IdToken, ExternalTokenTypeEnum.Jwt, null, Info(ctx), ctx.Token).ConfigureAwait(false);
                    break;
                default:
                    FacebookSignInRequest facebook = RouteHelper.ReadJson<FacebookSignInRequest>(ctx);
                    me = await _Directory.For(ctx).Accounts.LinkIdentityAsync(userId, provider, facebook.Token, facebook.TokenType ?? ExternalTokenTypeEnum.AccessToken, facebook.Nonce, Info(ctx), ctx.Token).ConfigureAwait(false);
                    break;
            }
            await RouteHelper.SendJsonAsync(ctx, 200, me).ConfigureAwait(false);
        }

        private async Task UnlinkAsync(HttpContextBase ctx)
        {
            string userId = RouteHelper.RequireUserId(ctx);
            ExternalProviderEnum provider = ParseProvider(RouteHelper.Param(ctx, "provider"));
            MeResponse me = await _Directory.For(ctx).Accounts.UnlinkIdentityAsync(userId, provider, Info(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, me).ConfigureAwait(false);
        }

        private Task ExportAsync(HttpContextBase ctx)
        {
            throw new ApiErrorException(410, "ExportMoved", "Account exports are asynchronous: POST /api/v1/me/export, then poll GET /api/v1/me/export/{id} for the download link.");
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            string userId = RouteHelper.RequireUserId(ctx);
            DeleteAccountRequest request = RouteHelper.ReadJson<DeleteAccountRequest>(ctx);
            if (!String.Equals(request.Confirm, "DELETE", StringComparison.Ordinal)) throw ApiErrorException.Field("confirm", "Type DELETE to confirm.");
            DeleteAccountResponse response = await _Directory.For(ctx).Accounts.DeleteAccountAsync(userId, Info(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 202, response).ConfigureAwait(false);
        }

        private static ExternalProviderEnum ParseProvider(string value)
        {
            switch ((value ?? String.Empty).Trim().ToLowerInvariant())
            {
                case "apple":
                    return ExternalProviderEnum.Apple;
                case "google":
                    return ExternalProviderEnum.Google;
                case "facebook":
                    return ExternalProviderEnum.Facebook;
                default:
                    throw ApiErrorException.NotFound("Provider");
            }
        }

        #endregion
    }
}
