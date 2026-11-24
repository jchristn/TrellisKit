namespace Test.Shared.IntegrationTests
{
    using System;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
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
    /// App Store JWS verification (x5c chain to a runtime-generated root), notifications, and the Server API client.
    /// </summary>
    public static class AppStoreSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "IntegrationAppStore";

        #endregion

        #region Private-Members

        private const string BundleId = "com.example.app";
        private const string ProductId = "example.membership.annual";
        private const string ProductionUrl = "https://storekit.test";
        private const string SandboxUrl = "https://storekit-sandbox.test";
        private const string AccountToken = "6f1c2a7e-4b7d-4c3e-9a51-0d2b8f6e1a11";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "App Store verification", new[]
            {
                Case("EmbeddedRoot.Fingerprint", "The embedded Apple Root CA G3 matches Apple's published SHA-256 fingerprint", ct =>
                {
                    using (X509Certificate2 root = AppStoreClient.LoadEmbeddedAppleRoot())
                    {
                        string fingerprint = Convert.ToHexString(SHA256.HashData(root.RawData));
                        string expected = "63343ABFB89A6A03EBB57E9B3F5FA7BE7C4F5C756F3017B3A8C488C3653E9179";
                        Check.Equal(expected, fingerprint, "SHA-256 fingerprint");
                        Check.Equal(expected, AppStoreClient.AppleRootCaG3Sha256Fingerprint.Replace(":", String.Empty), "Published constant");
                        Check.True(root.Subject.Contains("CN=Apple Root CA - G3", StringComparison.Ordinal), "Subject");
                        Check.True(root.NotAfter.ToUniversalTime() > new DateTime(2039, 1, 1, 0, 0, 0, DateTimeKind.Utc), "Valid until 2039");
                    }
                    return Task.CompletedTask;
                }),
                Case("Transaction.Valid", "A transaction signed under the trusted chain is decoded", async ct =>
                {
                    using (TestAppleChain chain = new TestAppleChain())
                    using (TestHttp http = new TestHttp())
                    {
                        AppStoreClient client = new AppStoreClient(Settings(), http.Client, chain.Root, ProductionUrl, SandboxUrl);
                        Check.True(client.IsConfigured, "Configured");
                        AppStoreTransaction transaction = await client.VerifyTransactionAsync(chain.Sign(Transaction("2000000001", "Sandbox", 0)), ct).ConfigureAwait(false);
                        Check.Equal("2000000001", transaction.TransactionId, "Transaction id");
                        Check.Equal("1000000001", transaction.OriginalTransactionId, "Original id");
                        Check.Equal(ProductId, transaction.ProductId, "Product");
                        Check.Equal(BundleId, transaction.BundleId, "Bundle");
                        Check.Equal(StoreEnvironmentEnum.Sandbox, transaction.Environment, "Environment");
                        Check.Equal((Guid?)Guid.Parse(AccountToken), transaction.AppAccountToken, "appAccountToken");
                        Check.True(transaction.ExpiresUtc != null && transaction.ExpiresUtc.Value > DateTime.UtcNow, "Expiry decoded");
                        Check.True(transaction.RevocationUtc == null, "Not revoked");
                        Check.True(transaction.RawPayloadJson.Contains("\"transactionId\"", StringComparison.Ordinal), "Raw payload kept");
                    }
                }),
                Case("Transaction.WrongRoot", "A chain that does not build to the trusted root is rejected", async ct =>
                {
                    using (TestAppleChain chain = new TestAppleChain())
                    using (TestAppleChain other = new TestAppleChain())
                    using (TestHttp http = new TestHttp())
                    {
                        AppStoreClient client = new AppStoreClient(Settings(), http.Client, other.Root, ProductionUrl, SandboxUrl);
                        await Check.ThrowsAsync<StoreVerificationException>(() => client.VerifyTransactionAsync(chain.Sign(Transaction("2000000001", "Sandbox", 0)), ct), "Wrong root").ConfigureAwait(false);

                        AppStoreClient embedded = new AppStoreClient(Settings(), http.Client, null, ProductionUrl, SandboxUrl);
                        await Check.ThrowsAsync<StoreVerificationException>(() => embedded.VerifyTransactionAsync(chain.Sign(Transaction("2000000001", "Sandbox", 0)), ct), "Embedded Apple root").ConfigureAwait(false);
                    }
                }),
                Case("Transaction.MissingMarker", "A leaf without the App Store marker extension is rejected", async ct =>
                {
                    using (TestAppleChain chain = new TestAppleChain(false))
                    using (TestHttp http = new TestHttp())
                    {
                        AppStoreClient client = new AppStoreClient(Settings(), http.Client, chain.Root, ProductionUrl, SandboxUrl);
                        await Check.ThrowsAsync<StoreVerificationException>(() => client.VerifyTransactionAsync(chain.Sign(Transaction("2000000001", "Sandbox", 0)), ct), "No marker").ConfigureAwait(false);
                    }
                }),
                Case("Transaction.Tampered", "A modified payload or signature fails verification", async ct =>
                {
                    using (TestAppleChain chain = new TestAppleChain())
                    using (TestHttp http = new TestHttp())
                    {
                        AppStoreClient client = new AppStoreClient(Settings(), http.Client, chain.Root, ProductionUrl, SandboxUrl);
                        string signed = chain.Sign(Transaction("2000000001", "Sandbox", 0));
                        JsonObject altered = Transaction("2000000001", "Sandbox", 0);
                        altered["expiresDate"] = DateTimeOffset.UtcNow.AddYears(50).ToUnixTimeMilliseconds();
                        await Check.ThrowsAsync<StoreVerificationException>(() => client.VerifyTransactionAsync(TestTokens.ReplacePayload(signed, altered), ct), "Tampered payload").ConfigureAwait(false);
                        await Check.ThrowsAsync<StoreVerificationException>(() => client.VerifyTransactionAsync("not.a.jws", ct), "Malformed").ConfigureAwait(false);

                        string unsignedHeader = TestTokens.SignEs256(chain.LeafKey, new JsonObject(), Transaction("2000000001", "Sandbox", 0));
                        await Check.ThrowsAsync<StoreVerificationException>(() => client.VerifyTransactionAsync(unsignedHeader, ct), "No x5c").ConfigureAwait(false);
                    }
                }),
                Case("Transaction.SandboxAndBundle", "Sandbox is rejected unless allowed, and another bundle id is rejected", async ct =>
                {
                    using (TestAppleChain chain = new TestAppleChain())
                    using (TestHttp http = new TestHttp())
                    {
                        AppStoreSettings settings = Settings();
                        settings.AllowSandbox = false;
                        AppStoreClient client = new AppStoreClient(settings, http.Client, chain.Root, ProductionUrl, SandboxUrl);
                        await Check.ThrowsAsync<StoreVerificationException>(() => client.VerifyTransactionAsync(chain.Sign(Transaction("2000000001", "Sandbox", 0)), ct), "Sandbox rejected").ConfigureAwait(false);
                        AppStoreTransaction production = await client.VerifyTransactionAsync(chain.Sign(Transaction("2000000001", "Production", 0)), ct).ConfigureAwait(false);
                        Check.Equal(StoreEnvironmentEnum.Production, production.Environment, "Production accepted");

                        JsonObject otherApp = Transaction("2000000001", "Production", 0);
                        otherApp["bundleId"] = "com.someone.else";
                        await Check.ThrowsAsync<StoreVerificationException>(() => client.VerifyTransactionAsync(chain.Sign(otherApp), ct), "Other bundle").ConfigureAwait(false);

                        settings.AllowSandbox = true;
                        await client.VerifyTransactionAsync(chain.Sign(Transaction("2000000001", "Sandbox", 0)), ct).ConfigureAwait(false);
                    }
                }),
                Case("Notification.Decode", "A notification with nested transaction and renewal info is decoded", async ct =>
                {
                    using (TestAppleChain chain = new TestAppleChain())
                    using (TestHttp http = new TestHttp())
                    {
                        AppStoreClient client = new AppStoreClient(Settings(), http.Client, chain.Root, ProductionUrl, SandboxUrl);
                        JsonObject renewal = new JsonObject
                        {
                            ["originalTransactionId"] = "1000000001",
                            ["autoRenewProductId"] = ProductId,
                            ["autoRenewStatus"] = 0,
                            ["environment"] = "Sandbox",
                            ["signedDate"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        };
                        JsonObject payload = new JsonObject
                        {
                            ["notificationType"] = "DID_CHANGE_RENEWAL_STATUS",
                            ["subtype"] = "AUTO_RENEW_DISABLED",
                            ["notificationUUID"] = "6a0a5c0e-1111-2222-3333-444455556666",
                            ["version"] = "2.0",
                            ["signedDate"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            ["data"] = new JsonObject
                            {
                                ["bundleId"] = BundleId,
                                ["environment"] = "Sandbox",
                                ["signedTransactionInfo"] = chain.Sign(Transaction("2000000002", "Sandbox", 0)),
                                ["signedRenewalInfo"] = chain.Sign(renewal),
                                ["status"] = 1
                            }
                        };
                        AppStoreNotification notification = await client.DecodeNotificationAsync(chain.Sign(payload), ct).ConfigureAwait(false);
                        Check.Equal("DID_CHANGE_RENEWAL_STATUS", notification.NotificationType, "Type");
                        Check.Equal("AUTO_RENEW_DISABLED", notification.Subtype, "Subtype");
                        Check.Equal("6a0a5c0e-1111-2222-3333-444455556666", notification.NotificationUuid, "UUID");
                        Check.Equal(StoreEnvironmentEnum.Sandbox, notification.Environment, "Environment");
                        Check.True(notification.Transaction != null && notification.Transaction.TransactionId == "2000000002", "Nested transaction");
                        Check.Equal((bool?)false, notification.AutoRenewEnabled, "Auto renew");

                        JsonObject foreign = JsonNode.Parse(payload.ToJsonString())!.AsObject();
                        foreign["data"]!["bundleId"] = "com.someone.else";
                        await Check.ThrowsAsync<StoreVerificationException>(() => client.DecodeNotificationAsync(chain.Sign(foreign), ct), "Other bundle").ConfigureAwait(false);

                        JsonObject test = new JsonObject
                        {
                            ["notificationType"] = "TEST",
                            ["notificationUUID"] = "test-uuid",
                            ["data"] = new JsonObject { ["bundleId"] = BundleId, ["environment"] = "Sandbox" }
                        };
                        AppStoreNotification testNotification = await client.DecodeNotificationAsync(chain.Sign(test), ct).ConfigureAwait(false);
                        Check.True(testNotification.Transaction == null && testNotification.AutoRenewEnabled == null, "TEST has no transaction");
                    }
                }),
                Case("Api.LatestTransaction", "The Server API is called with an ES256 token and the latest verified transaction is returned", async ct =>
                {
                    using (TestAppleChain chain = new TestAppleChain())
                    using (ECDsa apiKey = ECDsa.Create(ECCurve.NamedCurves.nistP256))
                    using (TestHttp http = new TestHttp())
                    {
                        AppStoreSettings settings = Settings();
                        settings.IssuerId = "57246542-96fe-1a63-e053-0824d011072a";
                        settings.KeyId = "2X9R4HXF34";
                        settings.PrivateKeyPem = apiKey.ExportPkcs8PrivateKeyPem();
                        AppStoreClient client = new AppStoreClient(settings, http.Client, chain.Root, ProductionUrl, SandboxUrl);

                        JsonObject response = new JsonObject
                        {
                            ["environment"] = "Production",
                            ["bundleId"] = BundleId,
                            ["data"] = new JsonArray(new JsonObject
                            {
                                ["subscriptionGroupIdentifier"] = "21000000",
                                ["lastTransactions"] = new JsonArray(
                                    new JsonObject { ["originalTransactionId"] = "1000000001", ["status"] = 2, ["signedTransactionInfo"] = chain.Sign(Transaction("2000000001", "Production", -400)) },
                                    new JsonObject { ["originalTransactionId"] = "1000000001", ["status"] = 1, ["signedTransactionInfo"] = chain.Sign(Transaction("2000000009", "Production", 0)) })
                            })
                        };
                        http.Handler.MapJson("GET", ProductionUrl + "/inApps/v1/subscriptions/", 200, response.ToJsonString());

                        AppStoreTransaction? latest = await client.GetLatestTransactionAsync("1000000001", StoreEnvironmentEnum.Production, ct).ConfigureAwait(false);
                        Check.True(latest != null && latest.TransactionId == "2000000009", "Latest transaction returned");

                        RecordedRequest request = http.Handler.Requests[0];
                        Check.Equal(ProductionUrl + "/inApps/v1/subscriptions/1000000001", request.Url.AbsoluteUri, "URL");
                        string? authorization = request.Header("Authorization");
                        Check.True(authorization != null && authorization.StartsWith("Bearer ", StringComparison.Ordinal), "Bearer token");
                        string jwt = authorization!.Substring(7);
                        JsonObject header = TestTokens.DecodeSegment(jwt, 0);
                        JsonObject claims = TestTokens.DecodeSegment(jwt, 1);
                        Check.Equal("ES256", (string?)header["alg"], "alg");
                        Check.Equal("2X9R4HXF34", (string?)header["kid"], "kid");
                        Check.Equal("57246542-96fe-1a63-e053-0824d011072a", (string?)claims["iss"], "iss");
                        Check.Equal("appstoreconnect-v1", (string?)claims["aud"], "aud");
                        Check.Equal(BundleId, (string?)claims["bid"], "bid");
                        Check.Equal(300L, (long)claims["exp"]! - (long)claims["iat"]!, "5 minute lifetime");
                        string[] parts = jwt.Split('.');
                        bool valid = apiKey.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), TestTokens.FromBase64Url(parts[2]), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
                        Check.True(valid, "Token signed by the configured .p8 key");

                        http.Handler.MapJson("GET", SandboxUrl + "/inApps/v1/subscriptions/", 404, "{\"errorCode\":4040010,\"errorMessage\":\"Transaction id not found.\"}");
                        AppStoreTransaction? missing = await client.GetLatestTransactionAsync("1000000001", StoreEnvironmentEnum.Sandbox, ct).ConfigureAwait(false);
                        Check.True(missing == null, "404 returns null");

                        http.Handler.MapJson("GET", ProductionUrl + "/inApps/v1/subscriptions/", 500, "{\"errorCode\":5000000,\"errorMessage\":\"An unknown error occurred.\"}");
                        await Check.ThrowsAsync<StoreVerificationException>(() => client.GetLatestTransactionAsync("1000000001", StoreEnvironmentEnum.Production, ct), "Server error").ConfigureAwait(false);
                    }
                }),
                Case("Api.NotConfigured", "API calls name the missing setting; no bundle id means not configured", async ct =>
                {
                    using (TestHttp http = new TestHttp())
                    {
                        AppStoreSettings settings = Settings();
                        AppStoreClient client = new AppStoreClient(settings, http.Client, null, ProductionUrl, SandboxUrl);
                        IntegrationNotConfiguredException e = await Check.ThrowsAsync<IntegrationNotConfiguredException>(() => client.GetLatestTransactionAsync("1", StoreEnvironmentEnum.Production, ct), "No issuer").ConfigureAwait(false);
                        Check.True(e.Message.Contains("IssuerId", StringComparison.Ordinal), "Names IssuerId");
                        settings.IssuerId = "issuer";
                        e = await Check.ThrowsAsync<IntegrationNotConfiguredException>(() => client.GetLatestTransactionAsync("1", StoreEnvironmentEnum.Production, ct), "No key id").ConfigureAwait(false);
                        Check.True(e.Message.Contains("KeyId", StringComparison.Ordinal), "Names KeyId");
                        settings.KeyId = "key";
                        e = await Check.ThrowsAsync<IntegrationNotConfiguredException>(() => client.GetLatestTransactionAsync("1", StoreEnvironmentEnum.Production, ct), "No key").ConfigureAwait(false);
                        Check.True(e.Message.Contains("PrivateKeyPem", StringComparison.Ordinal), "Names PrivateKeyPem");

                        settings.BundleId = String.Empty;
                        Check.True(!client.IsConfigured, "Not configured without a bundle id");
                        await Check.ThrowsAsync<IntegrationNotConfiguredException>(() => client.VerifyTransactionAsync("a.b.c", ct), "Verify").ConfigureAwait(false);
                        Check.Equal(0, http.Handler.Requests.Count, "No network calls");
                    }
                })
            });
        }

        #endregion

        #region Private-Methods

        private static AppStoreSettings Settings()
        {
            return new AppStoreSettings { BundleId = BundleId, AllowSandbox = true };
        }

        private static JsonObject Transaction(string transactionId, string environment, int expiresInDays)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset purchase = now.AddDays(expiresInDays - 365);
            return new JsonObject
            {
                ["transactionId"] = transactionId,
                ["originalTransactionId"] = "1000000001",
                ["bundleId"] = BundleId,
                ["productId"] = ProductId,
                ["subscriptionGroupIdentifier"] = "21000000",
                ["purchaseDate"] = purchase.ToUnixTimeMilliseconds(),
                ["originalPurchaseDate"] = purchase.ToUnixTimeMilliseconds(),
                ["expiresDate"] = now.AddDays(expiresInDays).AddDays(1).ToUnixTimeMilliseconds(),
                ["quantity"] = 1,
                ["type"] = "Auto-Renewable Subscription",
                ["appAccountToken"] = AccountToken,
                ["inAppOwnershipType"] = "PURCHASED",
                ["signedDate"] = now.ToUnixTimeMilliseconds(),
                ["environment"] = environment,
                ["transactionReason"] = "PURCHASE",
                ["storefront"] = "USA"
            };
        }

        private static TestCaseDescriptor Case(string caseId, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(Id, caseId, name, body, new[] { "fast", "unit", "integrations" });
        }

        #endregion
    }
}
