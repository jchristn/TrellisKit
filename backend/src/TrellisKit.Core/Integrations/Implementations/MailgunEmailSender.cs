namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Observability;

    /// <summary>
    /// Sends administrative email through the Mailgun messages API (POST {baseUrl}{domain}/messages). Each message carries
    /// both bodies (text and html, so clients that block HTML get the plain-text part), the reply-to address, extra
    /// headers such as List-Unsubscribe and List-Unsubscribe-Post (h:), up to three tags (o:tag), variables echoed back in
    /// webhooks (v:), and the open and click tracking choice (o:tracking-opens, o:tracking-clicks). The provider message
    /// id is returned on the message. SendWithMailgun (used for address validation) sends one body format and no headers,
    /// so sending posts the form directly. Errors never include the API key or the message bodies.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class MailgunEmailSender : IEmailSender
    {
        #region Public-Members

        /// <inheritdoc />
        public bool IsConfigured
        {
            get { return !String.IsNullOrWhiteSpace(_Domain) && !String.IsNullOrWhiteSpace(_ApiKey) && !String.IsNullOrWhiteSpace(_FromAddress); }
        }

        /// <summary>
        /// Request timeout. Default 30 seconds. Minimum 1 second, maximum 300 seconds.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is outside the range.</exception>
        public TimeSpan Timeout
        {
            get { return _Timeout; }
            set
            {
                if (value < TimeSpan.FromSeconds(1) || value > TimeSpan.FromSeconds(300)) throw new ArgumentOutOfRangeException(nameof(Timeout), "Use 1 to 300 seconds.");
                _Timeout = value;
            }
        }

        /// <summary>
        /// Header names the sender never passes through from <see cref="EmailMessage.Headers"/> because Mailgun sets them
        /// from the message fields.
        /// </summary>
        public static readonly IReadOnlyList<string> ReservedHeaders = new[] { "From", "To", "Cc", "Bcc", "Subject", "Reply-To", "Content-Type", "MIME-Version", "Content-Transfer-Encoding" };

        #endregion

        #region Private-Members

        private static readonly HttpClient _SharedClient = new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        private readonly string _Domain;
        private readonly string _ApiKey;
        private readonly string _BaseUrl;
        private readonly string _FromName;
        private readonly string _FromAddress;
        private readonly HttpMessageHandler? _Handler;
        private TimeSpan _Timeout = TimeSpan.FromSeconds(30);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the sender.
        /// </summary>
        /// <param name="domain">Sending domain, for example mg.example.test.</param>
        /// <param name="apiKey">API key.</param>
        /// <param name="baseUrl">API base URL (https://api.mailgun.net/v3/ or https://api.eu.mailgun.net/v3/); empty uses the US region.</param>
        /// <param name="fromName">Default sender name.</param>
        /// <param name="fromAddress">Default sender address.</param>
        /// <param name="handler">HTTP handler, or null for the shared client (tests pass a recording handler). Not disposed by the sender.</param>
        public MailgunEmailSender(string domain, string apiKey, string baseUrl, string fromName, string fromAddress, HttpMessageHandler? handler = null)
        {
            _Domain = (domain ?? String.Empty).Trim();
            _ApiKey = (apiKey ?? String.Empty).Trim();
            string url = String.IsNullOrWhiteSpace(baseUrl) ? "https://api.mailgun.net/v3/" : baseUrl.Trim();
            _BaseUrl = url.EndsWith("/", StringComparison.Ordinal) ? url : url + "/";
            _FromName = fromName ?? String.Empty;
            _FromAddress = (fromAddress ?? String.Empty).Trim();
            _Handler = handler;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        /// <exception cref="IntegrationNotConfiguredException">The domain, API key, or from address is missing.</exception>
        /// <exception cref="ArgumentException">The message has no recipient.</exception>
        /// <exception cref="InvalidOperationException">Mailgun refused the message or could not be reached.</exception>
        public async Task SendAsync(EmailMessage message, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(message);
            if (!IsConfigured) throw new IntegrationNotConfiguredException("Mailgun is not configured: set the sending domain, API key, and from address.");
            if (String.IsNullOrWhiteSpace(message.To)) throw new ArgumentException("The message has no recipient.", nameof(message));
            using (Activity? activity = TrellisKitTelemetry.StartSpan("email.mailgun.send"))
            {
                activity?.SetTag("email.category", message.Category);
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, _BaseUrl + Uri.EscapeDataString(_Domain) + "/messages"))
                using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes("api:" + _ApiKey)));
                    request.Content = new FormUrlEncodedContent(Form(message));
                    timeout.CancelAfter(_Timeout);
                    HttpResponseMessage response;
                    HttpClient client = _Handler == null ? _SharedClient : new HttpClient(_Handler, false) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
                    try
                    {
                        response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception e) when (e is HttpRequestException || e is OperationCanceledException)
                    {
                        activity?.SetStatus(ActivityStatusCode.Error, e.GetType().Name);
                        throw new InvalidOperationException("Mailgun delivery for " + _Domain + " failed (" + e.GetType().Name + ").", e);
                    }
                    finally
                    {
                        if (_Handler != null) client.Dispose();
                    }
                    using (response)
                    {
                        string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                        activity?.SetTag("http.response.status_code", (int)response.StatusCode);
                        if (!response.IsSuccessStatusCode)
                        {
                            activity?.SetStatus(ActivityStatusCode.Error, "HTTP " + (int)response.StatusCode);
                            throw new InvalidOperationException("Mailgun refused the message for " + _Domain + " (HTTP " + (int)response.StatusCode + ": " + Describe(body) + ").");
                        }
                        string? id = ReadId(body);
                        if (String.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("Mailgun did not accept the message for " + _Domain + ".");
                        message.ProviderMessageId = NormalizeId(id);
                    }
                }
            }
        }

        /// <summary>
        /// The form fields posted to Mailgun for a message (public for tests and documentation).
        /// </summary>
        /// <param name="message">Message.</param>
        /// <returns>Ordered field list; names repeat for several tags.</returns>
        /// <exception cref="ArgumentNullException">message is null.</exception>
        public List<KeyValuePair<string, string>> Form(EmailMessage message)
        {
            ArgumentNullException.ThrowIfNull(message);
            string name = String.IsNullOrWhiteSpace(message.FromName) ? _FromName : message.FromName;
            string address = String.IsNullOrWhiteSpace(message.FromAddress) ? _FromAddress : message.FromAddress.Trim();
            string from = String.IsNullOrWhiteSpace(name) ? address : "\"" + OneLine(name).Replace("\"", "'", StringComparison.Ordinal) + "\" <" + address + ">";
            List<KeyValuePair<string, string>> form = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("from", from),
                new KeyValuePair<string, string>("to", message.To.Trim()),
                new KeyValuePair<string, string>("subject", OneLine(message.Subject))
            };
            if (!String.IsNullOrEmpty(message.TextBody)) form.Add(new KeyValuePair<string, string>("text", message.TextBody));
            if (!String.IsNullOrEmpty(message.HtmlBody)) form.Add(new KeyValuePair<string, string>("html", message.HtmlBody));
            if (String.IsNullOrEmpty(message.TextBody) && String.IsNullOrEmpty(message.HtmlBody)) form.Add(new KeyValuePair<string, string>("text", " "));
            if (!String.IsNullOrWhiteSpace(message.ReplyTo)) form.Add(new KeyValuePair<string, string>("h:Reply-To", OneLine(message.ReplyTo)));
            foreach (KeyValuePair<string, string> header in message.Headers)
            {
                string key = (header.Key ?? String.Empty).Trim();
                if (key.Length == 0 || !IsHeaderName(key) || ReservedHeaders.Contains(key, StringComparer.OrdinalIgnoreCase)) continue;
                form.Add(new KeyValuePair<string, string>("h:" + key, OneLine(header.Value)));
            }
            foreach (string tag in message.Tags.Where(t => !String.IsNullOrWhiteSpace(t)).Select(t => Ascii(t.Trim(), 128)).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal).Take(EmailMessage.MaxTags))
                form.Add(new KeyValuePair<string, string>("o:tag", tag));
            foreach (KeyValuePair<string, string> variable in message.Variables)
            {
                string key = (variable.Key ?? String.Empty).Trim();
                if (key.Length == 0 || !IsHeaderName(key)) continue;
                form.Add(new KeyValuePair<string, string>("v:" + key, OneLine(variable.Value)));
            }
            if (message.TrackOpens.HasValue || message.TrackClicks.HasValue)
            {
                bool any = message.TrackOpens == true || message.TrackClicks == true;
                form.Add(new KeyValuePair<string, string>("o:tracking", any ? "yes" : "no"));
                if (message.TrackOpens.HasValue) form.Add(new KeyValuePair<string, string>("o:tracking-opens", message.TrackOpens.Value ? "yes" : "no"));
                if (message.TrackClicks.HasValue) form.Add(new KeyValuePair<string, string>("o:tracking-clicks", message.TrackClicks.Value ? "htmlonly" : "no"));
            }
            return form;
        }

        /// <summary>
        /// A Mailgun message id without angle brackets.
        /// </summary>
        /// <param name="id">Id as returned by Mailgun or a webhook.</param>
        /// <returns>Normalized id.</returns>
        public static string NormalizeId(string id)
        {
            return (id ?? String.Empty).Trim().Trim('<', '>');
        }

        #endregion

        #region Private-Methods

        private static string OneLine(string? value)
        {
            return (value ?? String.Empty).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        }

        private static bool IsHeaderName(string name)
        {
            return name.Length <= 76 && name.All(c => c > 32 && c < 127 && c != ':');
        }

        private static string Ascii(string value, int max)
        {
            string ascii = new string(value.Where(c => c >= 32 && c < 127).ToArray());
            return ascii.Length > max ? ascii.Substring(0, max) : ascii;
        }

        private static string? ReadId(string body)
        {
            if (String.IsNullOrWhiteSpace(body)) return null;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
                }
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string Describe(string body)
        {
            if (String.IsNullOrWhiteSpace(body)) return "no body";
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("message", out JsonElement m) && m.ValueKind == JsonValueKind.String)
                        return Ascii(m.GetString() ?? String.Empty, 200);
                }
            }
            catch (JsonException)
            {
                // Not JSON: fall through to the trimmed text.
            }
            return Ascii(OneLine(body), 200);
        }

        #endregion
    }
}
