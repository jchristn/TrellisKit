namespace Test.Shared.IntegrationTests
{
    using System;
    using System.Collections.Generic;
    using System.Security.Cryptography;
    using System.Text;
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
    /// Facebook Limited Login JWT and access token verification against fake JWKS and Graph endpoints.
    /// </summary>
    public static class FacebookSignInSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "IntegrationFacebookSignIn";

        #endregion

        #region Private-Members

        private const string JwksUrl = "https://limited.facebook.test/jwks/";
        private const string GraphUrl = "https://graph.facebook.test";
        private const string AppId = "1234567890";
        private const string AppSecret = "facebook-app-secret-for-tests";
        private const string AccessToken = "EAAB-access-token-for-tests";
        private const string Nonce = "limited-login-nonce";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Facebook sign-in verification", new[]
            {
                Case("LimitedLogin.Valid", "A Limited Login token with the raw nonce returns claims; the email counts as verified", async ct =>
                {
                    using (TestRsaKey key = new TestRsaKey("fb-1"))
                    using (TestHttp http = new TestHttp())
                    {
                        FacebookTokenVerifier verifier = Create(http, key, Settings());
                        ExternalIdentityClaims claims = await verifier.VerifyAsync(TestTokens.SignRs256(key, Claims()), ExternalTokenTypeEnum.Jwt, Nonce, ct).ConfigureAwait(false);
                        Check.Equal(ExternalProviderEnum.Facebook, claims.Provider, "Provider");
                        Check.Equal("10158000000000001", claims.Subject, "Subject");
                        Check.Equal("member@example.com", claims.Email, "Email");
                        Check.True(claims.EmailVerified, "Returned email is verified");
                        Check.Equal("Pat", claims.GivenName, "Given name");
                    }
                }),
                Case("LimitedLogin.Rejects", "Nonce mismatch, wrong audience, and wrong issuer are rejected", async ct =>
                {
                    using (TestRsaKey key = new TestRsaKey("fb-1"))
                    using (TestHttp http = new TestHttp())
                    {
                        FacebookTokenVerifier verifier = Create(http, key, Settings());
                        string token = TestTokens.SignRs256(key, Claims());
                        await Check.ThrowsAsync<IdentityTokenException>(() => verifier.VerifyAsync(token, ExternalTokenTypeEnum.Jwt, "other-nonce", ct), "Nonce mismatch").ConfigureAwait(false);
                        await Check.ThrowsAsync<IdentityTokenException>(() => verifier.VerifyAsync(token, ExternalTokenTypeEnum.Jwt, TestTokens.Sha256Hex(Nonce), ct), "Hashed nonce is not the raw nonce").ConfigureAwait(false);

                        JsonObject wrongAudience = Claims();
                        wrongAudience["aud"] = "999";
                        await Check.ThrowsAsync<IdentityTokenException>(() => verifier.VerifyAsync(TestTokens.SignRs256(key, wrongAudience), ExternalTokenTypeEnum.Jwt, Nonce, ct), "Wrong audience").ConfigureAwait(false);

                        JsonObject wrongIssuer = Claims();
                        wrongIssuer["iss"] = "https://facebook.example.com";
                        await Check.ThrowsAsync<IdentityTokenException>(() => verifier.VerifyAsync(TestTokens.SignRs256(key, wrongIssuer), ExternalTokenTypeEnum.Jwt, Nonce, ct), "Wrong issuer").ConfigureAwait(false);
                    }
                }),
                Case("AccessToken.Valid", "debug_token validates the app and /me returns the profile with appsecret_proof", async ct =>
                {
                    using (TestHttp http = new TestHttp())
                    {
                        FacebookTokenVerifier verifier = new FacebookTokenVerifier(Settings(), http.Client, GraphUrl, JwksUrl);
                        MapGraph(http, true, AppId);
                        ExternalIdentityClaims claims = await verifier.VerifyAsync(AccessToken, ExternalTokenTypeEnum.AccessToken, null, ct).ConfigureAwait(false);
                        Check.Equal("10158000000000001", claims.Subject, "Subject");
                        Check.Equal("member@example.com", claims.Email, "Email");
                        Check.True(claims.EmailVerified, "Email verified");
                        Check.Equal("Christner", claims.FamilyName, "Family name");

                        RecordedRequest debug = http.Handler.Requests[0];
                        Dictionary<string, string> debugQuery = debug.Query();
                        Check.Equal(AccessToken, debugQuery["input_token"], "input_token");
                        Check.Equal(AppId + "|" + AppSecret, debugQuery["access_token"], "App access token");

                        RecordedRequest me = http.Handler.Requests[1];
                        Dictionary<string, string> meQuery = me.Query();
                        Check.Equal("id,name,email,first_name,last_name", meQuery["fields"], "Fields");
                        Check.Equal(AccessToken, meQuery["access_token"], "User access token");
                        string proof;
                        using (HMACSHA256 hmac = new HMACSHA256(Encoding.UTF8.GetBytes(AppSecret)))
                        {
                            proof = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(AccessToken))).ToLowerInvariant();
                        }
                        Check.Equal(proof, meQuery["appsecret_proof"], "appsecret_proof");
                    }
                }),
                Case("AccessToken.Invalid", "An invalid token or a token for another app is rejected before /me", async ct =>
                {
                    using (TestHttp http = new TestHttp())
                    {
                        FacebookTokenVerifier verifier = new FacebookTokenVerifier(Settings(), http.Client, GraphUrl, JwksUrl);
                        MapGraph(http, false, AppId);
                        await Check.ThrowsAsync<IdentityTokenException>(() => verifier.VerifyAsync(AccessToken, ExternalTokenTypeEnum.AccessToken, null, ct), "is_valid false").ConfigureAwait(false);
                        MapGraph(http, true, "5555555555");
                        await Check.ThrowsAsync<IdentityTokenException>(() => verifier.VerifyAsync(AccessToken, ExternalTokenTypeEnum.AccessToken, null, ct), "Other app").ConfigureAwait(false);
                        Check.Equal(0, http.Handler.Count(GraphUrl + "/me"), "/me never called");
                        http.Handler.MapJson("GET", GraphUrl + "/debug_token", 400, "{\"error\":{\"message\":\"Invalid OAuth access token.\",\"type\":\"OAuthException\",\"code\":190}}");
                        IdentityTokenException e = await Check.ThrowsAsync<IdentityTokenException>(() => verifier.VerifyAsync(AccessToken, ExternalTokenTypeEnum.AccessToken, null, ct), "Graph error").ConfigureAwait(false);
                        Check.True(!e.Message.Contains(AppSecret, StringComparison.Ordinal) && !e.Message.Contains(AccessToken, StringComparison.Ordinal), "No secrets in the message");
                    }
                }),
                Case("NotConfigured", "No app id means not configured; access tokens also need the secret", async ct =>
                {
                    using (TestHttp http = new TestHttp())
                    {
                        OAuthSettings settings = new OAuthSettings();
                        FacebookTokenVerifier verifier = new FacebookTokenVerifier(settings, http.Client, GraphUrl, JwksUrl);
                        Check.True(!verifier.IsConfigured, "Not configured");
                        await Check.ThrowsAsync<IntegrationNotConfiguredException>(() => verifier.VerifyAsync(AccessToken, ExternalTokenTypeEnum.AccessToken, null, ct), "No app id").ConfigureAwait(false);
                        settings.FacebookAppId = AppId;
                        Check.True(verifier.IsConfigured, "Configured with app id");
                        await Check.ThrowsAsync<IntegrationNotConfiguredException>(() => verifier.VerifyAsync(AccessToken, ExternalTokenTypeEnum.AccessToken, null, ct), "No secret").ConfigureAwait(false);
                    }
                })
            });
        }

        #endregion

        #region Private-Methods

        private static OAuthSettings Settings()
        {
            return new OAuthSettings { FacebookAppId = AppId, FacebookAppSecret = AppSecret };
        }

        private static FacebookTokenVerifier Create(TestHttp http, TestRsaKey key, OAuthSettings settings)
        {
            http.Handler.MapJson("GET", JwksUrl, 200, TestTokens.Jwks(key));
            return new FacebookTokenVerifier(settings, http.Client, GraphUrl, JwksUrl);
        }

        private static void MapGraph(TestHttp http, bool isValid, string appId)
        {
            JsonObject debug = new JsonObject
            {
                ["data"] = new JsonObject
                {
                    ["app_id"] = appId,
                    ["type"] = "USER",
                    ["is_valid"] = isValid,
                    ["user_id"] = "10158000000000001",
                    ["expires_at"] = TestTokens.Now + 3600
                }
            };
            http.Handler.MapJson("GET", GraphUrl + "/debug_token", 200, debug.ToJsonString());
            http.Handler.MapJson("GET", GraphUrl + "/me", 200, "{\"id\":\"10158000000000001\",\"name\":\"Pat Example\",\"email\":\"member@example.com\",\"first_name\":\"Pat\",\"last_name\":\"Christner\"}");
        }

        private static JsonObject Claims()
        {
            long now = TestTokens.Now;
            return new JsonObject
            {
                ["iss"] = "https://www.facebook.com",
                ["aud"] = AppId,
                ["sub"] = "10158000000000001",
                ["iat"] = now,
                ["exp"] = now + 3600,
                ["nonce"] = Nonce,
                ["email"] = "member@example.com",
                ["name"] = "Pat Example",
                ["given_name"] = "Pat",
                ["family_name"] = "Christner"
            };
        }

        private static TestCaseDescriptor Case(string caseId, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(Id, caseId, name, body, new[] { "fast", "unit", "integrations" });
        }

        #endregion
    }
}
