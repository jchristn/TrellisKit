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
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Expo push API client: POST {BaseUrl}/--/api/v2/push/send in batches of 100 and POST
    /// {BaseUrl}/--/api/v2/push/getReceipts in batches of 1000, with the optional push.expoAccessToken as a bearer token
    /// (read live). Every request records the expo integration metrics and a span ("expo push.send",
    /// "expo push.getReceipts"). Thread safe.
    /// </summary>
    public class ExpoPushSender : IPushSender
    {
        /// <summary>
        /// Base URL without a trailing slash. Default https://exp.host. Tests point it at a fake endpoint. Never null.
        /// </summary>
        public string BaseUrl
        {
            get { return _BaseUrl; }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(BaseUrl));
                _BaseUrl = value.Trim().TrimEnd('/');
            }
        }

        /// <summary>
        /// Messages per send request. Default 100 (the Expo maximum). Minimum 1, maximum 100.
        /// </summary>
        public int SendBatchSize
        {
            get { return _SendBatchSize; }
            set { _SendBatchSize = Math.Clamp(value, 1, 100); }
        }

        /// <summary>
        /// Ticket identifiers per receipts request. Default 1000 (the Expo maximum). Minimum 1, maximum 1000.
        /// </summary>
        public int ReceiptBatchSize
        {
            get { return _ReceiptBatchSize; }
            set { _ReceiptBatchSize = Math.Clamp(value, 1, 1000); }
        }

        /// <inheritdoc />
        public bool IsConfigured
        {
            get { return true; }
        }

        private const string _Service = "expo";
        private readonly PushSettings _Settings;
        private readonly HttpClient _Http;
        private string _BaseUrl = "https://exp.host";
        private int _SendBatchSize = 100;
        private int _ReceiptBatchSize = 1000;

        /// <summary>
        /// Instantiate the client.
        /// </summary>
        /// <param name="settings">Push settings (live instance; supplies the access token).</param>
        /// <param name="http">Shared HTTP client.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public ExpoPushSender(PushSettings settings, HttpClient http)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Http = http ?? throw new ArgumentNullException(nameof(http));
        }

        /// <inheritdoc />
        public async Task<List<PushTicket>> SendAsync(List<PushMessage> messages, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(messages);
            List<PushTicket> tickets = new List<PushTicket>();
            for (int i = 0; i < messages.Count; i += _SendBatchSize)
            {
                List<PushMessage> batch = messages.Skip(i).Take(_SendBatchSize).ToList();
                List<Dictionary<string, object?>> payload = batch.Select(ToPayload).ToList();
                using (JsonDocument doc = await PostAsync("push.send", "/--/api/v2/push/send", payload, token).ConfigureAwait(false))
                {
                    if (!doc.RootElement.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
                        throw new HttpRequestException("The Expo push response has no data array.");
                    List<PushTicket> parsed = data.EnumerateArray().Select(ParseTicket).ToList();
                    for (int j = 0; j < batch.Count; j++)
                    {
                        tickets.Add(j < parsed.Count ? parsed[j] : new PushTicket { Status = "error", Message = "No ticket returned." });
                    }
                }
            }
            return tickets;
        }

        /// <inheritdoc />
        public async Task<Dictionary<string, PushReceipt>> GetReceiptsAsync(List<string> ticketIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ticketIds);
            Dictionary<string, PushReceipt> result = new Dictionary<string, PushReceipt>(StringComparer.Ordinal);
            List<string> ids = ticketIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            for (int i = 0; i < ids.Count; i += _ReceiptBatchSize)
            {
                Dictionary<string, object> payload = new Dictionary<string, object> { { "ids", ids.Skip(i).Take(_ReceiptBatchSize).ToList() } };
                using (JsonDocument doc = await PostAsync("push.getReceipts", "/--/api/v2/push/getReceipts", payload, token).ConfigureAwait(false))
                {
                    if (!doc.RootElement.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Object) continue;
                    foreach (JsonProperty property in data.EnumerateObject())
                    {
                        result[property.Name] = new PushReceipt
                        {
                            Status = Text(property.Value, "status") ?? String.Empty,
                            Message = Text(property.Value, "message"),
                            Error = DetailsError(property.Value)
                        };
                    }
                }
            }
            return result;
        }

        private async Task<JsonDocument> PostAsync(string operation, string path, object payload, CancellationToken token)
        {
            Activity? activity = TrellisKitTelemetry.StartIntegrationSpan(_Service, operation);
            Stopwatch stopwatch = Stopwatch.StartNew();
            string outcome = "failure";
            try
            {
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, _BaseUrl + path))
                {
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    string accessToken = (_Settings.ExpoAccessToken ?? String.Empty).Trim();
                    if (accessToken.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                    using (HttpResponseMessage response = await _Http.SendAsync(request, token).ConfigureAwait(false))
                    {
                        string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode)
                        {
                            throw new HttpRequestException("Expo " + operation + " returned " + (int)response.StatusCode + ": " + Truncate(body), null, response.StatusCode);
                        }
                        JsonDocument doc;
                        try
                        {
                            doc = JsonDocument.Parse(body);
                        }
                        catch (JsonException e)
                        {
                            throw new HttpRequestException("Expo " + operation + " returned a body that is not JSON.", e);
                        }
                        outcome = "success";
                        return doc;
                    }
                }
            }
            finally
            {
                TrellisKitTelemetry.RecordIntegration(activity, _Service, operation, stopwatch.Elapsed, outcome);
                activity?.Dispose();
            }
        }

        private static Dictionary<string, object?> ToPayload(PushMessage message)
        {
            Dictionary<string, object?> payload = new Dictionary<string, object?>
            {
                { "to", message.To },
                { "title", message.Title },
                { "body", message.Body },
                { "data", message.Data },
                { "sound", message.Sound },
                { "channelId", message.ChannelId },
                { "priority", message.Priority }
            };
            if (message.Badge != null) payload["badge"] = message.Badge.Value;
            return payload;
        }

        private static PushTicket ParseTicket(JsonElement element)
        {
            return new PushTicket
            {
                Status = Text(element, "status") ?? String.Empty,
                Id = Text(element, "id"),
                Message = Text(element, "message"),
                Error = DetailsError(element)
            };
        }

        private static string? DetailsError(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object) return null;
            if (!element.TryGetProperty("details", out JsonElement details) || details.ValueKind != JsonValueKind.Object) return null;
            return Text(details, "error");
        }

        private static string? Text(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object) return null;
            if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String) return null;
            return value.GetString();
        }

        private static string Truncate(string text)
        {
            if (String.IsNullOrEmpty(text)) return String.Empty;
            return text.Length <= 300 ? text : text.Substring(0, 300);
        }
    }
}
