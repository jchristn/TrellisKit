namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Verifies Facebook sign-in. A <see cref="ExternalTokenTypeEnum.Jwt"/> is a Limited Login OIDC token checked
    /// against the Limited Login JWKS (RS256, issuer https://www.facebook.com, audience equal to the app id, exp with
    /// 5 minutes of skew, nonce equal to the raw nonce). An <see cref="ExternalTokenTypeEnum.AccessToken"/> is checked
    /// with the Graph debug_token endpoint (is_valid and app_id must match) and the profile is read from /me.
    /// Facebook only returns confirmed email addresses, so a returned email is treated as verified.
    /// Settings are read on every call. Thread safe.
    /// </summary>
    public class FacebookTokenVerifier : IIdentityTokenVerifier
    {
        #region Public-Members

        /// <summary>
        /// Issuer of Limited Login tokens.
        /// </summary>
        public const string Issuer = "https://www.facebook.com";

        /// <summary>
        /// Minimum time between JWKS fetches when tokens name an unknown kid. Default 60 seconds. Minimum 0, maximum 1 hour.
        /// </summary>
        public TimeSpan JwksMinimumRefetchInterval
        {
            get { return _Jwks.MinimumRefetchInterval; }
            set { _Jwks.MinimumRefetchInterval = value; }
        }

        /// <inheritdoc />
        public ExternalProviderEnum Provider
        {
            get { return ExternalProviderEnum.Facebook; }
        }

        /// <inheritdoc />
        public bool IsConfigured
        {
            get { return !String.IsNullOrWhiteSpace(_Settings.FacebookAppId); }
        }

        #endregion

        #region Private-Members

        private readonly OAuthSettings _Settings;
        private readonly HttpClient _HttpClient;
        private readonly string _GraphBaseUrl;
        private readonly string _JwksUrl;
        private readonly JwksCache _Jwks;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the client.
        /// </summary>
        /// <param name="settings">OAuth settings (live instance).</param>
        /// <param name="httpClient">HTTP client.</param>
        /// <param name="graphBaseUrl">Graph API base URL (override for tests).</param>
        /// <param name="jwksUrl">Limited Login JWKS URL (override for tests).</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public FacebookTokenVerifier(OAuthSettings settings, HttpClient httpClient, string graphBaseUrl = "https://graph.facebook.com", string jwksUrl = "https://limited.facebook.com/.well-known/oauth/openid/jwks/")
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _GraphBaseUrl = graphBaseUrl ?? throw new ArgumentNullException(nameof(graphBaseUrl));
            _JwksUrl = jwksUrl ?? throw new ArgumentNullException(nameof(jwksUrl));
            _Jwks = new JwksCache(_HttpClient, _JwksUrl);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<ExternalIdentityClaims> VerifyAsync(string token, ExternalTokenTypeEnum tokenType, string? expectedNonce, CancellationToken cancellationToken = default)
        {
            string appId = (_Settings.FacebookAppId ?? String.Empty).Trim();
            if (appId.Length < 1) throw new IntegrationNotConfiguredException("Facebook sign-in is not configured: set FacebookAppId.");
            if (String.IsNullOrWhiteSpace(token)) throw new IdentityTokenException("The Facebook token is empty.");

            if (tokenType == ExternalTokenTypeEnum.Jwt) return VerifyLimitedLoginAsync(token, appId, expectedNonce, cancellationToken);

            string appSecret = (_Settings.FacebookAppSecret ?? String.Empty).Trim();
            if (appSecret.Length < 1) throw new IntegrationNotConfiguredException("Facebook access token sign-in is not configured: set FacebookAppSecret.");
            return VerifyAccessTokenAsync(token.Trim(), appId, appSecret, cancellationToken);
        }

        #endregion

        #region Private-Methods

        private async Task<ExternalIdentityClaims> VerifyLimitedLoginAsync(string token, string appId, string? expectedNonce, CancellationToken cancellationToken)
        {
            JwtValidationOptions options = new JwtValidationOptions
            {
                ValidIssuers = new[] { Issuer },
                ValidAudiences = new[] { appId },
                ClockSkew = TimeSpan.FromMinutes(5)
            };

            JwtToken jwt;
            try
            {
                jwt = await JwtValidator.ValidateRs256Async(token, _Jwks, options, cancellationToken).ConfigureAwait(false);
            }
            catch (JwtValidationException e)
            {
                throw new IdentityTokenException("Facebook Limited Login token rejected: " + e.Message, e);
            }

            string? subject = JsonClaims.GetString(jwt.Payload, "sub");
            if (String.IsNullOrEmpty(subject)) throw new IdentityTokenException("Facebook Limited Login token rejected: no sub claim.");

            string? tokenNonce = JsonClaims.GetString(jwt.Payload, "nonce");
            if (!String.IsNullOrEmpty(expectedNonce) || !String.IsNullOrEmpty(tokenNonce))
            {
                byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedNonce ?? String.Empty);
                byte[] actualBytes = Encoding.UTF8.GetBytes(tokenNonce ?? String.Empty);
                if (!CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes))
                {
                    throw new IdentityTokenException("Facebook Limited Login token rejected: nonce mismatch.");
                }
            }

            string? email = EmptyToNull(JsonClaims.GetString(jwt.Payload, "email"));
            return new ExternalIdentityClaims
            {
                Provider = ExternalProviderEnum.Facebook,
                Subject = subject,
                Email = email,
                EmailVerified = email != null,
                GivenName = EmptyToNull(JsonClaims.GetString(jwt.Payload, "given_name")),
                FamilyName = EmptyToNull(JsonClaims.GetString(jwt.Payload, "family_name")),
                DisplayName = EmptyToNull(JsonClaims.GetString(jwt.Payload, "name"))
            };
        }

        private async Task<ExternalIdentityClaims> VerifyAccessTokenAsync(string accessToken, string appId, string appSecret, CancellationToken cancellationToken)
        {
            string graph = _GraphBaseUrl.TrimEnd('/');
            string debugUrl = graph + "/debug_token?input_token=" + Uri.EscapeDataString(accessToken)
                + "&access_token=" + Uri.EscapeDataString(appId + "|" + appSecret);

            string? userId;
            using (JsonDocument debug = await GetJsonAsync(debugUrl, "debug_token", cancellationToken).ConfigureAwait(false))
            {
                if (!JsonClaims.TryGetObject(debug.RootElement, "data", out JsonElement data))
                {
                    throw new IdentityTokenException("Facebook access token rejected: debug_token returned no data.");
                }
                if (JsonClaims.GetBoolean(data, "is_valid") != true) throw new IdentityTokenException("Facebook access token rejected: the token is not valid.");
                if (!String.Equals(JsonClaims.GetString(data, "app_id"), appId, StringComparison.Ordinal))
                {
                    throw new IdentityTokenException("Facebook access token rejected: the token was issued for another app.");
                }
                userId = JsonClaims.GetString(data, "user_id");
                if (String.IsNullOrEmpty(userId)) throw new IdentityTokenException("Facebook access token rejected: the token has no user.");
            }

            string proof;
            using (HMACSHA256 hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret)))
            {
                proof = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(accessToken))).ToLowerInvariant();
            }
            string meUrl = graph + "/me?fields=id,name,email,first_name,last_name&access_token=" + Uri.EscapeDataString(accessToken)
                + "&appsecret_proof=" + proof;

            using (JsonDocument me = await GetJsonAsync(meUrl, "me", cancellationToken).ConfigureAwait(false))
            {
                string? id = JsonClaims.GetString(me.RootElement, "id");
                if (String.IsNullOrEmpty(id) || !String.Equals(id, userId, StringComparison.Ordinal))
                {
                    throw new IdentityTokenException("Facebook access token rejected: the profile does not match the token's user.");
                }
                string? email = EmptyToNull(JsonClaims.GetString(me.RootElement, "email"));
                return new ExternalIdentityClaims
                {
                    Provider = ExternalProviderEnum.Facebook,
                    Subject = id,
                    Email = email,
                    EmailVerified = email != null,
                    GivenName = EmptyToNull(JsonClaims.GetString(me.RootElement, "first_name")),
                    FamilyName = EmptyToNull(JsonClaims.GetString(me.RootElement, "last_name")),
                    DisplayName = EmptyToNull(JsonClaims.GetString(me.RootElement, "name"))
                };
            }
        }

        private async Task<JsonDocument> GetJsonAsync(string url, string what, CancellationToken cancellationToken)
        {
            try
            {
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
                using (HttpResponseMessage response = await _HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new IdentityTokenException("Facebook access token rejected: Graph " + what + " returned HTTP " + (int)response.StatusCode + GraphError(body) + ".");
                    }
                    return JsonDocument.Parse(body);
                }
            }
            catch (HttpRequestException e)
            {
                throw new IdentityTokenException("Facebook Graph " + what + " request failed.", e);
            }
            catch (TaskCanceledException e) when (!cancellationToken.IsCancellationRequested)
            {
                throw new IdentityTokenException("Facebook Graph " + what + " request timed out.", e);
            }
            catch (JsonException e)
            {
                throw new IdentityTokenException("Facebook Graph " + what + " returned invalid JSON.", e);
            }
        }

        private static string GraphError(string body)
        {
            try
            {
                using (JsonDocument document = JsonDocument.Parse(body))
                {
                    if (JsonClaims.TryGetObject(document.RootElement, "error", out JsonElement error))
                    {
                        string? type = JsonClaims.GetString(error, "type");
                        string? code = JsonClaims.GetString(error, "code");
                        return " (" + (type ?? "error") + (code != null ? " " + code : String.Empty) + ")";
                    }
                }
            }
            catch (JsonException)
            {
            }
            return String.Empty;
        }

        private static string? EmptyToNull(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value;
        }

        #endregion
    }
}
