namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Verifies Sign in with Apple identity tokens: RS256 signature against Apple's JWKS (cached, refetched on an unknown
    /// kid), issuer https://appleid.apple.com, audience in <see cref="OAuthSettings.AppleAudiences"/>, exp and iat with
    /// 5 minutes of clock skew, and the nonce (the token's nonce claim must equal the lowercase SHA-256 hex of the raw
    /// nonce the client sent, unless the token says nonce_supported is false).
    /// Settings are read on every call. Thread safe.
    /// </summary>
    public class AppleIdentityTokenVerifier : IIdentityTokenVerifier
    {
        #region Public-Members

        /// <summary>
        /// Issuer of Apple identity tokens.
        /// </summary>
        public const string Issuer = "https://appleid.apple.com";

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
            get { return ExternalProviderEnum.Apple; }
        }

        /// <inheritdoc />
        public bool IsConfigured
        {
            get { return _Settings.AppleAudiences.Count > 0; }
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
        /// <param name="jwksUrl">Apple JWKS URL (override for tests).</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public AppleIdentityTokenVerifier(OAuthSettings settings, HttpClient httpClient, string jwksUrl = "https://appleid.apple.com/auth/keys")
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
            IReadOnlyList<string> audiences = _Settings.AppleAudiences;
            if (audiences.Count < 1) throw new IntegrationNotConfiguredException("Sign in with Apple is not configured: set AppleBundleId or AppleServicesId.");
            if (tokenType != ExternalTokenTypeEnum.Jwt) throw new IdentityTokenException("Apple identity tokens must be JWTs.");
            if (String.IsNullOrWhiteSpace(token)) throw new IdentityTokenException("The Apple identity token is empty.");

            JwtValidationOptions options = new JwtValidationOptions
            {
                ValidIssuers = new[] { Issuer },
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
                throw new IdentityTokenException("Apple identity token rejected: " + e.Message, e);
            }

            string? subject = JsonClaims.GetString(jwt.Payload, "sub");
            if (String.IsNullOrEmpty(subject)) throw new IdentityTokenException("Apple identity token rejected: no sub claim.");

            CheckNonce(jwt, expectedNonce);

            string? email = JsonClaims.GetString(jwt.Payload, "email");
            if (String.IsNullOrWhiteSpace(email)) email = null;
            bool emailVerified = email != null && JsonClaims.GetBoolean(jwt.Payload, "email_verified") == true;

            // is_private_email (string or boolean) marks a private relay address. Apple verifies relay addresses and
            // forwards mail to them, so they are treated like any other address and keep the email_verified value.

            return new ExternalIdentityClaims
            {
                Provider = ExternalProviderEnum.Apple,
                Subject = subject,
                Email = email,
                EmailVerified = emailVerified,
                GivenName = null,
                FamilyName = null,
                DisplayName = null
            };
        }

        #endregion

        #region Private-Methods

        private static void CheckNonce(JwtToken jwt, string? expectedNonce)
        {
            bool? nonceSupported = JsonClaims.GetBoolean(jwt.Payload, "nonce_supported");
            if (nonceSupported == false) return;

            string? tokenNonce = JsonClaims.GetString(jwt.Payload, "nonce");
            if (String.IsNullOrEmpty(expectedNonce))
            {
                if (!String.IsNullOrEmpty(tokenNonce)) throw new IdentityTokenException("Apple identity token rejected: the token carries a nonce but none was supplied.");
                return;
            }
            if (String.IsNullOrEmpty(tokenNonce)) throw new IdentityTokenException("Apple identity token rejected: the token has no nonce.");

            string expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(expectedNonce))).ToLowerInvariant();
            byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedHash);
            byte[] actualBytes = Encoding.UTF8.GetBytes(tokenNonce.ToLowerInvariant());
            if (!CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes))
            {
                throw new IdentityTokenException("Apple identity token rejected: nonce mismatch.");
            }
        }

        #endregion
    }
}
