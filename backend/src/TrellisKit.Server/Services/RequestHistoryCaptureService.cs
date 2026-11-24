namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Security;
    using TrellisKit.Core.Settings;
    using WatsonWebserver.Core;

    /// <summary>
    /// Captures each completed request into request history. The entry is built synchronously from the context and
    /// written on a background task, so capture never delays or fails a response.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class RequestHistoryCaptureService
    {
        #region Public-Members

        /// <summary>
        /// Placeholder written in place of redacted header values.
        /// </summary>
        public const string RedactedValue = "***redacted***";

        #endregion

        #region Private-Members

        private static readonly HashSet<string> _ExactRedactions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "authorization", "proxy-authorization", "cookie", "set-cookie", "x-password", "x-secret-key"
        };

        private static readonly HashSet<string> _RedactedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "password", "newPassword", "currentPassword", "token", "idToken", "identityToken", "refreshToken", "accessToken",
            "signedTransaction", "purchaseToken", "nonce", "dateOfBirth", "authValue", "privateKeyPem", "visitorToken"
        };

        private static readonly Regex _SignatureParameter = new Regex(
            "((?:[?&]|\\\\u0026)(?:sig|signature|x-amz-signature|x-amz-security-token|x-amz-credential|token|near)=)[^&\"\\s\\\\]*",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex _PrivateMediaToken = new Regex("(/media/private/)[A-Za-z0-9_-]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly DatabaseDriverBase _Database;
        private readonly RequestHistorySettings _Settings;
        private Func<HttpContextBase, string?>? _PropertyOf = null;
        private RestSettings? _Rest = null;
        private readonly AppLogger _Logger;

        #endregion

        #region Public-Members

        /// <summary>
        /// Resolves the property a completed request was bound to (set by the server). Requests that resolved to no
        /// property are stored under the reserved id unresolved, and platform requests under platform. Default null,
        /// which uses the property the database driver is bound to.
        /// </summary>
        public Func<HttpContextBase, string?>? PropertyOf
        {
            get { return _PropertyOf; }
            set { _PropertyOf = value; }
        }

        /// <summary>
        /// Listener settings whose trusted proxies resolve the stored source IP from X-Forwarded-For (set by the
        /// server). Default null, which stores the socket peer.
        /// </summary>
        public RestSettings? Rest
        {
            get { return _Rest; }
            set { _Rest = value; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Request history settings (the running instance, so live changes apply).</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public RequestHistoryCaptureService(DatabaseDriverBase database, RequestHistorySettings settings, AppLogger logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Capture a completed request. Never throws.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        public void Capture(HttpContextBase ctx)
        {
            if (ctx == null || !_Settings.Enabled) return;
            if (ctx.Request.Method == HttpMethod.OPTIONS) return;

            RequestHistoryEntry entry;
            try
            {
                entry = Build(ctx);
            }
            catch (Exception e)
            {
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.RequestHistoryCaptures, "outcome", "build_failure");
                _Logger.Debug("[RequestHistoryCapture] build failed: " + e.Message);
                return;
            }

            string propertyId = _PropertyOf?.Invoke(ctx) ?? _Database.BoundPropertyId ?? PropertyIds.Unresolved;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _Database.ForProperty(propertyId).RequestHistory.CreateAsync(entry).ConfigureAwait(false);
                    TrellisKitTelemetry.Increment(TrellisKitTelemetry.RequestHistoryCaptures, "outcome", "success");
                }
                catch (Exception e)
                {
                    TrellisKitTelemetry.Increment(TrellisKitTelemetry.RequestHistoryCaptures, "outcome", "write_failure");
                    _Logger.Warn("[RequestHistoryCapture] write failed: " + e.Message);
                }
            });
        }

        /// <summary>
        /// Build an entry from a completed request. Exposed for tests.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <returns>Entry.</returns>
        /// <exception cref="ArgumentNullException">The context is null.</exception>
        public RequestHistoryEntry Build(HttpContextBase ctx)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            RequestContext? context = ctx.Metadata as RequestContext;

            RequestHistoryEntry entry = new RequestHistoryEntry
            {
                MetricRoute = Metrics.MetricRequestDimensions.Route(ctx.Request.Method.ToString(), ctx.Request.Url.RawWithoutQuery),
                MetricApp = Metrics.MetricRequestDimensions.Header(ctx.Request.Headers, "X-Client"),
                MetricPlatform = Metrics.MetricRequestDimensions.Header(ctx.Request.Headers, "X-Client-Platform"),
                MetricVersion = Metrics.MetricRequestDimensions.Header(ctx.Request.Headers, "X-Client-Version"),
                UserId = context?.UserId,
                PrincipalName = context?.PrincipalName,
                Method = ctx.Request.Method.ToString(),
                Path = ctx.Request.Url.RawWithoutQuery ?? String.Empty,
                Url = RedactUrl(ctx.Request.Url.RawWithQuery) ?? String.Empty,
                StatusCode = ctx.Response.StatusCode,
                DurationMs = ctx.Timestamp.TotalMs ?? 0,
                SourceIp = ClientAddressResolver.Resolve(ctx, _Rest),
                CreatedUtc = ctx.Timestamp.Start,
                CompletedUtc = ctx.Timestamp.End ?? DateTime.UtcNow,
                RequestHeaders = RedactHeaders(ctx.Request.Headers),
                ResponseHeaders = RedactHeaders(ctx.Response.Headers)
            };

            bool sensitivePath = entry.Path.StartsWith(TrellisKit.Core.Constants.ApiPrefix + "/auth", StringComparison.OrdinalIgnoreCase) ||
                entry.Path.StartsWith(TrellisKit.Core.Constants.ApiPrefix + "/me", StringComparison.OrdinalIgnoreCase) ||
                entry.Path.StartsWith(TrellisKit.Core.Constants.ApiPrefix + "/membership", StringComparison.OrdinalIgnoreCase);
            // AI endpoint bodies carry write-only secrets (docs/AI_CONTRACT.md section 2.4): their request bodies and
            // reveal responses are never captured, and SSE streams are recorded without the body.
            bool aiSecrets = entry.Path.StartsWith(TrellisKit.Core.Constants.ApiPrefix + "/admin/ai/endpoints", StringComparison.OrdinalIgnoreCase);
            // Directory place search (docs/PHASE4_CONTRACT.md section 3.5): the typed place text is never stored.
            bool placeSearch = entry.Path.Equals(TrellisKit.Core.Constants.ApiPrefix + "/directory/geocode", StringComparison.OrdinalIgnoreCase) || entry.Path.Equals(TrellisKit.Core.Constants.ApiPrefix + "/directory/geocode/enumerate", StringComparison.OrdinalIgnoreCase);
            if (placeSearch) entry.Url = entry.Path;
            bool eventStream = (ctx.Response.ContentType ?? String.Empty).StartsWith("text/event-stream", StringComparison.OrdinalIgnoreCase);
            // Direct messages (docs/PHASE5_CONTRACT.md section 3.8 point 9): request and response bodies are never captured.
            bool messaging = IsMessagingPath(entry.Path);
            string? rawRequest = messaging ? MessagingRedacted : !aiSecrets && !placeSearch && IsText(ctx.Request.ContentType) ? RedactBody(ctx.Request.DataAsString, sensitivePath) : null;
            AttachBody(rawRequest, _Settings.MaxRequestBodyBytes, out string? requestBody, out long requestBytes, out bool requestTruncated);
            if (rawRequest == null) requestBytes = ctx.Request.ContentLength;
            entry.RequestBody = requestBody;
            entry.RequestBodyBytes = requestBytes;
            entry.RequestBodyTruncated = requestTruncated;

            bool reveal = aiSecrets && entry.Path.EndsWith("/reveal", StringComparison.OrdinalIgnoreCase);
            string? rawResponse = messaging ? MessagingRedacted : !reveal && !eventStream && !placeSearch && IsText(ctx.Response.ContentType) ? RedactBody(ctx.Response.DataAsString, sensitivePath) : null;
            AttachBody(rawResponse, _Settings.MaxResponseBodyBytes, out string? responseBody, out long responseBytes, out bool responseTruncated);
            entry.ResponseBody = responseBody;
            entry.ResponseBodyBytes = responseBytes;
            entry.ResponseBodyTruncated = responseTruncated;
            entry.MetricError = Metrics.MetricRequestDimensions.Error(entry.StatusCode, ctx.Response.DataAsString);

            return entry;
        }

        /// <summary>
        /// Placeholder captured instead of direct message request and response bodies.
        /// </summary>
        public const string MessagingRedacted = "[redacted]";

        /// <summary>
        /// Whether a path carries direct message content (/conversations*, /messages*, /me/messaging*, and the Messaging
        /// moderation routes), whose bodies are never captured.
        /// </summary>
        /// <param name="path">Path without query.</param>
        /// <returns>True for a messaging path.</returns>
        public static bool IsMessagingPath(string? path)
        {
            string p = path ?? String.Empty;
            string api = TrellisKit.Core.Constants.ApiPrefix;
            return p.StartsWith(api + "/conversations", StringComparison.OrdinalIgnoreCase) || p.StartsWith(api + "/messages", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith(api + "/me/messaging", StringComparison.OrdinalIgnoreCase) || p.StartsWith(api + "/admin/moderation/messaging", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether a header must be redacted before capture.
        /// </summary>
        /// <param name="name">Header name.</param>
        /// <returns>True when the header carries a secret.</returns>
        public static bool ShouldRedact(string name)
        {
            if (String.IsNullOrEmpty(name)) return false;
            if (_ExactRedactions.Contains(name)) return true;
            string lower = name.ToLowerInvariant();
            return lower.Contains("api-key") || lower.Contains("apikey") || lower.Contains("token") || lower.Contains("password") || lower.Contains("secret");
        }

        /// <summary>
        /// Redact secret-bearing JSON fields (passwords, tokens, store receipts) anywhere in a body. A body on a
        /// sensitive path that is not valid JSON is dropped entirely.
        /// </summary>
        /// <param name="body">Body text, or null.</param>
        /// <param name="sensitivePath">Whether the request was to an auth, account, or purchase route.</param>
        /// <returns>Redacted body, or null.</returns>
        public static string? RedactBody(string? body, bool sensitivePath)
        {
            if (String.IsNullOrWhiteSpace(body)) return body;
            string trimmed = body.TrimStart();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal) && !trimmed.StartsWith("[", StringComparison.Ordinal)) return sensitivePath ? null : RedactUrl(body);
            try
            {
                JsonNode? node = JsonNode.Parse(body);
                if (node == null) return body;
                if (!Redact(node)) return body;
                return node.ToJsonString();
            }
            catch (JsonException)
            {
                return sensitivePath ? null : RedactUrl(body);
            }
        }

        /// <summary>
        /// Redact signature query parameters (sig, signature, X-Amz-Signature, X-Amz-Security-Token,
        /// X-Amz-Credential, token) and private media URL tokens (/media/private/{token}/) in a URL or text, keeping the rest
        /// (for example exp) readable.
        /// </summary>
        /// <param name="text">URL or text, or null.</param>
        /// <returns>Redacted text, or null.</returns>
        public static string? RedactUrl(string? text)
        {
            if (String.IsNullOrEmpty(text)) return text;
            string redacted = _SignatureParameter.Replace(text, m => m.Groups[1].Value + RedactedValue);
            return _PrivateMediaToken.Replace(redacted, m => m.Groups[1].Value + RedactedValue);
        }

        #endregion

        #region Private-Methods

        private static bool Redact(JsonNode node)
        {
            bool changed = false;
            if (node is JsonObject obj)
            {
                foreach (string key in obj.Select(p => p.Key).ToList())
                {
                    JsonNode? child = obj[key];
                    if (child == null) continue;
                    bool cookieValue = String.Equals(key, "value", StringComparison.OrdinalIgnoreCase) && obj.ContainsKey("name") && IsCookieEntry(obj);
                    // A directory near query carries the caller's position (docs/PHASE4_CONTRACT.md section 3.5).
                    if (String.Equals(key, "near", StringComparison.OrdinalIgnoreCase) && child is JsonObject near)
                    {
                        foreach (string coordinate in near.Select(p => p.Key).Where(k => String.Equals(k, "lat", StringComparison.OrdinalIgnoreCase) || String.Equals(k, "lng", StringComparison.OrdinalIgnoreCase)).ToList())
                            near[coordinate] = RedactedValue;
                        changed = true;
                        continue;
                    }
                    if ((_RedactedFields.Contains(key) || cookieValue) && child.GetValueKind() != JsonValueKind.Null)
                    {
                        obj[key] = RedactedValue;
                        changed = true;
                    }
                    else if (child.GetValueKind() == JsonValueKind.String)
                    {
                        string text = child.GetValue<string>();
                        string? redacted = RedactUrl(text);
                        if (!String.Equals(text, redacted, StringComparison.Ordinal))
                        {
                            obj[key] = redacted;
                            changed = true;
                        }
                    }
                    else if (Redact(child)) changed = true;
                }
            }
            else if (node is JsonArray array)
            {
                for (int i = 0; i < array.Count; i++)
                {
                    JsonNode? child = array[i];
                    if (child == null) continue;
                    if (child.GetValueKind() == JsonValueKind.String)
                    {
                        string text = child.GetValue<string>();
                        string? redacted = RedactUrl(text);
                        if (!String.Equals(text, redacted, StringComparison.Ordinal))
                        {
                            array[i] = redacted;
                            changed = true;
                        }
                    }
                    else if (Redact(child)) changed = true;
                }
            }
            return changed;
        }

        private static bool IsCookieEntry(JsonObject obj)
        {
            // A { name, value } pair inside a "cookies" array (for example POST /api/v1/media/grant) carries a live cookie.
            JsonNode? parent = obj.Parent;
            if (parent is JsonArray && parent.Parent is JsonObject owner)
            {
                foreach (KeyValuePair<string, JsonNode?> pair in owner)
                {
                    if (ReferenceEquals(pair.Value, parent)) return pair.Key.Contains("cookie", StringComparison.OrdinalIgnoreCase);
                }
            }
            return false;
        }

        private static bool IsText(string? contentType)
        {
            if (String.IsNullOrWhiteSpace(contentType)) return true;
            string lower = contentType.ToLowerInvariant();
            return lower.Contains("json") || lower.StartsWith("text/", StringComparison.Ordinal) || lower.Contains("xml") || lower.Contains("x-www-form-urlencoded");
        }

        private static void AttachBody(string? body, int maxBytes, out string? captured, out long bytes, out bool truncated)
        {
            captured = null;
            bytes = 0;
            truncated = false;
            if (String.IsNullOrEmpty(body)) return;

            byte[] encoded = Encoding.UTF8.GetBytes(body);
            bytes = encoded.Length;
            if (encoded.Length <= maxBytes)
            {
                captured = body;
                return;
            }

            truncated = true;
            captured = maxBytes == 0 ? null : Encoding.UTF8.GetString(encoded, 0, maxBytes).TrimEnd('�');
        }

        private static Dictionary<string, string> RedactHeaders(NameValueCollection? headers)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (headers == null) return result;
            for (int i = 0; i < headers.Count; i++)
            {
                string? key = headers.GetKey(i);
                if (String.IsNullOrEmpty(key)) continue;
                result[key] = ShouldRedact(key) ? RedactedValue : (RedactUrl(headers.Get(i)) ?? String.Empty);
            }
            return result;
        }

        #endregion
    }
}
