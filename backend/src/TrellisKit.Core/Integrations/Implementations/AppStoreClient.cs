namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
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
    /// App Store transaction and notification verification and App Store Server API client.
    /// Every JWS (signed transaction, signed renewal info, notification payload) must carry an x5c chain that builds to
    /// the trusted root (the embedded Apple Root CA G3 unless overridden), with the Apple marker extensions on the leaf
    /// and intermediate, and an ES256 signature from the leaf key. Decoded objects must name the configured bundle id
    /// and an allowed environment (Sandbox only when <see cref="AppStoreSettings.AllowSandbox"/> is true).
    /// Settings are read on every call. Thread safe.
    /// </summary>
    public class AppStoreClient : IAppStoreClient
    {
        #region Public-Members

        /// <summary>
        /// Apple Root CA - G3 (DER, base64), from https://www.apple.com/certificateauthority/AppleRootCA-G3.cer.
        /// Valid until 2039-04-30. SHA-256 fingerprint is <see cref="AppleRootCaG3Sha256Fingerprint"/>.
        /// </summary>
        public const string AppleRootCaG3Base64 = "MIICQzCCAcmgAwIBAgIILcX8iNLFS5UwCgYIKoZIzj0EAwMwZzEbMBkGA1UEAwwSQXBwbGUgUm9vdCBDQSAtIEczMSYwJAYDVQQLDB1BcHBsZSBDZXJ0aWZpY2F0aW9uIEF1dGhvcml0eTETMBEGA1UECgwKQXBwbGUgSW5jLjELMAkGA1UEBhMCVVMwHhcNMTQwNDMwMTgxOTA2WhcNMzkwNDMwMTgxOTA2WjBnMRswGQYDVQQDDBJBcHBsZSBSb290IENBIC0gRzMxJjAkBgNVBAsMHUFwcGxlIENlcnRpZmljYXRpb24gQXV0aG9yaXR5MRMwEQYDVQQKDApBcHBsZSBJbmMuMQswCQYDVQQGEwJVUzB2MBAGByqGSM49AgEGBSuBBAAiA2IABJjpLz1AcqTtkyJygRMc3RCV8cWjTnHcFBbZDuWmBSp3ZHtfTjjTuxxEtX/1H7YyYl3J6YRbTzBPEVoA/VhYDKX1DyxNB0cTddqXl5dvMVztK517IDvYuVTZXpmkOlEKMaNCMEAwHQYDVR0OBBYEFLuw3qFYM4iapIqZ3r6966/ayySrMA8GA1UdEwEB/wQFMAMBAf8wDgYDVR0PAQH/BAQDAgEGMAoGCCqGSM49BAMDA2gAMGUCMQCD6cHEFl4aXTQY2e3v9GwOAEZLuN+yRhHFD/3meoyhpmvOwgPUnPWTxnS4at+qIxUCMG1mihDK1A3UT82NQz60imOlM27jbdoXt2QfyFMm+YhidDkLF1vLUagM6BgD56KyKA==";

        /// <summary>
        /// Published SHA-256 fingerprint of Apple Root CA - G3 (uppercase hex, colon separated).
        /// </summary>
        public const string AppleRootCaG3Sha256Fingerprint = "63:34:3A:BF:B8:9A:6A:03:EB:B5:7E:9B:3F:5F:A7:BE:7C:4F:5C:75:6F:30:17:B3:A8:C4:88:C3:65:3E:91:79";

        /// <summary>
        /// Audience of App Store Server API tokens.
        /// </summary>
        public const string ApiAudience = "appstoreconnect-v1";

        /// <summary>
        /// Lifetime of App Store Server API tokens. Default 5 minutes. Minimum 1 minute, maximum 60 minutes (Apple's limit).
        /// </summary>
        public TimeSpan ApiTokenLifetime
        {
            get { return _ApiTokenLifetime; }
            set
            {
                if (value < TimeSpan.FromMinutes(1)) value = TimeSpan.FromMinutes(1);
                if (value > TimeSpan.FromMinutes(60)) value = TimeSpan.FromMinutes(60);
                _ApiTokenLifetime = value;
            }
        }

        /// <inheritdoc />
        public bool IsConfigured
        {
            get { return !String.IsNullOrWhiteSpace(_Settings.BundleId); }
        }

        #endregion

        #region Private-Members

        private static readonly Lazy<X509Certificate2> _EmbeddedRoot = new Lazy<X509Certificate2>(
            () => X509CertificateLoader.LoadCertificate(Convert.FromBase64String(AppleRootCaG3Base64)),
            LazyThreadSafetyMode.ExecutionAndPublication);

        private readonly AppStoreSettings _Settings;
        private readonly HttpClient _HttpClient;
        private readonly X509Certificate2? _RootCertificate;
        private readonly string _ProductionBaseUrl;
        private readonly string _SandboxBaseUrl;
        private TimeSpan _ApiTokenLifetime = TimeSpan.FromMinutes(5);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the client.
        /// </summary>
        /// <param name="settings">App Store settings (live instance).</param>
        /// <param name="httpClient">HTTP client.</param>
        /// <param name="rootCertificate">Trusted root certificate; null uses the embedded Apple Root CA G3 (override for tests).</param>
        /// <param name="productionBaseUrl">App Store Server API production base URL.</param>
        /// <param name="sandboxBaseUrl">App Store Server API sandbox base URL.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public AppStoreClient(AppStoreSettings settings, HttpClient httpClient, X509Certificate2? rootCertificate = null, string productionBaseUrl = "https://api.storekit.itunes.apple.com", string sandboxBaseUrl = "https://api.storekit-sandbox.itunes.apple.com")
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _RootCertificate = rootCertificate;
            _ProductionBaseUrl = productionBaseUrl ?? throw new ArgumentNullException(nameof(productionBaseUrl));
            _SandboxBaseUrl = sandboxBaseUrl ?? throw new ArgumentNullException(nameof(sandboxBaseUrl));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Load the embedded Apple Root CA G3 certificate. The caller owns and disposes the returned instance.
        /// </summary>
        /// <returns>Certificate.</returns>
        public static X509Certificate2 LoadEmbeddedAppleRoot()
        {
            return X509CertificateLoader.LoadCertificate(Convert.FromBase64String(AppleRootCaG3Base64));
        }

        /// <inheritdoc />
        public Task<AppStoreTransaction> VerifyTransactionAsync(string signedTransaction, CancellationToken token = default)
        {
            string bundleId = RequireBundleId();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(VerifyTransaction(signedTransaction, bundleId, "signed transaction"));
        }

        /// <inheritdoc />
        public Task<AppStoreNotification> DecodeNotificationAsync(string signedPayload, CancellationToken token = default)
        {
            string bundleId = RequireBundleId();
            token.ThrowIfCancellationRequested();

            JwtToken jws = VerifyJws(signedPayload, "notification");
            JsonElement payload = jws.Payload;

            string? notificationType = JsonClaims.GetString(payload, "notificationType");
            string? notificationUuid = JsonClaims.GetString(payload, "notificationUUID");
            if (String.IsNullOrEmpty(notificationType) || String.IsNullOrEmpty(notificationUuid))
            {
                throw new StoreVerificationException("App Store notification rejected: notificationType or notificationUUID is missing.");
            }

            JsonElement data;
            if (!JsonClaims.TryGetObject(payload, "data", out data)
                && !JsonClaims.TryGetObject(payload, "summary", out data)
                && !JsonClaims.TryGetObject(payload, "externalPurchaseToken", out data))
            {
                throw new StoreVerificationException("App Store notification rejected: the payload has no data.");
            }

            string? payloadBundleId = JsonClaims.GetString(data, "bundleId");
            if (!String.Equals(payloadBundleId, bundleId, StringComparison.Ordinal))
            {
                throw new StoreVerificationException("App Store notification rejected: the bundle id does not match this app.");
            }
            StoreEnvironmentEnum environment = ParseEnvironment(JsonClaims.GetString(data, "environment"), "notification");

            long expectedAppleId = _Settings.AppAppleId;
            long? payloadAppleId = JsonClaims.GetInt64(data, "appAppleId");
            if (environment == StoreEnvironmentEnum.Production && expectedAppleId != 0 && payloadAppleId != null && payloadAppleId.Value != expectedAppleId)
            {
                throw new StoreVerificationException("App Store notification rejected: the app Apple ID does not match this app.");
            }

            AppStoreTransaction? transaction = null;
            string? signedTransactionInfo = JsonClaims.GetString(data, "signedTransactionInfo");
            if (!String.IsNullOrEmpty(signedTransactionInfo))
            {
                transaction = VerifyTransaction(signedTransactionInfo, bundleId, "notification transaction");
                if (transaction.Environment != environment)
                {
                    throw new StoreVerificationException("App Store notification rejected: the transaction environment differs from the notification environment.");
                }
            }

            bool? autoRenew = null;
            string? signedRenewalInfo = JsonClaims.GetString(data, "signedRenewalInfo");
            if (!String.IsNullOrEmpty(signedRenewalInfo))
            {
                JwtToken renewal = VerifyJws(signedRenewalInfo, "renewal info");
                string? renewalEnvironment = JsonClaims.GetString(renewal.Payload, "environment");
                if (renewalEnvironment != null && ParseEnvironment(renewalEnvironment, "renewal info") != environment)
                {
                    throw new StoreVerificationException("App Store notification rejected: the renewal info environment differs from the notification environment.");
                }
                long? status = JsonClaims.GetInt64(renewal.Payload, "autoRenewStatus");
                if (status != null) autoRenew = status.Value == 1;
            }

            return Task.FromResult(new AppStoreNotification
            {
                NotificationUuid = notificationUuid,
                NotificationType = notificationType,
                Subtype = JsonClaims.GetString(payload, "subtype"),
                Environment = environment,
                Transaction = transaction,
                AutoRenewEnabled = autoRenew,
                RawPayloadJson = jws.PayloadJson
            });
        }

        /// <inheritdoc />
        public async Task<AppStoreTransaction?> GetLatestTransactionAsync(string originalTransactionId, StoreEnvironmentEnum environment, CancellationToken token = default)
        {
            string bundleId = RequireBundleId();
            if (String.IsNullOrWhiteSpace(originalTransactionId)) throw new StoreVerificationException("The original transaction id is empty.");
            if (environment == StoreEnvironmentEnum.Sandbox && !_Settings.AllowSandbox)
            {
                throw new StoreVerificationException("App Store sandbox transactions are not accepted (AllowSandbox is false).");
            }

            string bearer = CreateApiToken(bundleId);
            string baseUrl = (environment == StoreEnvironmentEnum.Sandbox ? _SandboxBaseUrl : _ProductionBaseUrl).TrimEnd('/');
            string url = baseUrl + "/inApps/v1/subscriptions/" + Uri.EscapeDataString(originalTransactionId.Trim());

            string body;
            HttpStatusCode status;
            try
            {
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    using (HttpResponseMessage response = await _HttpClient.SendAsync(request, token).ConfigureAwait(false))
                    {
                        status = response.StatusCode;
                        body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    }
                }
            }
            catch (HttpRequestException e)
            {
                throw new StoreVerificationException("App Store Server API request failed.", e);
            }
            catch (TaskCanceledException e) when (!token.IsCancellationRequested)
            {
                throw new StoreVerificationException("App Store Server API request timed out.", e);
            }

            if (status == HttpStatusCode.NotFound) return null;
            if (status == HttpStatusCode.Unauthorized) throw new StoreVerificationException("App Store Server API rejected the credentials (HTTP 401). Check IssuerId, KeyId, and PrivateKeyPem.");
            if ((int)status < 200 || (int)status > 299) throw new StoreVerificationException("App Store Server API returned HTTP " + (int)status + ApiError(body) + ".");

            List<AppStoreTransaction> transactions = new List<AppStoreTransaction>();
            try
            {
                using (JsonDocument document = JsonDocument.Parse(body))
                {
                    if (JsonClaims.TryGetArray(document.RootElement, "data", out JsonElement groups))
                    {
                        foreach (JsonElement group in groups.EnumerateArray())
                        {
                            if (!JsonClaims.TryGetArray(group, "lastTransactions", out JsonElement lastTransactions)) continue;
                            foreach (JsonElement item in lastTransactions.EnumerateArray())
                            {
                                string? signed = JsonClaims.GetString(item, "signedTransactionInfo");
                                if (String.IsNullOrEmpty(signed)) continue;
                                transactions.Add(VerifyTransaction(signed, bundleId, "API transaction"));
                            }
                        }
                    }
                }
            }
            catch (JsonException e)
            {
                throw new StoreVerificationException("App Store Server API returned invalid JSON.", e);
            }

            if (transactions.Count < 1) return null;
            List<AppStoreTransaction> matching = transactions.Where(t => String.Equals(t.OriginalTransactionId, originalTransactionId.Trim(), StringComparison.Ordinal)).ToList();
            List<AppStoreTransaction> candidates = matching.Count > 0 ? matching : transactions;
            return candidates.OrderByDescending(t => t.ExpiresUtc ?? t.PurchaseUtc).ThenByDescending(t => t.PurchaseUtc).FirstOrDefault();
        }

        #endregion

        #region Private-Methods

        private string RequireBundleId()
        {
            string bundleId = (_Settings.BundleId ?? String.Empty).Trim();
            if (bundleId.Length < 1) throw new IntegrationNotConfiguredException("App Store is not configured: set AppStore BundleId.");
            return bundleId;
        }

        private X509Certificate2 TrustedRoot()
        {
            return _RootCertificate ?? _EmbeddedRoot.Value;
        }

        private JwtToken VerifyJws(string? jws, string what)
        {
            try
            {
                return AppleJwsVerifier.Verify(jws, TrustedRoot(), DateTime.UtcNow);
            }
            catch (JwtValidationException e)
            {
                throw new StoreVerificationException("App Store " + what + " rejected: " + e.Message, e);
            }
        }

        private AppStoreTransaction VerifyTransaction(string? jws, string bundleId, string what)
        {
            JwtToken token = VerifyJws(jws, what);
            JsonElement payload = token.Payload;

            string? transactionId = JsonClaims.GetString(payload, "transactionId");
            string? originalTransactionId = JsonClaims.GetString(payload, "originalTransactionId");
            string? productId = JsonClaims.GetString(payload, "productId");
            string? payloadBundleId = JsonClaims.GetString(payload, "bundleId");
            if (String.IsNullOrEmpty(transactionId) || String.IsNullOrEmpty(originalTransactionId) || String.IsNullOrEmpty(productId))
            {
                throw new StoreVerificationException("App Store " + what + " rejected: transactionId, originalTransactionId, or productId is missing.");
            }
            if (!String.Equals(payloadBundleId, bundleId, StringComparison.Ordinal))
            {
                throw new StoreVerificationException("App Store " + what + " rejected: the bundle id does not match this app.");
            }

            StoreEnvironmentEnum environment = ParseEnvironment(JsonClaims.GetString(payload, "environment"), what);
            DateTime? purchase = JsonClaims.GetUnixMilliseconds(payload, "purchaseDate");
            if (purchase == null) throw new StoreVerificationException("App Store " + what + " rejected: purchaseDate is missing.");

            Guid? appAccountToken = null;
            string? accountToken = JsonClaims.GetString(payload, "appAccountToken");
            if (!String.IsNullOrEmpty(accountToken) && Guid.TryParse(accountToken, out Guid parsed)) appAccountToken = parsed;

            return new AppStoreTransaction
            {
                TransactionId = transactionId,
                OriginalTransactionId = originalTransactionId,
                ProductId = productId,
                BundleId = bundleId,
                PurchaseUtc = purchase.Value,
                ExpiresUtc = JsonClaims.GetUnixMilliseconds(payload, "expiresDate"),
                RevocationUtc = JsonClaims.GetUnixMilliseconds(payload, "revocationDate"),
                AppAccountToken = appAccountToken,
                Environment = environment,
                RawPayloadJson = token.PayloadJson
            };
        }

        private StoreEnvironmentEnum ParseEnvironment(string? value, string what)
        {
            if (String.Equals(value, "Production", StringComparison.Ordinal)) return StoreEnvironmentEnum.Production;
            if (String.Equals(value, "Sandbox", StringComparison.Ordinal))
            {
                if (!_Settings.AllowSandbox) throw new StoreVerificationException("App Store " + what + " rejected: sandbox transactions are not accepted (AllowSandbox is false).");
                return StoreEnvironmentEnum.Sandbox;
            }
            throw new StoreVerificationException("App Store " + what + " rejected: unsupported environment.");
        }

        private string CreateApiToken(string bundleId)
        {
            string issuerId = (_Settings.IssuerId ?? String.Empty).Trim();
            string keyId = (_Settings.KeyId ?? String.Empty).Trim();
            string privateKeyPem = _Settings.PrivateKeyPem ?? String.Empty;
            if (issuerId.Length < 1) throw new IntegrationNotConfiguredException("App Store Server API is not configured: set AppStore IssuerId.");
            if (keyId.Length < 1) throw new IntegrationNotConfiguredException("App Store Server API is not configured: set AppStore KeyId.");
            if (String.IsNullOrWhiteSpace(privateKeyPem)) throw new IntegrationNotConfiguredException("App Store Server API is not configured: set AppStore PrivateKeyPem.");

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            JsonObject header = new JsonObject { ["kid"] = keyId, ["typ"] = "JWT" };
            JsonObject payload = new JsonObject
            {
                ["iss"] = issuerId,
                ["iat"] = now,
                ["exp"] = now + (long)_ApiTokenLifetime.TotalSeconds,
                ["aud"] = ApiAudience,
                ["bid"] = bundleId
            };

            using (ECDsa key = ECDsa.Create())
            {
                try
                {
                    key.ImportFromPem(privateKeyPem);
                }
                catch (Exception e) when (e is ArgumentException || e is CryptographicException)
                {
                    throw new IntegrationNotConfiguredException("App Store PrivateKeyPem could not be loaded as an EC private key (.p8).", e);
                }
                return JwtSigner.SignEs256(header, payload, key);
            }
        }

        private static string ApiError(string body)
        {
            try
            {
                using (JsonDocument document = JsonDocument.Parse(body))
                {
                    string? code = JsonClaims.GetString(document.RootElement, "errorCode");
                    string? message = JsonClaims.GetString(document.RootElement, "errorMessage");
                    if (code != null || message != null) return " (" + (code ?? "error") + (message != null ? ": " + message : String.Empty) + ")";
                }
            }
            catch (JsonException)
            {
            }
            return String.Empty;
        }

        #endregion
    }
}
