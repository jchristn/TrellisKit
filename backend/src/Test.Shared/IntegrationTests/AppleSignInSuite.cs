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
    /// Sign in with Apple identity token verification against a fake JWKS.
    /// </summary>
    public static class AppleSignInSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "IntegrationAppleSignIn";

        /// <summary>
        /// Fake JWKS URL.
        /// </summary>
        public const string JwksUrl = "https://apple.test/auth/keys";

        /// <summary>
        /// Accepted bundle id audience.
        /// </summary>
        public const string BundleId = "com.example.app";

        /// <summary>
        /// Accepted Services ID audience.
        /// </summary>
        public const string ServicesId = "com.example.web";

        #endregion

        #region Private-Members

        private const string RawNonce = "raw-nonce-1234567890";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Sign in with Apple token verification", new[]
            {
                Case("Valid", "A correctly signed token for either audience returns its claims", async ct =>
                {
                    using (TestRsaKey key = new TestRsaKey("apple-1"))
                    using (AppleSignInHarness h = new AppleSignInHarness(key))
                    {
                        JsonObject payload = Claims();
                        payload["email_verified"] = "true";
                        payload["is_private_email"] = "true";
                        ExternalIdentityClaims claims = await h.Verifier.VerifyAsync(TestTokens.SignRs256(key, payload), ExternalTokenTypeEnum.Jwt, RawNonce, ct).ConfigureAwait(false);
                        Check.Equal(ExternalProviderEnum.Apple, claims.Provider, "Provider");
                        Check.Equal("001234.apple.subject", claims.Subject, "Subject");
                        Check.Equal("relay@privaterelay.appleid.com", claims.Email, "Email");
                        Check.True(claims.EmailVerified, "String email_verified is honored");

                        JsonObject web = Claims();
                        web["aud"] = ServicesId;
                        web["email_verified"] = true;
                        ExternalIdentityClaims webClaims = await h.Verifier.VerifyAsync(TestTokens.SignRs256(key, web), ExternalTokenTypeEnum.Jwt, RawNonce, ct).ConfigureAwait(false);
                        Check.True(webClaims.EmailVerified, "Boolean email_verified is honored");
                        Check.Equal(1, h.Handler.Count(JwksUrl), "JWKS fetched once and cached");
                    }
                }),
                Case("WrongAudience", "A token for another client id is rejected", async ct =>
                {
                    using (TestRsaKey key = new TestRsaKey("apple-1"))
                    using (AppleSignInHarness h = new AppleSignInHarness(key))
                    {
                        JsonObject payload = Claims();
                        payload["aud"] = "com.someone.else";
                        await ExpectRejected(h, TestTokens.SignRs256(key, payload), RawNonce, "audience", ct).ConfigureAwait(false);
                    }
                }),
                Case("WrongIssuer", "A token from another issuer is rejected", async ct =>
                {
                    using (TestRsaKey key = new TestRsaKey("apple-1"))
                    using (AppleSignInHarness h = new AppleSignInHarness(key))
                    {
                        JsonObject payload = Claims();
                        payload["iss"] = "https://appleid.example.com";
                        await ExpectRejected(h, TestTokens.SignRs256(key, payload), RawNonce, "issuer", ct).ConfigureAwait(false);
                    }
                }),
                Case("Expired", "Expiry beyond the 5 minute skew is rejected; within the skew is accepted", async ct =>
                {
                    using (TestRsaKey key = new TestRsaKey("apple-1"))
                    using (AppleSignInHarness h = new AppleSignInHarness(key))
                    {
                        JsonObject expired = Claims();
                        expired["iat"] = TestTokens.Now - 3600;
                        expired["exp"] = TestTokens.Now - 600;
                        await ExpectRejected(h, TestTokens.SignRs256(key, expired), RawNonce, "expired", ct).ConfigureAwait(false);

                        JsonObject withinSkew = Claims();
                        withinSkew["exp"] = TestTokens.Now - 60;
                        await h.Verifier.VerifyAsync(TestTokens.SignRs256(key, withinSkew), ExternalTokenTypeEnum.Jwt, RawNonce, ct).ConfigureAwait(false);

                        JsonObject future = Claims();
                        future["iat"] = TestTokens.Now + 3600;
                        await ExpectRejected(h, TestTokens.SignRs256(key, future), RawNonce, "future", ct).ConfigureAwait(false);
                    }
                }),
                Case("BadSignature", "A token signed by another key with the same kid, or with a tampered payload, is rejected", async ct =>
                {
                    using (TestRsaKey key = new TestRsaKey("apple-1"))
                    using (TestRsaKey impostor = new TestRsaKey("apple-1"))
                    using (AppleSignInHarness h = new AppleSignInHarness(key))
                    {
                        await ExpectRejected(h, TestTokens.SignRs256(impostor, Claims()), RawNonce, "signature", ct).ConfigureAwait(false);

                        string genuine = TestTokens.SignRs256(key, Claims());
                        JsonObject altered = Claims();
                        altered["sub"] = "someone.else";
                        await ExpectRejected(h, TestTokens.ReplacePayload(genuine, altered), RawNonce, "signature", ct).ConfigureAwait(false);
                    }
                }),
                Case("UnknownKidRefetch", "An unknown kid triggers a JWKS refetch, limited by the minimum refetch interval", async ct =>
                {
                    using (TestRsaKey first = new TestRsaKey("apple-1"))
                    using (TestRsaKey rotated = new TestRsaKey("apple-2"))
                    using (AppleSignInHarness h = new AppleSignInHarness(first))
                    {
                        h.Verifier.JwksMinimumRefetchInterval = TimeSpan.Zero;
                        await h.Verifier.VerifyAsync(TestTokens.SignRs256(first, Claims()), ExternalTokenTypeEnum.Jwt, RawNonce, ct).ConfigureAwait(false);
                        h.Handler.MapJson("GET", JwksUrl, 200, TestTokens.Jwks(first, rotated));
                        ExternalIdentityClaims claims = await h.Verifier.VerifyAsync(TestTokens.SignRs256(rotated, Claims()), ExternalTokenTypeEnum.Jwt, RawNonce, ct).ConfigureAwait(false);
                        Check.Equal("001234.apple.subject", claims.Subject, "Rotated key accepted");
                        Check.Equal(2, h.Handler.Count(JwksUrl), "Refetched once for the unknown kid");
                    }

                    using (TestRsaKey first = new TestRsaKey("apple-1"))
                    using (TestRsaKey rotated = new TestRsaKey("apple-2"))
                    using (AppleSignInHarness h = new AppleSignInHarness(first))
                    {
                        await h.Verifier.VerifyAsync(TestTokens.SignRs256(first, Claims()), ExternalTokenTypeEnum.Jwt, RawNonce, ct).ConfigureAwait(false);
                        h.Handler.MapJson("GET", JwksUrl, 200, TestTokens.Jwks(first, rotated));
                        await ExpectRejected(h, TestTokens.SignRs256(rotated, Claims()), RawNonce, "unknown key", ct).ConfigureAwait(false);
                        Check.Equal(1, h.Handler.Count(JwksUrl), "No refetch inside the minimum interval");
                    }
                }),
                Case("Nonce", "The nonce must equal SHA-256 hex of the raw nonce unless nonce_supported is false", async ct =>
                {
                    using (TestRsaKey key = new TestRsaKey("apple-1"))
                    using (AppleSignInHarness h = new AppleSignInHarness(key))
                    {
                        string token = TestTokens.SignRs256(key, Claims());
                        await ExpectRejected(h, token, "a-different-nonce", "nonce", ct).ConfigureAwait(false);
                        await ExpectRejected(h, token, null, "nonce", ct).ConfigureAwait(false);

                        JsonObject rawInToken = Claims();
                        rawInToken["nonce"] = RawNonce;
                        await ExpectRejected(h, TestTokens.SignRs256(key, rawInToken), RawNonce, "nonce", ct).ConfigureAwait(false);

                        JsonObject unsupported = Claims();
                        unsupported.Remove("nonce");
                        unsupported["nonce_supported"] = false;
                        await h.Verifier.VerifyAsync(TestTokens.SignRs256(key, unsupported), ExternalTokenTypeEnum.Jwt, RawNonce, ct).ConfigureAwait(false);
                    }
                }),
                Case("NotConfigured", "Without audiences the verifier reports not configured; settings changes apply live", async ct =>
                {
                    using (TestRsaKey key = new TestRsaKey("apple-1"))
                    using (AppleSignInHarness h = new AppleSignInHarness(key))
                    {
                        h.Settings.AppleBundleId = String.Empty;
                        h.Settings.AppleServicesId = String.Empty;
                        Check.True(!h.Verifier.IsConfigured, "Not configured");
                        await Check.ThrowsAsync<IntegrationNotConfiguredException>(() => h.Verifier.VerifyAsync("a.b.c", ExternalTokenTypeEnum.Jwt, RawNonce, ct), "Verify").ConfigureAwait(false);
                        h.Settings.AppleBundleId = BundleId;
                        Check.True(h.Verifier.IsConfigured, "Configured after a live change");
                        await Check.ThrowsAsync<IdentityTokenException>(() => h.Verifier.VerifyAsync("not-a-jwt", ExternalTokenTypeEnum.Jwt, RawNonce, ct), "Malformed").ConfigureAwait(false);
                        await Check.ThrowsAsync<IdentityTokenException>(() => h.Verifier.VerifyAsync("a.b.c", ExternalTokenTypeEnum.AccessToken, RawNonce, ct), "Access token type").ConfigureAwait(false);
                    }
                })
            });
        }

        #endregion

        #region Private-Methods

        private static JsonObject Claims()
        {
            long now = TestTokens.Now;
            return new JsonObject
            {
                ["iss"] = "https://appleid.apple.com",
                ["aud"] = BundleId,
                ["sub"] = "001234.apple.subject",
                ["iat"] = now,
                ["exp"] = now + 600,
                ["nonce"] = TestTokens.Sha256Hex(RawNonce),
                ["nonce_supported"] = true,
                ["email"] = "relay@privaterelay.appleid.com",
                ["email_verified"] = true
            };
        }

        private static async Task ExpectRejected(AppleSignInHarness h, string token, string? nonce, string what, CancellationToken ct)
        {
            await Check.ThrowsAsync<IdentityTokenException>(() => h.Verifier.VerifyAsync(token, ExternalTokenTypeEnum.Jwt, nonce, ct), what).ConfigureAwait(false);
        }

        private static TestCaseDescriptor Case(string caseId, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(Id, caseId, name, body, new[] { "fast", "unit", "integrations" });
        }

        #endregion
    }
}
