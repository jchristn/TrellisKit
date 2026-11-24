namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Integrations.Interfaces;

    /// <summary>
    /// Reads TXT records through a DNS-over-HTTPS JSON endpoint (application/dns-json, for example
    /// https://cloudflare-dns.com/dns-query), so verification does not depend on the container's resolver.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class DohTxtResolver : IDnsTxtResolver
    {
        #region Private-Members

        private static readonly HttpClient _Http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        private readonly Func<string> _Endpoint;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the resolver.
        /// </summary>
        /// <param name="endpoint">Returns the DNS-over-HTTPS URL (read on every lookup so the setting is live).</param>
        /// <exception cref="ArgumentNullException">The endpoint is null.</exception>
        public DohTxtResolver(Func<string> endpoint)
        {
            _Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<List<string>> ResolveTxtAsync(string name, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            string url = _Endpoint().TrimEnd('?') + "?name=" + Uri.EscapeDataString(name.Trim().TrimEnd('.')) + "&type=TXT";
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.TryAddWithoutValidation("Accept", "application/dns-json");
                HttpResponseMessage response;
                try
                {
                    response = await _Http.SendAsync(request, token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is HttpRequestException || (e is TaskCanceledException && !token.IsCancellationRequested))
                {
                    throw new InvalidOperationException("The DNS lookup failed (" + e.GetType().Name + ").", e);
                }
                using (response)
                {
                    if (!response.IsSuccessStatusCode) throw new InvalidOperationException("The DNS resolver returned HTTP " + (int)response.StatusCode + ".");
                    string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    return Parse(body);
                }
            }
        }

        /// <summary>
        /// Parse a dns-json response into TXT values (quotes removed, multi-string records joined).
        /// </summary>
        /// <param name="json">Response body.</param>
        /// <returns>Values.</returns>
        /// <exception cref="InvalidOperationException">The body is not a dns-json answer.</exception>
        public static List<string> Parse(string json)
        {
            List<string> values = new List<string>();
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    if (!doc.RootElement.TryGetProperty("Answer", out JsonElement answers) || answers.ValueKind != JsonValueKind.Array) return values;
                    foreach (JsonElement answer in answers.EnumerateArray())
                    {
                        if (!answer.TryGetProperty("type", out JsonElement type) || type.ValueKind != JsonValueKind.Number || type.GetInt32() != 16) continue;
                        if (!answer.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.String) continue;
                        string text = data.GetString() ?? String.Empty;
                        values.Add(String.Join(String.Empty, text.Split("\" \"")).Trim().Trim('"'));
                    }
                }
            }
            catch (JsonException e)
            {
                throw new InvalidOperationException("The DNS resolver returned an unreadable answer.", e);
            }
            return values;
        }

        #endregion
    }
}
