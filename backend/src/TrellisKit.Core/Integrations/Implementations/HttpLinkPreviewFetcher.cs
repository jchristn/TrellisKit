namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Link preview fetcher with SSRF protection (contract section 5.9). For every hop (the first request and each
    /// redirect): the URL must be http or https without credentials on an allowed port (80 or 443); the host must not
    /// be under a linkPreviews.blockedDomains suffix; the host is resolved exactly once and the hop is refused when
    /// ANY resolved address is refused by the address policy; the connection is then made to the first checked
    /// address through SocketsHttpHandler.ConnectCallback (pinned, so DNS rebinding cannot swap it) while the URL
    /// keeps the original host name for TLS SNI and the Host header. Redirects are followed manually up to
    /// linkPreviews.maxRedirects. No proxy, no cookies, a fixed User-Agent, one total timeout of
    /// linkPreviews.timeoutSeconds (also the connect timeout), and reading stops after the size limit.
    /// Thread safety: safe to call concurrently; each fetch uses its own handler.
    /// </summary>
    public class HttpLinkPreviewFetcher : ILinkPreviewFetcher
    {
        #region Public-Members

        /// <summary>
        /// Integration service name used in telemetry.
        /// </summary>
        public const string ServiceName = "http_preview";

        #endregion

        #region Private-Members

        private readonly LinkPreviewSettings _Settings;
        private readonly IDnsResolver _Resolver;
        private readonly ILinkPreviewAddressPolicy _Policy;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the fetcher.
        /// </summary>
        /// <param name="settings">Live link preview settings.</param>
        /// <param name="resolver">DNS resolver, or null for the system resolver.</param>
        /// <param name="policy">Address policy, or null for <see cref="PublicAddressPolicy"/> (production).</param>
        /// <exception cref="ArgumentNullException">The settings are null.</exception>
        public HttpLinkPreviewFetcher(LinkPreviewSettings settings, IDnsResolver? resolver = null, ILinkPreviewAddressPolicy? policy = null)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Resolver = resolver ?? new SystemDnsResolver();
            _Policy = policy ?? new PublicAddressPolicy();
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<LinkFetchResult> FetchPageAsync(Uri url, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(url);
            return FetchAsync(url, false, token);
        }

        /// <inheritdoc />
        public Task<LinkFetchResult> FetchImageAsync(Uri url, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(url);
            return FetchAsync(url, true, token);
        }

        /// <summary>
        /// Whether a host is under one of the blocked domain suffixes (case-insensitive; "example.test" blocks
        /// example.test and every subdomain).
        /// </summary>
        /// <param name="host">Host name.</param>
        /// <param name="blockedDomains">Suffixes.</param>
        /// <returns>True when blocked.</returns>
        public static bool IsBlockedDomain(string? host, IEnumerable<string>? blockedDomains)
        {
            if (String.IsNullOrEmpty(host) || blockedDomains == null) return false;
            string h = host.TrimEnd('.').ToLowerInvariant();
            foreach (string raw in blockedDomains)
            {
                if (String.IsNullOrWhiteSpace(raw)) continue;
                string suffix = raw.Trim().Trim('.').ToLowerInvariant();
                if (suffix.Length == 0) continue;
                if (h == suffix || h.EndsWith("." + suffix, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether bytes start with a JPEG, PNG, or WebP signature.
        /// </summary>
        /// <param name="data">Bytes.</param>
        /// <returns>True for an accepted image type.</returns>
        public static bool IsAcceptedImage(byte[]? data)
        {
            if (data == null || data.Length < 12) return false;
            if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return true;
            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A) return true;
            return data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 && data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50;
        }

        #endregion

        #region Private-Methods

        private async Task<LinkFetchResult> FetchAsync(Uri url, bool image, CancellationToken token)
        {
            string operation = image ? "image" : "page";
            Stopwatch stopwatch = Stopwatch.StartNew();
            using (Activity? activity = TrellisKitTelemetry.StartIntegrationSpan(ServiceName, operation))
            {
                LinkFetchResult result;
                using (CancellationTokenSource total = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    TimeSpan timeout = TimeSpan.FromSeconds(Math.Max(1, _Settings.TimeoutSeconds));
                    total.CancelAfter(timeout);
                    try
                    {
                        result = await FollowAsync(url, image, timeout, total.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        result = LinkFetchResult.Fail(LinkFetchOutcomeEnum.Timeout, url, "timeout");
                    }
                    catch (HttpRequestException e) when (!token.IsCancellationRequested)
                    {
                        result = LinkFetchResult.Fail(total.IsCancellationRequested ? LinkFetchOutcomeEnum.Timeout : LinkFetchOutcomeEnum.Error, url, e.GetType().Name);
                    }
                    catch (IOException e) when (!token.IsCancellationRequested)
                    {
                        result = LinkFetchResult.Fail(total.IsCancellationRequested ? LinkFetchOutcomeEnum.Timeout : LinkFetchOutcomeEnum.Error, url, e.GetType().Name);
                    }
                    catch (SocketException e) when (!token.IsCancellationRequested)
                    {
                        result = LinkFetchResult.Fail(LinkFetchOutcomeEnum.Error, url, e.SocketErrorCode.ToString());
                    }
                }
                token.ThrowIfCancellationRequested();
                activity?.SetTag("link_preview.outcome", result.Outcome.ToString());
                TrellisKitTelemetry.RecordIntegration(activity, ServiceName, operation, stopwatch.Elapsed, result.Success ? "success" : OutcomeLabel(result.Outcome));
                return result;
            }
        }

        private async Task<LinkFetchResult> FollowAsync(Uri start, bool image, TimeSpan timeout, CancellationToken token)
        {
            Uri current = start;
            int redirects = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                LinkFetchResult? refused = CheckUrl(current);
                if (refused != null) return refused;
                IPAddress? pinned;
                IPAddress[] addresses;
                if (IPAddress.TryParse(current.DnsSafeHost, out IPAddress? literal))
                {
                    addresses = new[] { literal };
                }
                else
                {
                    addresses = await _Resolver.ResolveAsync(current.IdnHost, token).ConfigureAwait(false) ?? Array.Empty<IPAddress>();
                }
                if (addresses.Length == 0) return LinkFetchResult.Fail(LinkFetchOutcomeEnum.Error, current, "dns");
                if (addresses.Any(a => !_Policy.IsAllowedAddress(a))) return LinkFetchResult.Fail(LinkFetchOutcomeEnum.BlockedAddress, current, "address");
                pinned = addresses[0].IsIPv4MappedToIPv6 ? addresses[0].MapToIPv4() : addresses[0];

                using (SocketsHttpHandler handler = CreateHandler(pinned, timeout))
                using (HttpClient client = new HttpClient(handler, false) { Timeout = System.Threading.Timeout.InfiniteTimeSpan })
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, current))
                {
                    request.Headers.TryAddWithoutValidation("User-Agent", String.IsNullOrWhiteSpace(_Settings.UserAgent) ? "TrellisKitLinkPreview/1.0" : _Settings.UserAgent);
                    request.Headers.TryAddWithoutValidation("Accept", image ? "image/jpeg, image/png, image/webp" : "text/html, application/xhtml+xml;q=0.9");
                    using (HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                    {
                        int status = (int)response.StatusCode;
                        if (status == 301 || status == 302 || status == 303 || status == 307 || status == 308)
                        {
                            Uri? location = response.Headers.Location;
                            if (location == null) return LinkFetchResult.Fail(LinkFetchOutcomeEnum.TooManyRedirects, current, "redirect without location");
                            if (redirects >= Math.Max(0, _Settings.MaxRedirects)) return LinkFetchResult.Fail(LinkFetchOutcomeEnum.TooManyRedirects, current, "redirect limit");
                            Uri next = location.IsAbsoluteUri ? location : new Uri(current, location);
                            redirects++;
                            current = next;
                            continue;
                        }
                        string? contentType = response.Content.Headers.ContentType?.ToString();
                        if (status < 200 || status > 299)
                        {
                            return new LinkFetchResult { Outcome = LinkFetchOutcomeEnum.HttpError, FinalUrl = current, StatusCode = status, ContentType = contentType, Detail = "status " + status };
                        }
                        if (!image)
                        {
                            string? mediaType = response.Content.Headers.ContentType?.MediaType;
                            if (!String.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase))
                            {
                                return new LinkFetchResult { Outcome = LinkFetchOutcomeEnum.NotHtml, FinalUrl = current, StatusCode = status, ContentType = contentType };
                            }
                        }
                        int max = image ? _Settings.MaxImageBytes : _Settings.MaxHtmlBytes;
                        long? declared = response.Content.Headers.ContentLength;
                        if (declared != null && declared.Value > max)
                        {
                            return new LinkFetchResult { Outcome = LinkFetchOutcomeEnum.TooLarge, FinalUrl = current, StatusCode = status, ContentType = contentType };
                        }
                        byte[]? body = await ReadLimitedAsync(response, max, token).ConfigureAwait(false);
                        if (body == null)
                        {
                            return new LinkFetchResult { Outcome = LinkFetchOutcomeEnum.TooLarge, FinalUrl = current, StatusCode = status, ContentType = contentType };
                        }
                        if (image && !IsAcceptedImage(body))
                        {
                            return new LinkFetchResult { Outcome = LinkFetchOutcomeEnum.UnsupportedImage, FinalUrl = current, StatusCode = status, ContentType = contentType };
                        }
                        return new LinkFetchResult { Outcome = LinkFetchOutcomeEnum.Fetched, FinalUrl = current, StatusCode = status, ContentType = contentType, Body = body };
                    }
                }
            }
        }

        private LinkFetchResult? CheckUrl(Uri url)
        {
            if (!url.IsAbsoluteUri) return LinkFetchResult.Fail(LinkFetchOutcomeEnum.BlockedUrl, url, "relative");
            if (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps) return LinkFetchResult.Fail(LinkFetchOutcomeEnum.BlockedUrl, url, "scheme");
            if (!String.IsNullOrEmpty(url.UserInfo)) return LinkFetchResult.Fail(LinkFetchOutcomeEnum.BlockedUrl, url, "userinfo");
            if (!_Policy.IsAllowedPort(url.Port)) return LinkFetchResult.Fail(LinkFetchOutcomeEnum.BlockedUrl, url, "port");
            if (String.IsNullOrEmpty(url.IdnHost)) return LinkFetchResult.Fail(LinkFetchOutcomeEnum.BlockedUrl, url, "host");
            if (IsBlockedDomain(url.IdnHost, _Settings.BlockedDomains)) return LinkFetchResult.Fail(LinkFetchOutcomeEnum.BlockedDomain, url, "domain");
            return null;
        }

        private static SocketsHttpHandler CreateHandler(IPAddress pinned, TimeSpan timeout)
        {
            return new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                UseProxy = false,
                ConnectTimeout = timeout,
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromSeconds(1),
                ConnectCallback = async (context, ct) =>
                {
                    Socket socket = new Socket(pinned.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(pinned, context.DnsEndPoint.Port), ct).ConfigureAwait(false);
                        return new NetworkStream(socket, true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }
            };
        }

        private static async Task<byte[]?> ReadLimitedAsync(HttpResponseMessage response, int max, CancellationToken token)
        {
            using (Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            using (MemoryStream buffer = new MemoryStream())
            {
                byte[] chunk = new byte[16384];
                while (true)
                {
                    int read = await stream.ReadAsync(chunk, 0, chunk.Length, token).ConfigureAwait(false);
                    if (read == 0) break;
                    if (buffer.Length + read > max) return null;
                    buffer.Write(chunk, 0, read);
                }
                return buffer.ToArray();
            }
        }

        private static string OutcomeLabel(LinkFetchOutcomeEnum outcome)
        {
            switch (outcome)
            {
                case LinkFetchOutcomeEnum.BlockedAddress: return "blocked_address";
                case LinkFetchOutcomeEnum.BlockedDomain: return "blocked_domain";
                case LinkFetchOutcomeEnum.BlockedUrl: return "blocked_url";
                case LinkFetchOutcomeEnum.Timeout: return "timeout";
                case LinkFetchOutcomeEnum.TooLarge: return "too_large";
                case LinkFetchOutcomeEnum.NotHtml: return "not_html";
                case LinkFetchOutcomeEnum.UnsupportedImage: return "unsupported_image";
                case LinkFetchOutcomeEnum.TooManyRedirects: return "too_many_redirects";
                case LinkFetchOutcomeEnum.HttpError: return "http_error";
                default: return "error";
            }
        }

        #endregion
    }
}
