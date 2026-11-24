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
    /// Google Play Developer API client (service account OAuth, subscriptionsv2, acknowledge) and Pub/Sub push verification.
    /// </summary>
    public static class GooglePlaySuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "IntegrationGooglePlay";

        #endregion

        #region Private-Members

        private const string ApiUrl = "https://androidpublisher.test";
        private const string TokenUrl = "https://oauth2.test/token";
        private const string JwksUrl = "https://google.test/oauth2/v3/certs";
        private const string PackageName = "com.example.app";
        private const string ProductId = "example.membership.annual";
        private const string PurchaseToken = "purchase-token-abc";
        private const string ServiceEmail = "play-api@example-project.iam.gserviceaccount.com";
        private const string PushAudience = "https://api.example.test/api/v1/webhooks/google";
        private const string PushEmail = "pubsub-push@example-project.iam.gserviceaccount.com";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Google Play verification", new[]
            {
                Case("Subscription.Get", "The service account token is obtained once and the subscription is mapped", async ct =>
                {
                    using (RSA serviceKey = RSA.Create(2048))
                    using (TestHttp http = new TestHttp())
                    {
                        GooglePlayClient client = new GooglePlayClient(Settings(serviceKey), http.Client, ApiUrl, TokenUrl, JwksUrl);
                        Check.True(client.IsConfigured, "Configured");
                        MapToken(http);
                        http.Handler.MapJson("GET", ApiUrl + "/androidpublisher/v3/applications/", 200, SubscriptionJson());

                        GooglePlaySubscription subscription = await client.GetSubscriptionAsync(PurchaseToken, ct).ConfigureAwait(false);
                        Check.Equal(PurchaseToken, subscription.PurchaseToken, "Token");
                        Check.Equal(ProductId, subscription.ProductId, "Product");
                        Check.Equal(GooglePlaySubscriptionStateEnum.Active, subscription.State, "State");
                        Check.Equal("GPA.1234-5678-9012-34567", subscription.LatestOrderId, "Order");
                        Check.Equal((DateTime?)new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc), subscription.StartUtc, "Start");
                        Check.Equal((DateTime?)new DateTime(2027, 1, 15, 12, 0, 0, DateTimeKind.Utc), subscription.ExpiryUtc, "Expiry");
                        Check.True(subscription.AutoRenewing, "Auto renewing");
                        Check.True(!subscription.Acknowledged, "Not acknowledged");
                        Check.Equal("6f1c2a7e-4b7d-4c3e-9a51-0d2b8f6e1a11", subscription.ObfuscatedAccountId, "Account id");
                        Check.Equal("older-token", subscription.LinkedPurchaseToken, "Linked token");
                        Check.Equal(StoreEnvironmentEnum.Sandbox, subscription.Environment, "testPurchase means Sandbox");

                        await client.GetSubscriptionAsync(PurchaseToken, ct).ConfigureAwait(false);
                        Check.Equal(1, http.Handler.Count(TokenUrl), "Access token cached");

                        RecordedRequest tokenRequest = http.Handler.Requests[0];
                        Dictionary<string, string> form = tokenRequest.Form();
                        Check.Equal("urn:ietf:params:oauth:grant-type:jwt-bearer", form["grant_type"], "Grant type");
                        string assertion = form["assertion"];
                        JsonObject header = TestTokens.DecodeSegment(assertion, 0);
                        JsonObject claims = TestTokens.DecodeSegment(assertion, 1);
                        Check.Equal("RS256", (string?)header["alg"], "alg");
                        Check.Equal("key-id-1", (string?)header["kid"], "kid");
                        Check.Equal(ServiceEmail, (string?)claims["iss"], "iss");
                        Check.Equal("https://www.googleapis.com/auth/androidpublisher", (string?)claims["scope"], "scope");
                        Check.Equal(TokenUrl, (string?)claims["aud"], "aud");
                        string[] parts = assertion.Split('.');
                        Check.True(serviceKey.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), TestTokens.FromBase64Url(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), "Assertion signed by the service account key");

                        RecordedRequest api = http.Handler.Requests[1];
                        Check.Equal(ApiUrl + "/androidpublisher/v3/applications/" + PackageName + "/purchases/subscriptionsv2/tokens/" + PurchaseToken, api.Url.AbsoluteUri, "API URL");
                        Check.Equal("Bearer ya29.test-access-token", api.Header("Authorization"), "Bearer access token");
                    }
                }),
                Case("Subscription.Errors", "Unknown tokens and API errors become StoreVerificationException", async ct =>
                {
                    using (RSA serviceKey = RSA.Create(2048))
                    using (TestHttp http = new TestHttp())
                    {
                        GooglePlayClient client = new GooglePlayClient(Settings(serviceKey), http.Client, ApiUrl, TokenUrl, JwksUrl);
                        MapToken(http);
                        http.Handler.MapJson("GET", ApiUrl + "/androidpublisher/", 404, "{\"error\":{\"code\":404,\"message\":\"The purchase token was not found.\",\"status\":\"NOT_FOUND\"}}");
                        await Check.ThrowsAsync<StoreVerificationException>(() => client.GetSubscriptionAsync("unknown", ct), "Unknown token").ConfigureAwait(false);
                        http.Handler.MapJson("POST", TokenUrl, 400, "{\"error\":\"invalid_grant\"}");
                        GooglePlayClient fresh = new GooglePlayClient(Settings(serviceKey), http.Client, ApiUrl, TokenUrl, JwksUrl);
                        await Check.ThrowsAsync<StoreVerificationException>(() => fresh.GetSubscriptionAsync(PurchaseToken, ct), "OAuth failure").ConfigureAwait(false);
                    }
                }),
                Case("Acknowledge", "Acknowledge posts to the subscriptions acknowledge endpoint", async ct =>
                {
                    using (RSA serviceKey = RSA.Create(2048))
                    using (TestHttp http = new TestHttp())
                    {
                        GooglePlayClient client = new GooglePlayClient(Settings(serviceKey), http.Client, ApiUrl, TokenUrl, JwksUrl);
                        MapToken(http);
                        http.Handler.MapJson("POST", ApiUrl + "/androidpublisher/", 200, "{}");
                        await client.AcknowledgeAsync(ProductId, PurchaseToken, ct).ConfigureAwait(false);
                        RecordedRequest request = http.Handler.Requests[1];
                        Check.Equal("POST", request.Method, "Method");
                        Check.Equal(ApiUrl + "/androidpublisher/v3/applications/" + PackageName + "/purchases/subscriptions/" + ProductId + "/tokens/" + PurchaseToken + ":acknowledge", request.Url.AbsoluteUri, "URL");
                    }
                }),
                Case("PubSub.Valid", "A push with a valid Google OIDC token decodes the subscription notification", async ct =>
                {
                    using (RSA serviceKey = RSA.Create(2048))
                    using (TestRsaKey googleKey = new TestRsaKey("google-push-1"))
                    using (TestHttp http = new TestHttp())
                    {
                        http.Handler.MapJson("GET", JwksUrl, 200, TestTokens.Jwks(googleKey));
                        GooglePlayClient client = new GooglePlayClient(Settings(serviceKey), http.Client, ApiUrl, TokenUrl, JwksUrl);
                        GooglePlayNotification notification = await client.DecodeNotificationAsync(PushBody(SubscriptionNotification(PackageName)), "Bearer " + TestTokens.SignRs256(googleKey, PushClaims()), ct).ConfigureAwait(false);
                        Check.Equal("1234567890123456", notification.MessageId, "Message id");
                        Check.Equal(PackageName, notification.PackageName, "Package");
                        Check.Equal(PurchaseToken, notification.PurchaseToken, "Purchase token");
                        Check.Equal(ProductId, notification.SubscriptionId, "Subscription id");
                        Check.Equal(4, notification.NotificationType, "Type");
                        Check.True(!notification.IsTest, "Not a test");

                        JsonObject test = new JsonObject { ["version"] = "1.0", ["packageName"] = PackageName, ["eventTimeMillis"] = "1700000000000", ["testNotification"] = new JsonObject { ["version"] = "1.0" } };
                        GooglePlayNotification testNotification = await client.DecodeNotificationAsync(PushBody(test), "Bearer " + TestTokens.SignRs256(googleKey, PushClaims()), ct).ConfigureAwait(false);
                        Check.True(testNotification.IsTest && testNotification.PurchaseToken == null, "Test notification");
                    }
                }),
                Case("PubSub.Rejects", "Wrong audience, wrong email, unverified email, missing header, and another package are rejected", async ct =>
                {
                    using (RSA serviceKey = RSA.Create(2048))
                    using (TestRsaKey googleKey = new TestRsaKey("google-push-1"))
                    using (TestHttp http = new TestHttp())
                    {
                        http.Handler.MapJson("GET", JwksUrl, 200, TestTokens.Jwks(googleKey));
                        GooglePlayClient client = new GooglePlayClient(Settings(serviceKey), http.Client, ApiUrl, TokenUrl, JwksUrl);
                        string body = PushBody(SubscriptionNotification(PackageName));

                        JsonObject wrongAudience = PushClaims();
                        wrongAudience["aud"] = "https://elsewhere.test/hook";
                        await Check.ThrowsAsync<WebhookSignatureException>(() => client.DecodeNotificationAsync(body, "Bearer " + TestTokens.SignRs256(googleKey, wrongAudience), ct), "Wrong audience").ConfigureAwait(false);

                        JsonObject wrongEmail = PushClaims();
                        wrongEmail["email"] = "attacker@evil.iam.gserviceaccount.com";
                        await Check.ThrowsAsync<WebhookSignatureException>(() => client.DecodeNotificationAsync(body, "Bearer " + TestTokens.SignRs256(googleKey, wrongEmail), ct), "Wrong email").ConfigureAwait(false);

                        JsonObject unverified = PushClaims();
                        unverified["email_verified"] = false;
                        await Check.ThrowsAsync<WebhookSignatureException>(() => client.DecodeNotificationAsync(body, "Bearer " + TestTokens.SignRs256(googleKey, unverified), ct), "Unverified email").ConfigureAwait(false);

                        await Check.ThrowsAsync<WebhookSignatureException>(() => client.DecodeNotificationAsync(body, null, ct), "Missing header").ConfigureAwait(false);
                        await Check.ThrowsAsync<WebhookSignatureException>(() => client.DecodeNotificationAsync(body, "Basic abc", ct), "Wrong scheme").ConfigureAwait(false);

                        string otherPackage = PushBody(SubscriptionNotification("com.someone.else"));
                        await Check.ThrowsAsync<StoreVerificationException>(() => client.DecodeNotificationAsync(otherPackage, "Bearer " + TestTokens.SignRs256(googleKey, PushClaims()), ct), "Other package").ConfigureAwait(false);
                    }
                }),
                Case("NotConfigured", "Package name and service account are required; push decoding also needs audience and email", async ct =>
                {
                    using (TestHttp http = new TestHttp())
                    {
                        GooglePlaySettings settings = new GooglePlaySettings();
                        GooglePlayClient client = new GooglePlayClient(settings, http.Client, ApiUrl, TokenUrl, JwksUrl);
                        Check.True(!client.IsConfigured, "Not configured");
                        await Check.ThrowsAsync<IntegrationNotConfiguredException>(() => client.GetSubscriptionAsync(PurchaseToken, ct), "Get").ConfigureAwait(false);
                        settings.PackageName = PackageName;
                        IntegrationNotConfiguredException e = await Check.ThrowsAsync<IntegrationNotConfiguredException>(() => client.DecodeNotificationAsync("{}", "Bearer x", ct), "Push").ConfigureAwait(false);
                        Check.True(e.Message.Contains("PubSubAudience", StringComparison.Ordinal), "Names PubSubAudience");
                        Check.Equal(0, http.Handler.Requests.Count, "No network calls");
                    }
                })
            });
        }

        #endregion

        #region Private-Methods

        private static GooglePlaySettings Settings(RSA serviceKey)
        {
            JsonObject account = new JsonObject
            {
                ["type"] = "service_account",
                ["project_id"] = "example-project",
                ["private_key_id"] = "key-id-1",
                ["private_key"] = serviceKey.ExportPkcs8PrivateKeyPem(),
                ["client_email"] = ServiceEmail,
                ["token_uri"] = TokenUrl
            };
            return new GooglePlaySettings
            {
                PackageName = PackageName,
                ServiceAccountJson = account.ToJsonString(),
                PubSubAudience = PushAudience,
                PubSubServiceAccountEmail = PushEmail
            };
        }

        private static void MapToken(TestHttp http)
        {
            http.Handler.MapJson("POST", TokenUrl, 200, "{\"access_token\":\"ya29.test-access-token\",\"expires_in\":3599,\"token_type\":\"Bearer\"}");
        }

        private static string SubscriptionJson()
        {
            return "{\"kind\":\"androidpublisher#subscriptionPurchaseV2\",\"startTime\":\"2026-01-15T12:00:00Z\",\"regionCode\":\"US\","
                + "\"subscriptionState\":\"SUBSCRIPTION_STATE_ACTIVE\",\"latestOrderId\":\"GPA.1234-5678-9012-34567\","
                + "\"linkedPurchaseToken\":\"older-token\",\"acknowledgementState\":\"ACKNOWLEDGEMENT_STATE_PENDING\","
                + "\"externalAccountIdentifiers\":{\"obfuscatedExternalAccountId\":\"6f1c2a7e-4b7d-4c3e-9a51-0d2b8f6e1a11\"},"
                + "\"testPurchase\":{},"
                + "\"lineItems\":[{\"productId\":\"" + ProductId + "\",\"expiryTime\":\"2027-01-15T12:00:00.000Z\",\"autoRenewingPlan\":{\"autoRenewEnabled\":true}}]}";
        }

        private static JsonObject SubscriptionNotification(string packageName)
        {
            return new JsonObject
            {
                ["version"] = "1.0",
                ["packageName"] = packageName,
                ["eventTimeMillis"] = "1700000000000",
                ["subscriptionNotification"] = new JsonObject
                {
                    ["version"] = "1.0",
                    ["notificationType"] = 4,
                    ["purchaseToken"] = PurchaseToken,
                    ["subscriptionId"] = ProductId
                }
            };
        }

        private static string PushBody(JsonObject developerNotification)
        {
            JsonObject body = new JsonObject
            {
                ["message"] = new JsonObject
                {
                    ["data"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(developerNotification.ToJsonString())),
                    ["messageId"] = "1234567890123456",
                    ["publishTime"] = "2026-11-11T00:00:00Z"
                },
                ["subscription"] = "projects/example-project/subscriptions/play-rtdn"
            };
            return body.ToJsonString();
        }

        private static JsonObject PushClaims()
        {
            long now = TestTokens.Now;
            return new JsonObject
            {
                ["iss"] = "https://accounts.google.com",
                ["aud"] = PushAudience,
                ["azp"] = "1234567890",
                ["email"] = PushEmail,
                ["email_verified"] = true,
                ["sub"] = "1234567890",
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
