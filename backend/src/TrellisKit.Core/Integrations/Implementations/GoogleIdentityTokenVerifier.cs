namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Verifies Google Sign-In ID tokens: RS256 signature against Google's JWKS (cached, refetched on an unknown kid),
    /// issuer accounts.google.com or https://accounts.google.com, audience in <see cref="OAuthSettings.GoogleAudiences"/>,
    /// and exp with 5 minutes of clock skew. Returns email, email_verified, and the given, family, and display names.
    /// Settings are read on every call. Thread safe.
    /// </summary>
    public class GoogleIdentityTokenVerifier : IIdentityTokenVerifier
    {
        #region Public-Members

        /// <summary>
        /// Accepted issuers of Google ID tokens.
        /// </summary>
        public static readonly IReadOnlyList<string> Issuers = new[] { "accounts.google.com", "https://accounts.google.com" };

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
            get { return ExternalProviderEnum.Google; }
        }

        /// <inheritdoc />
        public bool IsConfigured
        {
            get { return _Settings.GoogleAudiences.Count > 0; }
        }

        #endregion

        #region Private-Members

        private readonly OAuthSettings _Settings;
        private readonly HttpClient _HttpClient;
        private readonly string _JwksUrl;
        private readonly JwksCache _Jwks;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the client.
        /// </summary>
        /// <param name="settings">OAuth settings (live instance).</param>
        /// <param name="httpClient">HTTP client.</param>
        /// <param name="jwksUrl">Google JWKS URL (override for tests).</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public GoogleIdentityTokenVerifier(OAuthSettings settings, HttpClient httpClient, string jwksUrl = "https://www.googleapis.com/oauth2/v3/certs")
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _JwksUrl = jwksUrl ?? throw new ArgumentNullException(nameof(jwksUrl));
            _Jwks = new JwksCache(_HttpClient, _JwksUrl);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<ExternalIdentityClaims> VerifyAsync(string token, ExternalTokenTypeEnum tokenType, string? expectedNonce, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<string> audiences = _Settings.GoogleAudiences;
            if (audiences.Count < 1) throw new IntegrationNotConfiguredException("Google sign-in is not configured: set GoogleWebClientId, GoogleIosClientId, or GoogleAndroidClientId.");
            if (tokenType != ExternalTokenTypeEnum.Jwt) throw new IdentityTokenException("Google sign-in requires an ID token (JWT).");
            if (String.IsNullOrWhiteSpace(token)) throw new IdentityTokenException("The Google ID token is empty.");

            JwtValidationOptions options = new JwtValidationOptions
            {
                ValidIssuers = Issuers,
                ValidAudiences = audiences,
                ClockSkew = TimeSpan.FromMinutes(5)
            };

            JwtToken jwt;
            try
            {
                jwt = await JwtValidator.ValidateRs256Async(token, _Jwks, options, cancellationToken).ConfigureAwait(false);
            }
            catch (JwtValidationException e)
            {
                throw new IdentityTokenException("Google ID token rejected: " + e.Message, e);
            }

            string? subject = JsonClaims.GetString(jwt.Payload, "sub");
            if (String.IsNullOrEmpty(subject)) throw new IdentityTokenException("Google ID token rejected: no sub claim.");

            if (!String.IsNullOrEmpty(expectedNonce))
            {
                string? tokenNonce = JsonClaims.GetString(jwt.Payload, "nonce");
                if (!String.Equals(tokenNonce, expectedNonce, StringComparison.Ordinal)) throw new IdentityTokenException("Google ID token rejected: nonce mismatch.");
            }

            string? email = JsonClaims.GetString(jwt.Payload, "email");
            if (String.IsNullOrWhiteSpace(email)) email = null;

            return new ExternalIdentityClaims
            {
                Provider = ExternalProviderEnum.Google,
                Subject = subject,
                Email = email,
                EmailVerified = email != null && JsonClaims.GetBoolean(jwt.Payload, "email_verified") == true,
                GivenName = EmptyToNull(JsonClaims.GetString(jwt.Payload, "given_name")),
                FamilyName = EmptyToNull(JsonClaims.GetString(jwt.Payload, "family_name")),
                DisplayName = EmptyToNull(JsonClaims.GetString(jwt.Payload, "name"))
            };
        }

        #endregion

        #region Private-Methods

        private static string? EmptyToNull(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value;
        }

        #endregion
    }
}
