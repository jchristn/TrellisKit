namespace TrellisKit.Server.Services.CatalogImport
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Integrations.Interfaces;

    /// <summary>
    /// Fetches one reviewed catalog manifest: HTTPS only through the outbound address policy (no proxy, cookies, or
    /// redirects), robots.txt honored for the configured product token, at most <see cref="MaxBytes"/> bytes of JSON,
    /// and a <see cref="TimeoutSeconds"/> second timeout. An injected client (tests) bypasses the address policy, so its
    /// caller must enforce outbound address rules. Thread safe.
    /// </summary>
    public sealed class CatalogManifestFetcher
    {
        #region Public-Members

        /// <summary>
        /// Largest accepted manifest, in bytes (1,000,000).
        /// </summary>
        public const int MaxBytes = 1000000;

        /// <summary>
        /// Largest robots.txt read, in bytes (100,000); longer files are truncated.
        /// </summary>
        public const int MaxRobotsBytes = 100000;

        /// <summary>
        /// Request timeout for robots.txt and the manifest, in seconds (20).
        /// </summary>
        public const int TimeoutSeconds = 20;

        /// <summary>
        /// The User-Agent product token sent with every request and matched in robots.txt groups.
        /// </summary>
        public const string ProductToken = "TrellisKitCatalogImport";

        #endregion

        #region Private-Members

        private readonly HttpClient? _Injected;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="client">Optional client for local transport tests; null builds a policy-enforcing client per fetch.</param>
        public CatalogManifestFetcher(HttpClient? client = null)
        {
            _Injected = client;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Fetch a manifest after checking robots.txt.
        /// </summary>
        /// <param name="url">Absolute HTTPS manifest URL.</param>
        /// <param name="policy">Outbound address policy (ignored with an injected client).</param>
        /// <param name="resolver">DNS resolver (ignored with an injected client).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The outcome; never throws for network or HTTP failures.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<CatalogManifestResponse> FetchAsync(Uri url, ILinkPreviewAddressPolicy policy, IDnsResolver resolver, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(url);
            ArgumentNullException.ThrowIfNull(policy);
            ArgumentNullException.ThrowIfNull(resolver);
            if (!url.IsAbsoluteUri || url.Scheme != Uri.UriSchemeHttps) return new CatalogManifestResponse { Error = "The manifest URL must be absolute HTTPS." };
            TimeSpan timeout = TimeSpan.FromSeconds(TimeoutSeconds);
            HttpClient client = _Injected ?? new HttpClient(PolicyHttpHandlerFactory.Create(policy, resolver, timeout), true) { Timeout = timeout };
            try
            {
                CatalogManifestResponse? robots = await RobotsAsync(client, url, token).ConfigureAwait(false);
                if (robots != null) return robots;
                using (HttpRequestMessage request = Request(url, "application/json"))
                using (CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    limit.CancelAfter(timeout);
                    using (HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false))
                    {
                        int status = (int)response.StatusCode;
                        if (status >= 300 && status < 400) return new CatalogManifestResponse { Status = status, Error = "Redirects are not followed; review the final manifest URL." };
                        if (status < 200 || status >= 300) return new CatalogManifestResponse { Status = status, Error = "HTTP " + status, Retryable = status == 429 || status >= 500 };
                        string? media = response.Content.Headers.ContentType?.MediaType;
                        if (media == null || !(media.Equals("application/json", StringComparison.OrdinalIgnoreCase) || media.EndsWith("+json", StringComparison.OrdinalIgnoreCase)))
                            return new CatalogManifestResponse { Status = status, Error = "The manifest is not served as application/json." };
                        if (response.Content.Headers.ContentLength > MaxBytes) return new CatalogManifestResponse { Status = status, Error = "The manifest exceeds " + MaxBytes + " bytes." };
                        string? body = await ReadAsync(response, MaxBytes, limit.Token).ConfigureAwait(false);
                        if (body == null) return new CatalogManifestResponse { Status = status, Error = "The manifest exceeds " + MaxBytes + " bytes." };
                        return new CatalogManifestResponse { Status = status, Body = body };
                    }
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                return new CatalogManifestResponse { Error = "The manifest request timed out.", Retryable = true };
            }
            catch (HttpRequestException)
            {
                return new CatalogManifestResponse { Error = "The manifest host could not be reached.", Retryable = true };
            }
            finally
            {
                if (_Injected == null) client.Dispose();
            }
        }

        /// <summary>
        /// Whether robots.txt rules allow a path for <see cref="ProductToken"/>: the most specific group (the product
        /// token, else *) and the longest matching Allow or Disallow prefix win; Allow wins a tie.
        /// </summary>
        /// <param name="robots">robots.txt text.</param>
        /// <param name="path">Path and query.</param>
        /// <returns>True when allowed.</returns>
        public static bool IsAllowed(string? robots, string path)
        {
            if (String.IsNullOrEmpty(robots)) return true;
            List<KeyValuePair<bool, string>> specific = new List<KeyValuePair<bool, string>>();
            List<KeyValuePair<bool, string>> wildcard = new List<KeyValuePair<bool, string>>();
            bool inSpecific = false;
            bool inWildcard = false;
            bool readingAgents = false;
            bool sawSpecific = false;
            foreach (string raw in robots.Split('\n'))
            {
                string line = raw;
                int hash = line.IndexOf('#');
                if (hash >= 0) line = line.Substring(0, hash);
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                string field = line.Substring(0, colon).Trim().ToLowerInvariant();
                string value = line.Substring(colon + 1).Trim();
                if (field == "user-agent")
                {
                    if (!readingAgents)
                    {
                        inSpecific = false;
                        inWildcard = false;
                    }
                    readingAgents = true;
                    if (value == "*") inWildcard = true;
                    else if (value.Equals(ProductToken, StringComparison.OrdinalIgnoreCase))
                    {
                        inSpecific = true;
                        sawSpecific = true;
                    }
                    continue;
                }
                readingAgents = false;
                if (field != "allow" && field != "disallow") continue;
                KeyValuePair<bool, string> rule = new KeyValuePair<bool, string>(field == "allow", value);
                if (inSpecific) specific.Add(rule);
                if (inWildcard) wildcard.Add(rule);
            }
            List<KeyValuePair<bool, string>> rules = sawSpecific ? specific : wildcard;
            int best = -1;
            bool allowed = true;
            foreach (KeyValuePair<bool, string> rule in rules)
            {
                if (rule.Value.Length == 0) continue;
                if (!path.StartsWith(rule.Value, StringComparison.Ordinal)) continue;
                if (rule.Value.Length > best || (rule.Value.Length == best && rule.Key))
                {
                    best = rule.Value.Length;
                    allowed = rule.Key;
                }
            }
            return allowed;
        }

        #endregion

        #region Private-Methods

        private static HttpRequestMessage Request(Uri url, string accept)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue(ProductToken, "1.0"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
            return request;
        }

        private static async Task<CatalogManifestResponse?> RobotsAsync(HttpClient client, Uri url, CancellationToken token)
        {
            Uri robotsUrl = new Uri(url.GetLeftPart(UriPartial.Authority) + "/robots.txt");
            string? text = null;
            using (HttpRequestMessage request = Request(robotsUrl, "text/plain"))
            using (CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                limit.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
                using (HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false))
                {
                    int status = (int)response.StatusCode;
                    // Missing robots.txt (4xx) allows everything; server errors mean the site is not answering.
                    if (status >= 500) return new CatalogManifestResponse { Error = "The source did not answer robots.txt.", Retryable = true };
                    if (status >= 200 && status < 300) text = await ReadAsync(response, MaxRobotsBytes, limit.Token, true).ConfigureAwait(false);
                }
            }
            if (!IsAllowed(text, url.PathAndQuery)) return new CatalogManifestResponse { Status = -1, Error = "robots.txt disallows this manifest." };
            return null;
        }

        private static async Task<string?> ReadAsync(HttpResponseMessage response, int max, CancellationToken token, bool truncate = false)
        {
            using (Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            using (MemoryStream buffer = new MemoryStream())
            {
                byte[] chunk = new byte[16384];
                while (true)
                {
                    int read = await stream.ReadAsync(chunk, 0, chunk.Length, token).ConfigureAwait(false);
                    if (read == 0) break;
                    buffer.Write(chunk, 0, read);
                    if (buffer.Length > max)
                    {
                        if (!truncate) return null;
                        buffer.SetLength(max);
                        break;
                    }
                }
                return Encoding.UTF8.GetString(buffer.ToArray());
            }
        }

        #endregion
    }
}
