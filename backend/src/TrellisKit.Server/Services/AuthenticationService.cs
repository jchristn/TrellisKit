namespace TrellisKit.Server.Services
{
    using System;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Security;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Serialization;
    using WatsonWebserver.Core;

    /// <summary>
    /// Watson AuthenticateRequest hook for every post-authentication route. Accepts a member access token or the
    /// break-glass admin token, stores a <see cref="RequestContext"/> in the request metadata, then runs the central
    /// <see cref="AuthorizationService"/> check (route groups and the paywall).
    /// Thread safety: safe to call concurrently; reads the admin token live so a changed token applies immediately.
    /// </summary>
    public class AuthenticationService
    {
        #region Public-Members

        /// <summary>
        /// Property this service authenticates for, stamped on every request context. Default empty.
        /// </summary>
        public string PropertyId { get; set; } = String.Empty;

        /// <summary>
        /// Authenticates live update deploy keys (bearer tokens starting with tkdk_), or null to refuse them.
        /// </summary>
        public Func<string, HttpContextBase, Task<RequestContext?>>? DeployKeyAuthenticator { get; set; } = null;

        /// <summary>
        /// Routes a deploy key may call (method, path). Default: none.
        /// </summary>
        public Func<string, string?, bool>? DeployKeyPermits { get; set; } = null;

        /// <summary>
        /// Runs after authorization succeeds (kill switches and the minimum binary version); returns true when it
        /// refused and answered the request. Null runs nothing.
        /// </summary>
        public Func<HttpContextBase, RequestContext?, Task<bool>>? RequestGate { get; set; } = null;

        #endregion

        #region Private-Members

        private const string _BearerPrefix = "Bearer ";
        private readonly AuthSettings _Settings;
        private readonly TokenService? _Tokens;
        private readonly AuthorizationService _Authorization;
        private readonly AppLogger _Logger;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="settings">Authentication settings (the running instance, so live changes apply).</param>
        /// <param name="tokens">Token service for member access tokens, or null to accept only the admin token.</param>
        /// <param name="authorization">Authorization service (built with the property's minimum age).</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">The settings, authorization service, or logger are null.</exception>
        public AuthenticationService(AuthSettings settings, TokenService? tokens, AuthorizationService authorization, AppLogger logger)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Tokens = tokens;
            _Authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Watson AuthenticateRequest hook. On success sets ctx.Metadata; on failure sends 401, 402, or 403.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <returns>Task.</returns>
        public async Task AuthenticateRequestAsync(HttpContextBase ctx)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            string? header = ctx.Request.RetrieveHeaderValue("Authorization");
            RequestContext? context = Authenticate(header);
            if (context != null)
            {
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.AdminAuthentications, "outcome", "success");
            }
            else if (DeployKeyAuthenticator != null && TryBearer(header, out string deployKey) && deployKey.StartsWith("tkdk_", StringComparison.Ordinal))
            {
                context = await DeployKeyAuthenticator(deployKey, ctx).ConfigureAwait(false);
            }
            else if (_Tokens != null && TryBearer(header, out string presented))
            {
                SessionPrincipal? principal = await _Tokens.ValidateAccessTokenAsync(presented, ctx.Token).ConfigureAwait(false);
                if (principal != null && principal.PrincipalKind == PrincipalKindEnum.Delegated)
                {
                    // A platform administrator acting as SystemAdmin in this property: staff rights without a member
                    // account, exactly like the break-glass token, so routes that act on "me" return 403.
                    context = new RequestContext
                    {
                        IsAuthenticated = true,
                        PrincipalKind = PrincipalKindEnum.Delegated,
                        PlatformAdminId = principal.UserId,
                        SessionId = principal.SessionId,
                        Email = principal.Email,
                        PrincipalName = "platform admin " + principal.DisplayName,
                        IsAdmin = true,
                        IsBreakGlass = true,
                        AgeAttested = true,
                        HasMembership = true,
                        ClientType = principal.ClientType,
                        Scheme = "Delegated"
                    };
                }
                else if (principal != null)
                {
                    context = new RequestContext
                    {
                        PrincipalKind = PrincipalKindEnum.Member,
                        IsAuthenticated = true,
                        UserId = principal.UserId,
                        SessionId = principal.SessionId,
                        Email = principal.Email,
                        PrincipalName = principal.DisplayName,
                        IsAdmin = principal.IsSystemAdmin,
                        IsModerator = principal.IsModerator,
                        IsSupportAgent = principal.IsSupportAgent,
                        IsCommunicator = principal.IsCommunicator,
                        IsReleaseManager = principal.IsReleaseManager,
                        AgeAttested = principal.AgeAttested,
                        HasMembership = principal.HasMembership,
                        AcceptedDocuments = principal.AcceptedDocuments,
                        ClientType = principal.ClientType,
                        Scheme = "Session"
                    };
                }
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.SessionAuthentications, "outcome", principal != null ? "success" : "failure");
            }

            if (context == null && String.IsNullOrWhiteSpace(header))
            {
                // Signed out: the authorization stage decides (anonymous browsing opens Read and List content routes
                // in PartiallyGated and Open; everything else is 401 as before).
                context = new RequestContext { IsAuthenticated = false, PrincipalKind = PrincipalKindEnum.Anonymous, Scheme = ActorService.AnonymousScheme };
            }

            if (context == null)
            {
                if (header != null && header.StartsWith(_BearerPrefix + Constants.AdminTokenPrefix, StringComparison.Ordinal)) TrellisKitTelemetry.Increment(TrellisKitTelemetry.AdminAuthentications, "outcome", "failure");
                _Logger.Debug("[AuthenticationService] rejected " + ctx.Request.Method + " " + ctx.Request.Url.RawWithoutQuery + " from " + ctx.Request.Source?.IpAddress);
                await SendAsync(ctx, 401, "Unauthorized", "A valid bearer token is required.").ConfigureAwait(false);
                return;
            }

            context.PropertyId = PropertyId;
            ctx.Metadata = context;
            if (context.DeployKeyId != null && (DeployKeyPermits == null || !DeployKeyPermits(ctx.Request.Method.ToString(), ctx.Request.Url.RawWithoutQuery)))
            {
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.AuthorizationDenials, "reason", "Forbidden", "route_group", "DeployKey");
                await SendAsync(ctx, 403, "Forbidden", "A deploy key may only upload and publish updates.").ConfigureAwait(false);
                return;
            }
            AuthorizationDecision decision = _Authorization.Authorize(context, ctx.Request.Method.ToString(), ctx.Request.Url.RawWithoutQuery);
            if (!decision.IsPermitted)
            {
                if (decision.StatusCode == 401) _Logger.Debug("[AuthenticationService] rejected " + ctx.Request.Method + " " + ctx.Request.Url.RawWithoutQuery + " from " + ctx.Request.Source?.IpAddress);
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.AuthorizationDenials, "reason", decision.Error, "route_group", decision.Group.ToString());
                if (decision.Documents != null)
                {
                    await SendBodyAsync(ctx, decision.StatusCode, new DocumentAcceptanceRequiredResponse(decision.Message, decision.Documents)).ConfigureAwait(false);
                    return;
                }
                if (decision.Required != null)
                {
                    await SendBodyAsync(ctx, decision.StatusCode, new AccessDeniedResponse { Error = decision.Error, Message = decision.Message, Required = decision.Required, Feature = decision.Feature }).ConfigureAwait(false);
                    return;
                }
                await SendAsync(ctx, decision.StatusCode, decision.Error, decision.Message).ConfigureAwait(false);
                return;
            }
            if (RequestGate != null) await RequestGate(ctx, context).ConfigureAwait(false);
        }

        /// <summary>
        /// Authenticate the break-glass admin token from an Authorization header value.
        /// </summary>
        /// <param name="authorizationHeader">Header value, for example "Bearer abc".</param>
        /// <returns>Admin context on success, or null.</returns>
        public RequestContext? Authenticate(string? authorizationHeader)
        {
            if (!TryBearer(authorizationHeader, out string presented)) return null;
            string expected = _Settings.AdminToken;
            if (String.IsNullOrEmpty(expected)) return null;
            if (!FixedTimeEquals(presented, expected)) return null;

            return new RequestContext
            {
                IsAuthenticated = true,
                IsAdmin = true,
                IsBreakGlass = true,
                PrincipalKind = PrincipalKindEnum.BreakGlass,
                AgeAttested = true,
                HasMembership = true,
                PrincipalName = "admin",
                Scheme = "AdminToken"
            };
        }

        #endregion

        #region Private-Methods

        private static bool TryBearer(string? header, out string presented)
        {
            presented = String.Empty;
            if (String.IsNullOrWhiteSpace(header)) return false;
            if (!header.StartsWith(_BearerPrefix, StringComparison.OrdinalIgnoreCase)) return false;
            presented = header.Substring(_BearerPrefix.Length).Trim();
            return presented.Length > 0;
        }

        private static async Task SendBodyAsync(HttpContextBase ctx, int status, ErrorResponse body)
        {
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.Send(ApiJson.Serialize(body), ctx.Token).ConfigureAwait(false);
        }

        private static async Task SendAsync(HttpContextBase ctx, int status, string error, string message)
        {
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.Send(ApiJson.Serialize(new ErrorResponse(error, message)), ctx.Token).ConfigureAwait(false);
        }

        private static bool FixedTimeEquals(string presented, string expected)
        {
            byte[] a = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
            byte[] b = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
            return CryptographicOperations.FixedTimeEquals(a, b);
        }

        #endregion
    }
}
