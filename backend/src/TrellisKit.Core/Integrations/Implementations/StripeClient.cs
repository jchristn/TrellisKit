namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Stripe REST client: form-encoded requests authenticated with the secret key, a pinned Stripe-Version, and an
    /// idempotency key on every POST. Webhooks are verified with HMAC-SHA256 over "t.payload" using the webhook secret,
    /// compared in constant time against every v1 signature, with a 300 second tolerance.
    /// Settings are read on every call. Thread safe.
    /// </summary>
    public class StripeClient : IStripeClient
    {
        #region Public-Members

        /// <summary>
        /// Stripe-Version sent with every request. Default 2025-09-30.clover. Changing it changes response shapes;
        /// current_period_start and current_period_end are read from the subscription or its first item, so versions
        /// before and after 2025-03-31.basil are both handled.
        /// </summary>
        public string ApiVersion
        {
            get { return _ApiVersion; }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentException("The Stripe API version must not be empty.", nameof(value));
                _ApiVersion = value.Trim();
            }
        }

        /// <summary>
        /// Webhook timestamp tolerance. Default 300 seconds. Minimum 1 second, maximum 1 hour.
        /// </summary>
        public TimeSpan WebhookTolerance
        {
            get { return _WebhookTolerance; }
            set
            {
                if (value < TimeSpan.FromSeconds(1)) value = TimeSpan.FromSeconds(1);
                if (value > TimeSpan.FromHours(1)) value = TimeSpan.FromHours(1);
                _WebhookTolerance = value;
            }
        }

        /// <inheritdoc />
        public bool IsConfigured
        {
            get { return !String.IsNullOrWhiteSpace(_Settings.SecretKey); }
        }

        #endregion

        #region Private-Members

        private static readonly Regex _KeyPattern = new Regex(@"\b(sk|rk|pk)_(live|test)_[A-Za-z0-9*]+|\bwhsec_[A-Za-z0-9*]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly StripeSettings _Settings;
        private readonly HttpClient _HttpClient;
        private string _ApiVersion = "2025-09-30.clover";
        private TimeSpan _WebhookTolerance = TimeSpan.FromSeconds(300);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the client.
        /// </summary>
        /// <param name="settings">Stripe settings (live instance).</param>
        /// <param name="httpClient">HTTP client.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public StripeClient(StripeSettings settings, HttpClient httpClient)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException">The request is null.</exception>
        /// <exception cref="ArgumentException">The user id, success URL, or cancel URL is empty.</exception>
        public async Task<StripeCheckoutSession> CreateCheckoutSessionAsync(StripeCheckoutRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            RequireSecretKey();
            if (String.IsNullOrWhiteSpace(request.UserId)) throw new ArgumentException("The checkout request has no user id.", nameof(request));
            if (String.IsNullOrWhiteSpace(request.SuccessUrl)) throw new ArgumentException("The checkout request has no success URL.", nameof(request));
            if (String.IsNullOrWhiteSpace(request.CancelUrl)) throw new ArgumentException("The checkout request has no cancel URL.", nameof(request));

            string priceId = !String.IsNullOrWhiteSpace(request.PriceId) ? request.PriceId.Trim() : (_Settings.PriceId ?? String.Empty).Trim();
            if (priceId.Length < 1) throw new IntegrationNotConfiguredException("Stripe is not configured: set Stripe PriceId.");

            List<KeyValuePair<string, string>> form = BuildCheckoutForm(request, priceId);
            using (JsonDocument document = await SendAsync(HttpMethod.Post, "/v1/checkout/sessions", form, "create checkout session", token).ConfigureAwait(false))
            {
                string? id = JsonClaims.GetString(document.RootElement, "id");
                string? url = JsonClaims.GetString(document.RootElement, "url");
                if (String.IsNullOrEmpty(id) || String.IsNullOrEmpty(url)) throw new StoreVerificationException("Stripe checkout session response has no id or url.");
                return new StripeCheckoutSession { Id = id, Url = url };
            }
        }

        /// <inheritdoc />
        public async Task<string> CreateCouponAsync(StripeCouponRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            RequireSecretKey();
            if ((request.PercentOff == null) == (request.AmountOffCents == null)) throw new ArgumentException("Set exactly one of PercentOff and AmountOffCents.", nameof(request));
            List<KeyValuePair<string, string>> form = BuildCouponForm(request);
            using (JsonDocument document = await SendAsync(HttpMethod.Post, "/v1/coupons", form, "create coupon", token).ConfigureAwait(false))
            {
                string? id = JsonClaims.GetString(document.RootElement, "id");
                if (String.IsNullOrEmpty(id)) throw new StoreVerificationException("Stripe coupon response has no id.");
                return id;
            }
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentException">The customer id or return URL is empty.</exception>
        public async Task<string> CreatePortalSessionAsync(string customerId, string returnUrl, CancellationToken token = default)
        {
            RequireSecretKey();
            if (String.IsNullOrWhiteSpace(customerId)) throw new ArgumentException("The customer id is empty.", nameof(customerId));
            if (String.IsNullOrWhiteSpace(returnUrl)) throw new ArgumentException("The return URL is empty.", nameof(returnUrl));

            List<KeyValuePair<string, string>> form = new List<KeyValuePair<string, string>>
            {
                Pair("customer", customerId.Trim()),
                Pair("return_url", returnUrl.Trim())
            };
            using (JsonDocument document = await SendAsync(HttpMethod.Post, "/v1/billing_portal/sessions", form, "create portal session", token).ConfigureAwait(false))
            {
                string? url = JsonClaims.GetString(document.RootElement, "url");
                if (String.IsNullOrEmpty(url)) throw new StoreVerificationException("Stripe portal session response has no url.");
                return url;
            }
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentException">The subscription id is empty.</exception>
        public async Task<StripeSubscription> GetSubscriptionAsync(string subscriptionId, CancellationToken token = default)
        {
            RequireSecretKey();
            if (String.IsNullOrWhiteSpace(subscriptionId)) throw new ArgumentException("The subscription id is empty.", nameof(subscriptionId));
            string path = "/v1/subscriptions/" + Uri.EscapeDataString(subscriptionId.Trim());
            using (JsonDocument document = await SendAsync(HttpMethod.Get, path, null, "get subscription", token).ConfigureAwait(false))
            {
                return MapSubscription(document.RootElement);
            }
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentException">The subscription id is empty.</exception>
        public async Task<StripeSubscription> CancelAtPeriodEndAsync(string subscriptionId, CancellationToken token = default)
        {
            RequireSecretKey();
            if (String.IsNullOrWhiteSpace(subscriptionId)) throw new ArgumentException("The subscription id is empty.", nameof(subscriptionId));
            string path = "/v1/subscriptions/" + Uri.EscapeDataString(subscriptionId.Trim());
            List<KeyValuePair<string, string>> form = new List<KeyValuePair<string, string>> { Pair("cancel_at_period_end", "true") };
            using (JsonDocument document = await SendAsync(HttpMethod.Post, path, form, "cancel subscription", token).ConfigureAwait(false))
            {
                return MapSubscription(document.RootElement);
            }
        }

        /// <inheritdoc />
        public StripeEvent ParseWebhook(string payload, string? signatureHeader, DateTime nowUtc)
        {
            string secret = _Settings.WebhookSecret ?? String.Empty;
            if (String.IsNullOrWhiteSpace(secret)) throw new IntegrationNotConfiguredException("Stripe webhooks are not configured: set Stripe WebhookSecret.");
            if (payload == null) throw new WebhookSignatureException("Stripe webhook rejected: the payload is missing.");
            if (String.IsNullOrWhiteSpace(signatureHeader)) throw new WebhookSignatureException("Stripe webhook rejected: the Stripe-Signature header is missing.");

            long? timestamp = null;
            List<byte[]> signatures = new List<byte[]>();
            foreach (string part in signatureHeader.Split(','))
            {
                int equals = part.IndexOf('=');
                if (equals < 1) continue;
                string key = part.Substring(0, equals).Trim();
                string value = part.Substring(equals + 1).Trim();
                if (key == "t")
                {
                    if (Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)) timestamp = parsed;
                }
                else if (key == "v1")
                {
                    byte[]? decoded = TryDecodeHex(value);
                    if (decoded != null) signatures.Add(decoded);
                }
            }
            if (timestamp == null) throw new WebhookSignatureException("Stripe webhook rejected: the signature header has no timestamp.");
            if (signatures.Count < 1) throw new WebhookSignatureException("Stripe webhook rejected: the signature header has no v1 signature.");

            byte[] expected;
            using (HMACSHA256 hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret.Trim())))
            {
                expected = hmac.ComputeHash(Encoding.UTF8.GetBytes(timestamp.Value.ToString(CultureInfo.InvariantCulture) + "." + payload));
            }
            bool matched = false;
            foreach (byte[] candidate in signatures)
            {
                if (CryptographicOperations.FixedTimeEquals(candidate, expected)) matched = true;
            }
            if (!matched) throw new WebhookSignatureException("Stripe webhook rejected: no signature matches the payload.");

            DateTime signedUtc;
            try
            {
                signedUtc = DateTimeOffset.FromUnixTimeSeconds(timestamp.Value).UtcDateTime;
            }
            catch (ArgumentOutOfRangeException e)
            {
                throw new WebhookSignatureException("Stripe webhook rejected: the timestamp is out of range.", e);
            }
            DateTime now = nowUtc.Kind == DateTimeKind.Local ? nowUtc.ToUniversalTime() : nowUtc;
            if ((now - signedUtc).Duration() > _WebhookTolerance) throw new WebhookSignatureException("Stripe webhook rejected: the timestamp is outside the tolerance.");

            try
            {
                using (JsonDocument document = JsonDocument.Parse(payload))
                {
                    return MapEvent(document.RootElement, payload);
                }
            }
            catch (JsonException e)
            {
                throw new WebhookSignatureException("Stripe webhook rejected: the signed payload is not valid JSON.", e);
            }
        }

        #endregion

        #region Private-Methods

        private string RequireSecretKey()
        {
            string key = (_Settings.SecretKey ?? String.Empty).Trim();
            if (key.Length < 1) throw new IntegrationNotConfiguredException("Stripe is not configured: set Stripe SecretKey.");
            return key;
        }

        /// <summary>
        /// Form fields of a coupon request.
        /// </summary>
        private static List<KeyValuePair<string, string>> BuildCouponForm(StripeCouponRequest request)
        {
            List<KeyValuePair<string, string>> form = new List<KeyValuePair<string, string>>
            {
                Pair("duration", request.Forever ? "forever" : "once")
            };
            if (request.PercentOff != null) form.Add(Pair("percent_off", request.PercentOff.Value.ToString(CultureInfo.InvariantCulture)));
            else
            {
                form.Add(Pair("amount_off", (request.AmountOffCents ?? 0).ToString(CultureInfo.InvariantCulture)));
                form.Add(Pair("currency", String.IsNullOrWhiteSpace(request.Currency) ? "usd" : request.Currency.Trim().ToLowerInvariant()));
            }
            string name = (request.Name ?? String.Empty).Trim();
            if (name.Length > 40) name = name.Substring(0, 40);
            if (name.Length > 0) form.Add(Pair("name", name));
            if (!String.IsNullOrWhiteSpace(request.SignupCodeId)) form.Add(Pair("metadata[signupCodeId]", request.SignupCodeId.Trim()));
            return form;
        }

        private static List<KeyValuePair<string, string>> BuildCheckoutForm(StripeCheckoutRequest request, string priceId)
        {
            string userId = request.UserId.Trim();
            List<KeyValuePair<string, string>> form = new List<KeyValuePair<string, string>>
            {
                Pair("mode", "subscription"),
                Pair("line_items[0][price]", priceId),
                Pair("line_items[0][quantity]", "1"),
                Pair("client_reference_id", userId),
                Pair("metadata[userId]", userId),
                Pair("subscription_data[metadata][userId]", userId),
                Pair("success_url", request.SuccessUrl.Trim()),
                Pair("cancel_url", request.CancelUrl.Trim()),
                Pair("automatic_tax[enabled]", "true")
            };
            // Stripe rejects allow_promotion_codes together with discounts.
            if (!String.IsNullOrWhiteSpace(request.CouponId)) form.Add(Pair("discounts[0][coupon]", request.CouponId.Trim()));
            else form.Add(Pair("allow_promotion_codes", "false"));
            if (!String.IsNullOrWhiteSpace(request.SignupCodeId))
            {
                form.Add(Pair("metadata[signupCodeId]", request.SignupCodeId.Trim()));
                form.Add(Pair("subscription_data[metadata][signupCodeId]", request.SignupCodeId.Trim()));
            }
            if (!String.IsNullOrWhiteSpace(request.CustomerId))
            {
                form.Add(Pair("customer", request.CustomerId.Trim()));
                // Automatic tax needs an address on the customer; let Checkout save the one the member enters.
                form.Add(Pair("customer_update[address]", "auto"));
                form.Add(Pair("customer_update[name]", "auto"));
            }
            else if (!String.IsNullOrWhiteSpace(request.Email))
            {
                form.Add(Pair("customer_email", request.Email.Trim()));
            }
            return form;
        }

        private async Task<JsonDocument> SendAsync(HttpMethod method, string path, List<KeyValuePair<string, string>>? form, string what, CancellationToken token)
        {
            string secretKey = RequireSecretKey();
            string url = (_Settings.ApiBaseUrl ?? "https://api.stripe.com").TrimEnd('/') + path;

            HttpStatusCode status;
            string body;
            try
            {
                using (HttpRequestMessage request = new HttpRequestMessage(method, url))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);
                    request.Headers.TryAddWithoutValidation("Stripe-Version", _ApiVersion);
                    if (method == HttpMethod.Post)
                    {
                        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
                        request.Content = new FormUrlEncodedContent(form ?? new List<KeyValuePair<string, string>>());
                    }
                    using (HttpResponseMessage response = await _HttpClient.SendAsync(request, token).ConfigureAwait(false))
                    {
                        status = response.StatusCode;
                        body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    }
                }
            }
            catch (HttpRequestException e)
            {
                throw new StoreVerificationException("Stripe " + what + " request failed.", e);
            }
            catch (TaskCanceledException e) when (!token.IsCancellationRequested)
            {
                throw new StoreVerificationException("Stripe " + what + " request timed out.", e);
            }

            if ((int)status < 200 || (int)status > 299)
            {
                throw new StoreVerificationException("Stripe " + what + " failed (HTTP " + (int)status + StripeError(body, secretKey) + ").");
            }
            try
            {
                return JsonDocument.Parse(body);
            }
            catch (JsonException e)
            {
                throw new StoreVerificationException("Stripe " + what + " returned invalid JSON.", e);
            }
        }

        private static string StripeError(string body, string secretKey)
        {
            try
            {
                using (JsonDocument document = JsonDocument.Parse(body))
                {
                    if (JsonClaims.TryGetObject(document.RootElement, "error", out JsonElement error))
                    {
                        string type = JsonClaims.GetString(error, "type") ?? "api_error";
                        string? code = JsonClaims.GetString(error, "code");
                        string? message = JsonClaims.GetString(error, "message");
                        string text = ", " + type + (code != null ? "/" + code : String.Empty);
                        if (!String.IsNullOrEmpty(message)) text += ": " + Redact(message, secretKey);
                        return text;
                    }
                }
            }
            catch (JsonException)
            {
            }
            return String.Empty;
        }

        private static string Redact(string message, string secretKey)
        {
            string result = message;
            if (secretKey.Length > 0) result = result.Replace(secretKey, "[redacted]", StringComparison.Ordinal);
            return _KeyPattern.Replace(result, "[redacted]");
        }

        private static StripeSubscription MapSubscription(JsonElement root)
        {
            StripeSubscription subscription = new StripeSubscription
            {
                Id = JsonClaims.GetString(root, "id") ?? String.Empty,
                CustomerId = JsonClaims.GetIdOrExpandedId(root, "customer") ?? String.Empty,
                Status = JsonClaims.GetString(root, "status") ?? String.Empty,
                CancelAtPeriodEnd = JsonClaims.GetBoolean(root, "cancel_at_period_end") == true,
                LiveMode = JsonClaims.GetBoolean(root, "livemode") == true,
                CurrentPeriodStartUtc = JsonClaims.GetUnixSeconds(root, "current_period_start"),
                CurrentPeriodEndUtc = JsonClaims.GetUnixSeconds(root, "current_period_end"),
                RawPayloadJson = root.GetRawText()
            };
            if (String.IsNullOrEmpty(subscription.Id)) throw new StoreVerificationException("Stripe subscription response has no id.");

            if (JsonClaims.TryGetObject(root, "metadata", out JsonElement metadata))
            {
                subscription.UserId = JsonClaims.GetString(metadata, "userId");
            }

            if (JsonClaims.TryGetObject(root, "items", out JsonElement items)
                && JsonClaims.TryGetArray(items, "data", out JsonElement data)
                && data.GetArrayLength() > 0)
            {
                JsonElement first = data[0];
                if (JsonClaims.TryGetObject(first, "price", out JsonElement price)) subscription.PriceId = JsonClaims.GetString(price, "id");
                if (subscription.CurrentPeriodStartUtc == null) subscription.CurrentPeriodStartUtc = JsonClaims.GetUnixSeconds(first, "current_period_start");
                if (subscription.CurrentPeriodEndUtc == null) subscription.CurrentPeriodEndUtc = JsonClaims.GetUnixSeconds(first, "current_period_end");
            }
            return subscription;
        }

        private static StripeEvent MapEvent(JsonElement root, string payload)
        {
            string? id = JsonClaims.GetString(root, "id");
            string? type = JsonClaims.GetString(root, "type");
            if (String.IsNullOrEmpty(id) || String.IsNullOrEmpty(type)) throw new WebhookSignatureException("Stripe webhook rejected: the event has no id or type.");

            StripeEvent result = new StripeEvent
            {
                Id = id,
                Type = type,
                CreatedUtc = JsonClaims.GetUnixSeconds(root, "created") ?? DateTime.MinValue,
                LiveMode = JsonClaims.GetBoolean(root, "livemode") == true,
                RawPayloadJson = payload
            };

            if (!JsonClaims.TryGetObject(root, "data", out JsonElement data) || !JsonClaims.TryGetObject(data, "object", out JsonElement obj)) return result;

            if (type.StartsWith("checkout.session.", StringComparison.Ordinal))
            {
                result.SubscriptionId = JsonClaims.GetIdOrExpandedId(obj, "subscription");
                result.CustomerId = JsonClaims.GetIdOrExpandedId(obj, "customer");
                result.ClientReferenceId = JsonClaims.GetString(obj, "client_reference_id");
                if (JsonClaims.TryGetObject(obj, "metadata", out JsonElement metadata)) result.SignupCodeId = JsonClaims.GetString(metadata, "signupCodeId");
            }
            else if (type.StartsWith("customer.subscription.", StringComparison.Ordinal))
            {
                result.SubscriptionId = JsonClaims.GetString(obj, "id");
                result.CustomerId = JsonClaims.GetIdOrExpandedId(obj, "customer");
            }
            else if (type.StartsWith("invoice.", StringComparison.Ordinal))
            {
                string? subscriptionId = JsonClaims.GetIdOrExpandedId(obj, "subscription");
                if (subscriptionId == null
                    && JsonClaims.TryGetObject(obj, "parent", out JsonElement parent)
                    && JsonClaims.TryGetObject(parent, "subscription_details", out JsonElement details))
                {
                    subscriptionId = JsonClaims.GetIdOrExpandedId(details, "subscription");
                }
                result.SubscriptionId = subscriptionId;
                result.CustomerId = JsonClaims.GetIdOrExpandedId(obj, "customer");
            }
            return result;
        }

        private static byte[]? TryDecodeHex(string value)
        {
            if (value.Length == 0 || value.Length % 2 != 0) return null;
            try
            {
                return Convert.FromHexString(value);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        private static KeyValuePair<string, string> Pair(string key, string value)
        {
            return new KeyValuePair<string, string>(key, value);
        }

        #endregion
    }
}
