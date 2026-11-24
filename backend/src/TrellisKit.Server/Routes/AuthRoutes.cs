namespace TrellisKit.Server.Routes
{
    using System;
    using System.Globalization;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.Routing;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Authentication routes (contract section 2). Web clients (X-Client: web) get the refresh token as the HttpOnly
    /// tk_refresh cookie on path /api/v1/auth and refreshToken null in the body; other clients get it in the body.
    /// </summary>
    public class AuthRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/auth";
        private const string _Tag = "Auth";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public AuthRoutes(PropertyDirectory directory)
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
            RoutingGroup pre = server.Routes.PreAuthentication;
            const string clientNote = " Send X-Client: web or mobile. Web clients receive the refresh token as the tk_refresh cookie and refreshToken null.";

            pre.Static.Add(HttpMethod.POST, _Base + "/register", RegisterAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Register with email and password", _Tag, "Creates an account (the property minimum age, membership.minimumAge, or older) and sends a verification email. Anonymous." + clientNote)
                    .Body("{ email, password, displayName, dateOfBirth: YYYY-MM-DD, acceptTerms: true, acceptedDocuments: [{ key, version }] (every RequiredAtSignup document with separateCheckbox, at its current version; fieldErrors acceptedDocuments.<key> otherwise) }")
                    .Resp(201, "AuthResponse (isNewAccount true)").Resp(400, "ValidationFailed").Resp(403, "AgeRequirementNotMet").Resp(409, "EmailInUse"));
            pre.Static.Add(HttpMethod.POST, _Base + "/login", LoginAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Sign in with email and password", _Tag, "Ten failures in 15 minutes lock password sign-in. Anonymous." + clientNote)
                    .Body("{ email, password }").Resp(200, "AuthResponse").Resp(401, "InvalidCredentials").Resp(403, "AccountBanned").Resp(423, "AccountLocked"));
            pre.Static.Add(HttpMethod.POST, _Base + "/apple", AppleAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Social("Sign in with Apple", "{ identityToken, nonce (raw), givenName?, familyName? }", clientNote));
            pre.Static.Add(HttpMethod.POST, _Base + "/google", GoogleAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Social("Sign in with Google", "{ idToken }", clientNote));
            pre.Static.Add(HttpMethod.POST, _Base + "/facebook", FacebookAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Social("Sign in with Facebook", "{ token, tokenType: Jwt | AccessToken, nonce? (raw, iOS Limited Login) }", clientNote));
            pre.Static.Add(HttpMethod.POST, _Base + "/refresh", RefreshAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Rotate the refresh token", _Tag, "Mobile sends { refreshToken }; web sends no body (cookie). Reusing an old refresh token revokes the session. Anonymous.")
                    .Body("{ refreshToken } (mobile only)", false).Resp(200, "AuthResponse").Resp(401, "InvalidRefreshToken"));
            pre.Static.Add(HttpMethod.POST, _Base + "/logout", LogoutAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Sign out", _Tag, "Revokes the session of the refresh token (body or cookie) and clears the web cookie. Always 204. Anonymous.")
                    .Body("{ refreshToken } (mobile only)", false).Resp(204, "Signed out"));
            pre.Static.Add(HttpMethod.POST, _Base + "/password/forgot", ForgotAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Request a password reset email", _Tag, "Always 202 so accounts cannot be discovered. Anonymous.")
                    .Body("{ email }").Resp(202, "Accepted").Resp(429, "RateLimited"));
            pre.Static.Add(HttpMethod.POST, _Base + "/password/reset", ResetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Reset a password", _Tag, "Uses the emailed token (single use, 1 hour) and revokes every session. Anonymous.")
                    .Body("{ token, newPassword }").Resp(204, "Password reset").Resp(400, "InvalidToken or ValidationFailed"));
            pre.Static.Add(HttpMethod.POST, _Base + "/email/verify", VerifyAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Verify an email address", _Tag, "Uses the emailed token (single use, 48 hours). Anonymous.")
                    .Body("{ token }").Resp(204, "Verified").Resp(400, "InvalidToken"));

            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, _Base + "/me", MeAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("The signed-in member", _Tag, "Returns Me. Signed in.").Bounded("Me", "goals, rankHistory, signInMethods, and roles are bounded by the property configuration, the provider count, and 20 role assignments; labels and tags at most 50 each").Resp(401, "Unauthorized"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, _Base + "/email/resend", ResendAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Resend the verification email", _Tag, "Signed in.").Resp(202, "Accepted").Resp(429, "RateLimited"));
        }

        #endregion

        #region Private-Methods

        private static WatsonWebserver.Core.OpenApi.OpenApiRouteMetadata Social(string summary, string body, string clientNote)
        {
            return Doc(summary, _Tag, "Signs in or creates an account (new accounts must call POST /api/v1/me/age-attestation). Anonymous." + clientNote)
                .Body(body).Resp(200, "AuthResponse").Resp(401, "InvalidIdentityToken").Resp(403, "AccountBanned")
                .Resp(409, "AccountLinkRequired (body adds existingMethods)").Resp(503, "IntegrationNotConfigured");
        }

        private RequestInfo Info(HttpContextBase ctx)
        {
            return RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
        }

        private async Task RegisterAsync(HttpContextBase ctx)
        {
            RegisterRequest request = RouteHelper.ReadJson<RegisterRequest>(ctx);
            RequestInfo info = Info(ctx);
            IssuedSession issued = await _Directory.For(ctx).Accounts.RegisterAsync(request, info, ctx.Token).ConfigureAwait(false);
            await SendSessionAsync(ctx, 201, issued, info, info.ClientType == ClientTypeEnum.Web).ConfigureAwait(false);
        }

        private async Task LoginAsync(HttpContextBase ctx)
        {
            LoginRequest request = RouteHelper.ReadJson<LoginRequest>(ctx);
            RequestInfo info = Info(ctx);
            IssuedSession issued = await _Directory.For(ctx).Accounts.LoginAsync(request, info, ctx.Token).ConfigureAwait(false);
            await SendSessionAsync(ctx, 200, issued, info, info.ClientType == ClientTypeEnum.Web).ConfigureAwait(false);
        }

        private async Task AppleAsync(HttpContextBase ctx)
        {
            AppleSignInRequest request = RouteHelper.ReadJson<AppleSignInRequest>(ctx);
            RequestInfo info = Info(ctx);
            IssuedSession issued = await _Directory.For(ctx).Accounts.SocialSignInAsync(ExternalProviderEnum.Apple, request.IdentityToken, ExternalTokenTypeEnum.Jwt, request.Nonce,
                request.GivenName, request.FamilyName, info, ctx.Token).ConfigureAwait(false);
            await SendSessionAsync(ctx, 200, issued, info, info.ClientType == ClientTypeEnum.Web).ConfigureAwait(false);
        }

        private async Task GoogleAsync(HttpContextBase ctx)
        {
            GoogleSignInRequest request = RouteHelper.ReadJson<GoogleSignInRequest>(ctx);
            RequestInfo info = Info(ctx);
            IssuedSession issued = await _Directory.For(ctx).Accounts.SocialSignInAsync(ExternalProviderEnum.Google, request.IdToken, ExternalTokenTypeEnum.Jwt, null, null, null, info, ctx.Token).ConfigureAwait(false);
            await SendSessionAsync(ctx, 200, issued, info, info.ClientType == ClientTypeEnum.Web).ConfigureAwait(false);
        }

        private async Task FacebookAsync(HttpContextBase ctx)
        {
            FacebookSignInRequest request = RouteHelper.ReadJson<FacebookSignInRequest>(ctx);
            RequestInfo info = Info(ctx);
            IssuedSession issued = await _Directory.For(ctx).Accounts.SocialSignInAsync(ExternalProviderEnum.Facebook, request.Token, request.TokenType ?? ExternalTokenTypeEnum.AccessToken,
                request.Nonce, null, null, info, ctx.Token).ConfigureAwait(false);
            await SendSessionAsync(ctx, 200, issued, info, info.ClientType == ClientTypeEnum.Web).ConfigureAwait(false);
        }

        private async Task RefreshAsync(HttpContextBase ctx)
        {
            RefreshTokenRequest request = RouteHelper.ReadJson<RefreshTokenRequest>(ctx);
            RequestInfo info = Info(ctx);
            string? cookie = ReadCookie(ctx, Constants.RefreshCookieName);
            bool fromCookie = String.IsNullOrWhiteSpace(request.RefreshToken);
            string? presented = fromCookie ? cookie : request.RefreshToken;
            try
            {
                IssuedSession issued = await _Directory.For(ctx).Sessions.RefreshAsync(presented, ctx.Token).ConfigureAwait(false);
                await SendSessionAsync(ctx, 200, issued, info, info.ClientType == ClientTypeEnum.Web || (fromCookie && cookie != null)).ConfigureAwait(false);
            }
            catch (ApiErrorException e) when (fromCookie && cookie != null && !(e is RefreshRaceException))
            {
                ctx.Response.Headers["Set-Cookie"] = ClearCookie(info);
                throw;
            }
        }

        private async Task LogoutAsync(HttpContextBase ctx)
        {
            RefreshTokenRequest request;
            try
            {
                request = RouteHelper.ReadJson<RefreshTokenRequest>(ctx);
            }
            catch (ApiErrorException)
            {
                request = new RefreshTokenRequest();
            }
            RequestInfo info = Info(ctx);
            string? presented = String.IsNullOrWhiteSpace(request.RefreshToken) ? ReadCookie(ctx, Constants.RefreshCookieName) : request.RefreshToken;
            await _Directory.For(ctx).Sessions.LogoutAsync(presented, ctx.Token).ConfigureAwait(false);
            ctx.Response.Headers["Set-Cookie"] = ClearCookie(info);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task ForgotAsync(HttpContextBase ctx)
        {
            ForgotPasswordRequest request = RouteHelper.ReadJson<ForgotPasswordRequest>(ctx);
            await _Directory.For(ctx).Accounts.ForgotPasswordAsync(request.Email, Info(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 202, new MessageResponse("If an account exists for that email, a reset link is on its way.")).ConfigureAwait(false);
        }

        private async Task ResetAsync(HttpContextBase ctx)
        {
            ResetPasswordRequest request = RouteHelper.ReadJson<ResetPasswordRequest>(ctx);
            await _Directory.For(ctx).Accounts.ResetPasswordAsync(request.Token, request.NewPassword, Info(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task VerifyAsync(HttpContextBase ctx)
        {
            TokenRequest request = RouteHelper.ReadJson<TokenRequest>(ctx);
            await _Directory.For(ctx).Accounts.VerifyEmailAsync(request.Token, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task MeAsync(HttpContextBase ctx)
        {
            MeResponse me = await _Directory.For(ctx).Views.BuildMeAsync(RouteHelper.RequireUserId(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, me).ConfigureAwait(false);
        }

        private async Task ResendAsync(HttpContextBase ctx)
        {
            await _Directory.For(ctx).Accounts.ResendVerificationAsync(RouteHelper.RequireUserId(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 202, new MessageResponse("If your email is not verified yet, a new link is on its way.")).ConfigureAwait(false);
        }

        private async Task SendSessionAsync(HttpContextBase ctx, int status, IssuedSession issued, RequestInfo info, bool useCookie)
        {
            if (useCookie)
            {
                ctx.Response.Headers["Set-Cookie"] = RefreshCookie(issued.RefreshToken, issued.RefreshExpiresUtc, info);
                issued.Response.RefreshToken = null;
            }
            ctx.Response.Headers["Cache-Control"] = "no-store";
            await RouteHelper.SendJsonAsync(ctx, status, issued.Response).ConfigureAwait(false);
        }

        private static string RefreshCookie(string value, DateTime expiresUtc, RequestInfo info)
        {
            long maxAge = Math.Max(0, (long)(expiresUtc - DateTime.UtcNow).TotalSeconds);
            return Constants.RefreshCookieName + "=" + value + "; Path=" + Constants.RefreshCookiePath + "; Max-Age=" + maxAge.ToString(CultureInfo.InvariantCulture) +
                "; HttpOnly; SameSite=Lax" + (info.IsInsecureLoopback ? String.Empty : "; Secure");
        }

        private static string ClearCookie(RequestInfo info)
        {
            return Constants.RefreshCookieName + "=; Path=" + Constants.RefreshCookiePath + "; Max-Age=0; Expires=Thu, 01 Jan 1970 00:00:00 GMT; HttpOnly; SameSite=Lax" +
                (info.IsInsecureLoopback ? String.Empty : "; Secure");
        }

        private static string? ReadCookie(HttpContextBase ctx, string name)
        {
            string? header = ctx.Request.RetrieveHeaderValue("Cookie");
            if (String.IsNullOrWhiteSpace(header)) return null;
            foreach (string part in header.Split(';'))
            {
                int equals = part.IndexOf('=');
                if (equals <= 0) continue;
                if (String.Equals(part.Substring(0, equals).Trim(), name, StringComparison.Ordinal))
                {
                    string value = part.Substring(equals + 1).Trim();
                    return value.Length == 0 ? null : value;
                }
            }
            return null;
        }

        #endregion
    }
}
