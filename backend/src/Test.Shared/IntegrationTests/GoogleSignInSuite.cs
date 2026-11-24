namespace Test.Shared.IntegrationTests
{
    using System;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Google Sign-In ID token verification against a fake JWKS.
    /// </summary>
    public static class GoogleSignInSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "IntegrationGoogleSignIn";

        #endregion

        #region Private-Members

        private const string JwksUrl = "https://google.test/oauth2/v3/certs";
        private const string WebClientId = "web-client.apps.googleusercontent.com";
        private const string IosClientId = "ios-client.apps.googleusercontent.com";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Google Sign-In token verification", new[]
            {
                Case("Valid", "Tokens from either issuer form and any configured client id return claims", async ct =>
                {
                    using (TestRsaKey key = new TestRsaKey("google-1"))
                    using (TestHttp http = new TestHttp())
                    {
                        GoogleIdentityTokenVerifier verifier = Create(http, key, Settings());
                        ExternalIdentityClaims claims = await verifier.VerifyAsync(TestTokens.SignRs256(key, Claims()), ExternalTokenTypeEnum.Jwt, null, ct).ConfigureAwait(false);
                        Check.Equal(ExternalProviderEnum.Google, claims.Provider, "Provider");
                        Check.Equal("1092837465", claims.Subject, "Subject");
                        Check.Equal("member@gmail.com", claims.Email, "Email");
                        Check.True(claims.EmailVerified, "Email verified");
                        Check.Equal("Pat", claims.GivenName, "Given name");
                        Check.Equal("Christner", claims.FamilyName, "Family name");
                        Check.Equal("Pat Example", claims.DisplayName, "Display name");

                        JsonObject ios = Claims();
                        ios["iss"] = "accounts.google.com";
                        ios["aud"] = IosClientId;
                        ios["email_verified"] = "false";
                        ExternalIdentityClaims iosClaims = await verifier.VerifyAsync(TestTokens.SignRs256(key, ios), ExternalTokenTypeEnum.Jwt, null, ct).ConfigureAwait(false);
                        Check.True(!iosClaims.EmailVerified, "Unverified email reported as unverified");
                    }
                }),
                Case("Rejects", "Wrong audience, wrong issuer, expired, bad signature, and access tokens are rejected", async ct =>
                {
                    using (TestRsaKey key = new TestRsaKey("google-1"))
                    using (TestRsaKey other = new TestRsaKey("google-1"))
                    using (TestHttp http = new TestHttp())
                    {
                        GoogleIdentityTokenVerifier verifier = Create(http, key, Settings());

                        JsonObject wrongAudience = Claims();
                        wrongAudience["aud"] = "android-client-not-configured";
                        await Check.ThrowsAsync<IdentityTokenException>(() => verifier.VerifyAsync(TestTokens.SignRs256(key, wrongAudience), ExternalTokenTypeEnum.Jwt, null, ct), "Wrong audience").ConfigureAwait(false);

                        JsonObject wrongIssuer = Claims();
                        wrongIssuer["iss"] = "https://accounts.google.evil";
                        await Check.ThrowsAsync<IdentityTokenException>(() => verifier.VerifyAsync(TestTokens.SignRs256(key, wrongIssuer), ExternalTokenTypeEnum.Jwt, null, ct), "Wrong issuer").ConfigureAwait(false);

                        JsonObject expired = Claims();
                        expired["exp"] = TestTokens.Now - 301;
                        await Check.ThrowsAsync<IdentityTokenException>(() => verifier.VerifyAsync(TestTokens.SignRs256(key, expired), ExternalTokenTypeEnum.Jwt, null, ct), "Expired").ConfigureAwait(false);

                        await Check.ThrowsAsync<IdentityTokenException>(() => verifier.VerifyAsync(TestTokens.SignRs256(other, Claims()), ExternalTokenTypeEnum.Jwt, null, ct), "Bad signature").ConfigureAwait(false);
                        await Check.ThrowsAsync<IdentityTokenException>(() => verifier.VerifyAsync("ya29.access-token", ExternalTokenTypeEnum.AccessToken, null, ct), "Access token").ConfigureAwait(false);
                    }
                }),
                Case("NotConfigured", "No client ids means not configured", async ct =>
                {
                    using (TestHttp http = new TestHttp())
                    {
                        GoogleIdentityTokenVerifier verifier = new GoogleIdentityTokenVerifier(new OAuthSettings(), http.Client, JwksUrl);
                        Check.True(!verifier.IsConfigured, "Not configured");
                        await Check.ThrowsAsync<IntegrationNotConfiguredException>(() => verifier.VerifyAsync("a.b.c", ExternalTokenTypeEnum.Jwt, null, ct), "Verify").ConfigureAwait(false);
                        Check.Equal(0, http.Handler.Requests.Count, "No network calls");
                    }
                })
            });
        }

        #endregion

        #region Private-Methods

        private static OAuthSettings Settings()
        {
            return new OAuthSettings { GoogleWebClientId = WebClientId, GoogleIosClientId = IosClientId };
        }

        private static GoogleIdentityTokenVerifier Create(TestHttp http, TestRsaKey key, OAuthSettings settings)
        {
            http.Handler.MapJson("GET", JwksUrl, 200, TestTokens.Jwks(key));
            return new GoogleIdentityTokenVerifier(settings, http.Client, JwksUrl);
        }

        private static JsonObject Claims()
        {
            long now = TestTokens.Now;
            return new JsonObject
            {
                ["iss"] = "https://accounts.google.com",
                ["azp"] = WebClientId,
                ["aud"] = WebClientId,
                ["sub"] = "1092837465",
                ["email"] = "member@gmail.com",
                ["email_verified"] = true,
                ["name"] = "Pat Example",
                ["given_name"] = "Pat",
                ["family_name"] = "Christner",
                ["iat"] = now,
                ["exp"] = now + 3600
            };
        }

        private static TestCaseDescriptor Case(string caseId, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(Id, caseId, name, body, new[] { "fast", "unit", "integrations" });
        }

        #endregion
    }
}
