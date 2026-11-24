namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Google Play Developer API client and Pub/Sub push verification.
    /// API calls authenticate as the service account in <see cref="GooglePlaySettings.ServiceAccountJson"/> (RS256 JWT
    /// bearer grant, scope androidpublisher; the access token is cached until shortly before it expires). Pub/Sub pushes
    /// must carry a Google-signed OIDC token (RS256 against Google's JWKS, issuer accounts.google.com, audience
    /// <see cref="GooglePlaySettings.PubSubAudience"/>, email <see cref="GooglePlaySettings.PubSubServiceAccountEmail"/>
    /// with email_verified) and a developer notification for the configured package.
    /// Settings are read on every call. Thread safe.
    /// </summary>
    public class GooglePlayClient : IGooglePlayClient
    {
        #region Public-Members

        /// <summary>
        /// OAuth scope for the Android Publisher API.
        /// </summary>
        public const string Scope = "https://www.googleapis.com/auth/androidpublisher";

        /// <summary>
        /// Accepted issuers of Pub/Sub push tokens.
        /// </summary>
        public static readonly IReadOnlyList<string> PushTokenIssuers = new[] { "accounts.google.com", "https://accounts.google.com" };

        /// <summary>
        /// How long before expiry a cached access token is replaced. Default 5 minutes. Minimum 0, maximum 30 minutes.
        /// </summary>
        public TimeSpan AccessTokenRefreshMargin
        {
            get { return _AccessTokenRefreshMargin; }
            set
            {
                if (value < TimeSpan.Zero) value = TimeSpan.Zero;
                if (value > TimeSpan.FromMinutes(30)) value = TimeSpan.FromMinutes(30);
                _AccessTokenRefreshMargin = value;
            }
        }

        /// <summary>
        /// Minimum time between JWKS fetches when push tokens name an unknown kid. Default 60 seconds. Minimum 0, maximum 1 hour.
        /// </summary>
        public TimeSpan JwksMinimumRefetchInterval
        {
            get { return _Jwks.MinimumRefetchInterval; }
            set { _Jwks.MinimumRefetchInterval = value; }
        }

        /// <inheritdoc />
        public bool IsConfigured
        {
            get { return !String.IsNullOrWhiteSpace(_Settings.PackageName) && !String.IsNullOrWhiteSpace(_Settings.ServiceAccountJson); }
        }

        #endregion

        #region Private-Members

        private readonly GooglePlaySettings _Settings;
        private readonly HttpClient _HttpClient;
        private readonly string _ApiBaseUrl;
        private readonly string _TokenUrl;
        private readonly string _JwksUrl;
        private readonly JwksCache _Jwks;
        private readonly SemaphoreSlim _TokenLock = new SemaphoreSlim(1, 1);
        private TimeSpan _AccessTokenRefreshMargin = TimeSpan.FromMinutes(5);
        private string? _AccessToken = null;
        private DateTime _AccessTokenExpiresUtc = DateTime.MinValue;
        private string? _AccessTokenAccountFingerprint = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the client.
        /// </summary>
        /// <param name="settings">Google Play settings (live instance).</param>
        /// <param name="httpClient">HTTP client.</param>
        /// <param name="apiBaseUrl">Android Publisher API base URL (override for tests).</param>
        /// <param name="tokenUrl">OAuth token endpoint for the service account (override for tests).</param>
        /// <param name="jwksUrl">Google JWKS URL used to verify Pub/Sub push tokens (override for tests).</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public GooglePlayClient(GooglePlaySettings settings, HttpClient httpClient, string apiBaseUrl = "https://androidpublisher.googleapis.com", string tokenUrl = "https://oauth2.googleapis.com/token", string jwksUrl = "https://www.googleapis.com/oauth2/v3/certs")
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _ApiBaseUrl = apiBaseUrl ?? throw new ArgumentNullException(nameof(apiBaseUrl));
            _TokenUrl = tokenUrl ?? throw new ArgumentNullException(nameof(tokenUrl));
            _JwksUrl = jwksUrl ?? throw new ArgumentNullException(nameof(jwksUrl));
            _Jwks = new JwksCache(_HttpClient, _JwksUrl);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<GooglePlaySubscription> GetSubscriptionAsync(string purchaseToken, CancellationToken token = default)
        {
            string packageName = RequirePackageName();
            RequireServiceAccount();
            if (String.IsNullOrWhiteSpace(purchaseToken)) throw new StoreVerificationException("The Google Play purchase token is empty.");

            string url = _ApiBaseUrl.TrimEnd('/') + "/androidpublisher/v3/applications/" + Uri.EscapeDataString(packageName)
                + "/purchases/subscriptionsv2/tokens/" + Uri.EscapeDataString(purchaseToken.Trim());
            ApiResponse response = await SendApiAsync(HttpMethod.Get, url, null, token).ConfigureAwait(false);
            if (response.Status == HttpStatusCode.NotFound || response.Status == HttpStatusCode.Gone)
            {
                throw new StoreVerificationException("Google Play does not recognize the purchase token (HTTP " + (int)response.Status + ").");
            }
            if (!IsSuccess(response.Status)) throw new StoreVerificationException("Google Play subscriptionsv2.get returned HTTP " + (int)response.Status + ApiError(response.Body) + ".");

            try
            {
                using (JsonDocument document = JsonDocument.Parse(response.Body))
                {
                    return MapSubscription(document.RootElement, purchaseToken.Trim(), response.Body);
                }
            }
            catch (JsonException e)
            {
                throw new StoreVerificationException("Google Play subscriptionsv2.get returned invalid JSON.", e);
            }
        }

        /// <inheritdoc />
        public async Task AcknowledgeAsync(string productId, string purchaseToken, CancellationToken token = default)
        {
            string packageName = RequirePackageName();
            RequireServiceAccount();
            if (String.IsNullOrWhiteSpace(productId)) throw new StoreVerificationException("The Google Play product id is empty.");
            if (String.IsNullOrWhiteSpace(purchaseToken)) throw new StoreVerificationException("The Google Play purchase token is empty.");

            string url = _ApiBaseUrl.TrimEnd('/') + "/androidpublisher/v3/applications/" + Uri.EscapeDataString(packageName)
                + "/purchases/subscriptions/" + Uri.EscapeDataString(productId.Trim())
                + "/tokens/" + Uri.EscapeDataString(purchaseToken.Trim()) + ":acknowledge";
            ApiResponse response = await SendApiAsync(HttpMethod.Post, url, "{}", token).ConfigureAwait(false);
            if (IsSuccess(response.Status)) return;

            if ((int)response.Status >= 400 && (int)response.Status < 500 && response.Status != HttpStatusCode.Unauthorized && response.Status != HttpStatusCode.Forbidden)
            {
                // Acknowledging twice can be refused; treat an already acknowledged purchase as success.
                GooglePlaySubscription current = await GetSubscriptionAsync(purchaseToken, token).ConfigureAwait(false);
                if (current.Acknowledged) return;
            }
            throw new StoreVerificationException("Google Play acknowledge returned HTTP " + (int)response.Status + ApiError(response.Body) + ".");
        }

        /// <inheritdoc />
        public async Task<GooglePlayNotification> DecodeNotificationAsync(string body, string? authorizationHeader, CancellationToken token = default)
        {
            string packageName = RequirePackageName();
            string audience = (_Settings.PubSubAudience ?? String.Empty).Trim();
            string serviceAccountEmail = (_Settings.PubSubServiceAccountEmail ?? String.Empty).Trim();
            if (audience.Length < 1) throw new IntegrationNotConfiguredException("Google Play notifications are not configured: set GooglePlay PubSubAudience.");
            if (serviceAccountEmail.Length < 1) throw new IntegrationNotConfiguredException("Google Play notifications are not configured: set GooglePlay PubSubServiceAccountEmail.");

            await VerifyPushTokenAsync(authorizationHeader, audience, serviceAccountEmail, token).ConfigureAwait(false);

            if (String.IsNullOrWhiteSpace(body)) throw new StoreVerificationException("Google Play notification rejected: the body is empty.");
            try
            {
                using (JsonDocument envelope = JsonDocument.Parse(body))
                {
                    if (!JsonClaims.TryGetObject(envelope.RootElement, "message", out JsonElement message))
                    {
                        throw new StoreVerificationException("Google Play notification rejected: the body has no message.");
                    }
                    string? messageId = JsonClaims.GetString(message, "messageId") ?? JsonClaims.GetString(message, "message_id");
                    string? data = JsonClaims.GetString(message, "data");
                    if (String.IsNullOrEmpty(messageId)) throw new StoreVerificationException("Google Play notification rejected: the message has no messageId.");
                    if (String.IsNullOrEmpty(data)) throw new StoreVerificationException("Google Play notification rejected: the message has no data.");

                    string json;
                    try
                    {
                        json = Encoding.UTF8.GetString(Convert.FromBase64String(data));
                    }
                    catch (FormatException e)
                    {
                        throw new StoreVerificationException("Google Play notification rejected: message.data is not valid base64.", e);
                    }

                    using (JsonDocument notification = JsonDocument.Parse(json))
                    {
                        JsonElement root = notification.RootElement;
                        string? notificationPackage = JsonClaims.GetString(root, "packageName");
                        if (!String.Equals(notificationPackage, packageName, StringComparison.Ordinal))
                        {
                            throw new StoreVerificationException("Google Play notification rejected: the package name does not match this app.");
                        }

                        GooglePlayNotification result = new GooglePlayNotification
                        {
                            MessageId = messageId,
                            PackageName = packageName,
                            RawPayloadJson = json
                        };
                        if (JsonClaims.TryGetObject(root, "subscriptionNotification", out JsonElement subscription))
                        {
                            result.PurchaseToken = JsonClaims.GetString(subscription, "purchaseToken");
                            result.SubscriptionId = JsonClaims.GetString(subscription, "subscriptionId");
                            long? type = JsonClaims.GetInt64(subscription, "notificationType");
                            result.NotificationType = type != null && type.Value >= Int32.MinValue && type.Value <= Int32.MaxValue ? (int)type.Value : 0;
                        }
                        else if (JsonClaims.TryGetObject(root, "testNotification", out JsonElement _))
                        {
                            result.IsTest = true;
                        }
                        return result;
                    }
                }
            }
            catch (JsonException e)
            {
                throw new StoreVerificationException("Google Play notification rejected: the body or developer notification is not valid JSON.", e);
            }
        }

        #endregion

        #region Private-Methods

        private string RequirePackageName()
        {
            string packageName = (_Settings.PackageName ?? String.Empty).Trim();
            if (packageName.Length < 1) throw new IntegrationNotConfiguredException("Google Play is not configured: set GooglePlay PackageName.");
            return packageName;
        }

        private void RequireServiceAccount()
        {
            if (String.IsNullOrWhiteSpace(_Settings.ServiceAccountJson)) throw new IntegrationNotConfiguredException("Google Play is not configured: set GooglePlay ServiceAccountJson.");
        }

        private async Task VerifyPushTokenAsync(string? authorizationHeader, string audience, string serviceAccountEmail, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(authorizationHeader)) throw new WebhookSignatureException("Google Play notification rejected: the Authorization header is missing.");
            string header = authorizationHeader.Trim();
            if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) throw new WebhookSignatureException("Google Play notification rejected: the Authorization header is not a bearer token.");
            string jwtText = header.Substring(7).Trim();

            JwtValidationOptions options = new JwtValidationOptions
            {
                ValidIssuers = PushTokenIssuers,
                ValidAudiences = new[] { audience },
                ClockSkew = TimeSpan.FromMinutes(5)
            };

            JwtToken jwt;
            try
            {
                jwt = await JwtValidator.ValidateRs256Async(jwtText, _Jwks, options, token).ConfigureAwait(false);
            }
            catch (JwtValidationException e)
            {
                throw new WebhookSignatureException("Google Play notification rejected: push token " + LowerFirst(e.Message), e);
            }

            string? email = JsonClaims.GetString(jwt.Payload, "email");
            if (!String.Equals(email, serviceAccountEmail, StringComparison.OrdinalIgnoreCase))
            {
                throw new WebhookSignatureException("Google Play notification rejected: the push token was not issued to the configured service account.");
            }
            if (JsonClaims.GetBoolean(jwt.Payload, "email_verified") != true)
            {
                throw new WebhookSignatureException("Google Play notification rejected: the push token email is not verified.");
            }
        }

        private static GooglePlaySubscription MapSubscription(JsonElement root, string purchaseToken, string rawJson)
        {
            GooglePlaySubscription subscription = new GooglePlaySubscription
            {
                PurchaseToken = purchaseToken,
                LatestOrderId = JsonClaims.GetString(root, "latestOrderId"),
                State = MapState(JsonClaims.GetString(root, "subscriptionState")),
                StartUtc = JsonClaims.GetRfc3339(root, "startTime"),
                Acknowledged = String.Equals(JsonClaims.GetString(root, "acknowledgementState"), "ACKNOWLEDGEMENT_STATE_ACKNOWLEDGED", StringComparison.Ordinal),
                LinkedPurchaseToken = JsonClaims.GetString(root, "linkedPurchaseToken"),
                Environment = JsonClaims.TryGetObject(root, "testPurchase", out JsonElement _) ? StoreEnvironmentEnum.Sandbox : StoreEnvironmentEnum.Production,
                RawPayloadJson = rawJson
            };

            if (JsonClaims.TryGetObject(root, "externalAccountIdentifiers", out JsonElement external))
            {
                subscription.ObfuscatedAccountId = JsonClaims.GetString(external, "obfuscatedExternalAccountId");
            }

            if (JsonClaims.TryGetArray(root, "lineItems", out JsonElement lineItems) && lineItems.GetArrayLength() > 0)
            {
                JsonElement first = lineItems[0];
                subscription.ProductId = JsonClaims.GetString(first, "productId") ?? String.Empty;
                subscription.ExpiryUtc = JsonClaims.GetRfc3339(first, "expiryTime");
                if (JsonClaims.TryGetObject(first, "autoRenewingPlan", out JsonElement plan))
                {
                    subscription.AutoRenewing = JsonClaims.GetBoolean(plan, "autoRenewEnabled") == true;
                }
                if (subscription.LatestOrderId == null) subscription.LatestOrderId = JsonClaims.GetString(first, "latestSuccessfulOrderId");
            }
            return subscription;
        }

        private static GooglePlaySubscriptionStateEnum MapState(string? state)
        {
            switch (state)
            {
                case "SUBSCRIPTION_STATE_PENDING": return GooglePlaySubscriptionStateEnum.Pending;
                case "SUBSCRIPTION_STATE_ACTIVE": return GooglePlaySubscriptionStateEnum.Active;
                case "SUBSCRIPTION_STATE_PAUSED": return GooglePlaySubscriptionStateEnum.Paused;
                case "SUBSCRIPTION_STATE_IN_GRACE_PERIOD": return GooglePlaySubscriptionStateEnum.InGracePeriod;
                case "SUBSCRIPTION_STATE_ON_HOLD": return GooglePlaySubscriptionStateEnum.OnHold;
                case "SUBSCRIPTION_STATE_CANCELED": return GooglePlaySubscriptionStateEnum.Canceled;
                case "SUBSCRIPTION_STATE_EXPIRED": return GooglePlaySubscriptionStateEnum.Expired;
                case "SUBSCRIPTION_STATE_PENDING_PURCHASE_CANCELED": return GooglePlaySubscriptionStateEnum.PendingPurchaseCanceled;
                default: return GooglePlaySubscriptionStateEnum.Unspecified;
            }
        }

        private async Task<ApiResponse> SendApiAsync(HttpMethod method, string url, string? jsonBody, CancellationToken token)
        {
            string accessToken = await GetAccessTokenAsync(token).ConfigureAwait(false);
            try
            {
                using (HttpRequestMessage request = new HttpRequestMessage(method, url))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    if (jsonBody != null) request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                    using (HttpResponseMessage response = await _HttpClient.SendAsync(request, token).ConfigureAwait(false))
                    {
                        string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                        if (response.StatusCode == HttpStatusCode.Unauthorized) InvalidateAccessToken(accessToken);
                        return new ApiResponse(response.StatusCode, body);
                    }
                }
            }
            catch (HttpRequestException e)
            {
                throw new StoreVerificationException("Google Play API request failed.", e);
            }
            catch (TaskCanceledException e) when (!token.IsCancellationRequested)
            {
                throw new StoreVerificationException("Google Play API request timed out.", e);
            }
        }

        private async Task<string> GetAccessTokenAsync(CancellationToken token)
        {
            string json = _Settings.ServiceAccountJson ?? String.Empty;
            string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

            await _TokenLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_AccessToken != null
                    && String.Equals(_AccessTokenAccountFingerprint, fingerprint, StringComparison.Ordinal)
                    && DateTime.UtcNow + _AccessTokenRefreshMargin < _AccessTokenExpiresUtc)
                {
                    return _AccessToken;
                }

                ServiceAccount account = ParseServiceAccount(json);
                string assertion = CreateAssertion(account);
                List<KeyValuePair<string, string>> form = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer"),
                    new KeyValuePair<string, string>("assertion", assertion)
                };

                string body;
                HttpStatusCode status;
                try
                {
                    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, _TokenUrl))
                    {
                        request.Content = new FormUrlEncodedContent(form);
                        using (HttpResponseMessage response = await _HttpClient.SendAsync(request, token).ConfigureAwait(false))
                        {
                            status = response.StatusCode;
                            body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                        }
                    }
                }
                catch (HttpRequestException e)
                {
                    throw new StoreVerificationException("Google OAuth token request failed.", e);
                }
                catch (TaskCanceledException e) when (!token.IsCancellationRequested)
                {
                    throw new StoreVerificationException("Google OAuth token request timed out.", e);
                }

                if (!IsSuccess(status)) throw new StoreVerificationException("Google OAuth token request returned HTTP " + (int)status + OAuthError(body) + ".");

                try
                {
                    using (JsonDocument document = JsonDocument.Parse(body))
                    {
                        string? accessToken = JsonClaims.GetString(document.RootElement, "access_token");
                        long expiresIn = JsonClaims.GetInt64(document.RootElement, "expires_in") ?? 3600;
                        if (String.IsNullOrEmpty(accessToken)) throw new StoreVerificationException("Google OAuth token response has no access_token.");
                        _AccessToken = accessToken;
                        _AccessTokenExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(0, expiresIn));
                        _AccessTokenAccountFingerprint = fingerprint;
                        return accessToken;
                    }
                }
                catch (JsonException e)
                {
                    throw new StoreVerificationException("Google OAuth token response is not valid JSON.", e);
                }
            }
            finally
            {
                _TokenLock.Release();
            }
        }

        private void InvalidateAccessToken(string rejected)
        {
            if (!_TokenLock.Wait(0)) return;
            try
            {
                if (String.Equals(_AccessToken, rejected, StringComparison.Ordinal)) _AccessToken = null;
            }
            finally
            {
                _TokenLock.Release();
            }
        }

        private static ServiceAccount ParseServiceAccount(string json)
        {
            try
            {
                using (JsonDocument document = JsonDocument.Parse(json))
                {
                    string? email = JsonClaims.GetString(document.RootElement, "client_email");
                    string? privateKey = JsonClaims.GetString(document.RootElement, "private_key");
                    string? keyId = JsonClaims.GetString(document.RootElement, "private_key_id");
                    if (String.IsNullOrEmpty(email)) throw new IntegrationNotConfiguredException("GooglePlay ServiceAccountJson has no client_email.");
                    if (String.IsNullOrEmpty(privateKey)) throw new IntegrationNotConfiguredException("GooglePlay ServiceAccountJson has no private_key.");
                    return new ServiceAccount(email, privateKey, keyId);
                }
            }
            catch (JsonException e)
            {
                throw new IntegrationNotConfiguredException("GooglePlay ServiceAccountJson is not valid JSON.", e);
            }
        }

        private string CreateAssertion(ServiceAccount account)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            JsonObject header = new JsonObject { ["typ"] = "JWT" };
            if (!String.IsNullOrEmpty(account.KeyId)) header["kid"] = account.KeyId;
            JsonObject payload = new JsonObject
            {
                ["iss"] = account.Email,
                ["scope"] = Scope,
                ["aud"] = _TokenUrl,
                ["iat"] = now,
                ["exp"] = now + 3600
            };

            using (RSA rsa = RSA.Create())
            {
                try
                {
                    rsa.ImportFromPem(account.PrivateKeyPem);
                }
                catch (Exception e) when (e is ArgumentException || e is CryptographicException)
                {
                    throw new IntegrationNotConfiguredException("GooglePlay ServiceAccountJson private_key could not be loaded as an RSA private key.", e);
                }
                return JwtSigner.SignRs256(header, payload, rsa);
            }
        }

        private static bool IsSuccess(HttpStatusCode status)
        {
            return (int)status >= 200 && (int)status <= 299;
        }

        private static string ApiError(string body)
        {
            try
            {
                using (JsonDocument document = JsonDocument.Parse(body))
                {
                    if (JsonClaims.TryGetObject(document.RootElement, "error", out JsonElement error))
                    {
                        string? status = JsonClaims.GetString(error, "status");
                        string? message = JsonClaims.GetString(error, "message");
                        return " (" + (status ?? "error") + (message != null ? ": " + message : String.Empty) + ")";
                    }
                }
            }
            catch (JsonException)
            {
            }
            return String.Empty;
        }

        private static string OAuthError(string body)
        {
            try
            {
                using (JsonDocument document = JsonDocument.Parse(body))
                {
                    string? error = JsonClaims.GetString(document.RootElement, "error");
                    if (error != null) return " (" + error + ")";
                }
            }
            catch (JsonException)
            {
            }
            return String.Empty;
        }

        private static string LowerFirst(string value)
        {
            if (String.IsNullOrEmpty(value)) return value;
            return Char.ToLowerInvariant(value[0]) + value.Substring(1);
        }

        #endregion
    }
}
