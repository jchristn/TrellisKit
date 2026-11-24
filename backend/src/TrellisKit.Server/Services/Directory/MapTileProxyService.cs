namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Observability;
    using TrellisKit.Server.Settings;

    /// <summary>
    /// The map tile proxy (docs/PHASE4_CONTRACT.md section 3.6): with directory.map.tileProxy on, GET
    /// /api/v1/directory/tiles/{*path} fetches the path from the one configured tile origin (directory.map.tileBaseUrl,
    /// else the origin of the style URL), adds the secret key server-side, rewrites style and TileJSON documents so their
    /// URLs point back at the proxy (and never carry the key), and caches answers in memory. Outbound connections pass
    /// the address policy. Thread safe.
    /// </summary>
    public class MapTileProxyService : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Outbound address policy for the tile origin. Default <see cref="PublicAddressPolicy"/>.
        /// </summary>
        public ILinkPreviewAddressPolicy AddressPolicy
        {
            get { return _AddressPolicy; }
            set
            {
                _AddressPolicy = value ?? throw new ArgumentNullException(nameof(AddressPolicy));
                Invalidate();
            }
        }

        /// <summary>
        /// DNS resolver. Default <see cref="SystemDnsResolver"/>.
        /// </summary>
        public IDnsResolver Resolver
        {
            get { return _Resolver; }
            set
            {
                _Resolver = value ?? throw new ArgumentNullException(nameof(Resolver));
                Invalidate();
            }
        }

        /// <summary>
        /// Cache-Control of proxied answers.
        /// </summary>
        public const string CacheControl = "public, max-age=86400";

        /// <summary>
        /// Longest proxied answer in bytes (larger answers are refused).
        /// </summary>
        public const int MaxBytes = 10 * 1024 * 1024;

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private readonly ConcurrentDictionary<string, TileProxyResponse> _Cache = new ConcurrentDictionary<string, TileProxyResponse>(StringComparer.Ordinal);
        private readonly ConcurrentQueue<string> _Order = new ConcurrentQueue<string>();
        private readonly object _Lock = new object();
        private ILinkPreviewAddressPolicy _AddressPolicy = new PublicAddressPolicy();
        private IDnsResolver _Resolver = new SystemDnsResolver();
        private HttpClient? _Http = null;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public MapTileProxyService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Proxy one request.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="path">Path below the tile origin.</param>
        /// <param name="query">Query string without the leading question mark, or null.</param>
        /// <param name="proxyBase">Absolute URL of the proxy route (for rewriting documents).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Answer.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound (proxy off), 429 RateLimited, 502 TileUnavailable, 503 IntegrationNotConfigured.</exception>
        public async Task<TileProxyResponse> FetchAsync(ContentActor actor, string path, string? query, string proxyBase, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (!await _Services.Directory.Config.FlagAsync(DirectoryPropertySettings.MapTileProxy, token).ConfigureAwait(false)) throw ApiErrorException.NotFound("Tile proxy");
            int perMinute = _Services.Settings.Directory.TilesPerMinute;
            if (actor.IsAnonymous || String.IsNullOrEmpty(actor.UserId)) _Services.Limits.EnforceIp(actor.SourceIp, "directory-tiles", Math.Max(1, perMinute / 2), RateLimitService.Minute);
            else _Services.Limits.EnforceMember(actor, "directory-tiles", perMinute, RateLimitService.Minute);
            Uri origin = await OriginAsync(token).ConfigureAwait(false);
            string key = await _Services.Directory.Config.SettingAsync(DirectoryPropertySettings.MapProxyKey, token).ConfigureAwait(false);
            string parameter = await _Services.Directory.Config.SettingAsync(DirectoryPropertySettings.MapProxyKeyParameter, token).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(parameter)) parameter = "key";
            string clean = CleanPath(path);
            string cleanQuery = CleanQuery(query, parameter);
            string cacheKey = clean + "?" + cleanQuery;
            if (_Cache.TryGetValue(cacheKey, out TileProxyResponse? cached) && cached.ExpiresUtc > DateTime.UtcNow)
            {
                TrellisKitTelemetry.Add(DirectoryTelemetry.Tiles, 1, "outcome", "cache_hit");
                return new TileProxyResponse { StatusCode = 200, ContentType = cached.ContentType, Body = cached.Body, FromCache = true, ExpiresUtc = cached.ExpiresUtc };
            }
            string basePath = origin.AbsolutePath.TrimEnd('/');
            string url = origin.GetLeftPart(UriPartial.Authority) + basePath + "/" + clean + "?" + cleanQuery + (cleanQuery.Length == 0 ? String.Empty : "&") +
                Uri.EscapeDataString(parameter) + "=" + Uri.EscapeDataString(key ?? String.Empty);
            HttpClient http = Client();
            try
            {
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
                using (HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        TrellisKitTelemetry.Add(DirectoryTelemetry.Tiles, 1, "outcome", "error");
                        int status = (int)response.StatusCode == 404 ? 404 : 502;
                        throw new ApiErrorException(status, status == 404 ? "NotFound" : "TileUnavailable", status == 404 ? "Tile not found." : "The tile provider did not answer.");
                    }
                    if (response.Content.Headers.ContentLength > MaxBytes) throw new ApiErrorException(502, "TileUnavailable", "The tile is too large.");
                    byte[] body = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
                    if (body.Length > MaxBytes) throw new ApiErrorException(502, "TileUnavailable", "The tile is too large.");
                    string type = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
                    if (type.Contains("json", StringComparison.OrdinalIgnoreCase)) body = Rewrite(body, origin, proxyBase, key, parameter);
                    TileProxyResponse answer = new TileProxyResponse { StatusCode = 200, ContentType = type, Body = body, ExpiresUtc = DateTime.UtcNow.AddHours(24) };
                    Remember(cacheKey, answer);
                    TrellisKitTelemetry.Add(DirectoryTelemetry.Tiles, 1, "outcome", "proxied");
                    return answer;
                }
            }
            catch (HttpRequestException)
            {
                TrellisKitTelemetry.Add(DirectoryTelemetry.Tiles, 1, "outcome", "error");
                throw new ApiErrorException(502, "TileUnavailable", "The tile provider did not answer.");
            }
            catch (TaskCanceledException) when (!token.IsCancellationRequested)
            {
                TrellisKitTelemetry.Add(DirectoryTelemetry.Tiles, 1, "outcome", "error");
                throw new ApiErrorException(502, "TileUnavailable", "The tile provider timed out.");
            }
        }

        /// <summary>
        /// Drop the cache and the HTTP client (settings changed).
        /// </summary>
        public void Invalidate()
        {
            lock (_Lock)
            {
                _Cache.Clear();
                while (_Order.TryDequeue(out string? _))
                {
                }
                _Http?.Dispose();
                _Http = null;
            }
        }

        /// <summary>
        /// Release the HTTP client.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Dispose pattern.
        /// </summary>
        /// <param name="disposing">Whether called from Dispose.</param>
        protected virtual void Dispose(bool disposing)
        {
            lock (_Lock)
            {
                if (_Disposed) return;
                _Disposed = true;
                if (disposing) _Http?.Dispose();
                _Http = null;
            }
        }

        private HttpClient Client()
        {
            lock (_Lock)
            {
                ObjectDisposedException.ThrowIf(_Disposed, this);
                TimeSpan timeout = TimeSpan.FromSeconds(_Services.Settings.Directory.TileTimeoutSeconds);
                _Http ??= new HttpClient(PolicyHttpHandlerFactory.Create(_AddressPolicy, _Resolver, timeout), true) { Timeout = timeout };
                return _Http;
            }
        }

        private async Task<Uri> OriginAsync(CancellationToken token)
        {
            string configured = await _Services.Directory.Config.SettingAsync(DirectoryPropertySettings.MapTileBaseUrl, token).ConfigureAwait(false);
            string style = await _Services.Directory.Config.SettingAsync(DirectoryPropertySettings.MapStyleUrl, token).ConfigureAwait(false);
            string key = await _Services.Directory.Config.SettingAsync(DirectoryPropertySettings.MapProxyKey, token).ConfigureAwait(false);
            Uri? origin = null;
            if (!String.IsNullOrWhiteSpace(configured) && Uri.TryCreate(configured.Trim(), UriKind.Absolute, out Uri? explicitOrigin)) origin = explicitOrigin;
            else if (!String.IsNullOrWhiteSpace(style) && Uri.TryCreate(style.Trim(), UriKind.Absolute, out Uri? styleUri)) origin = new Uri(styleUri.GetLeftPart(UriPartial.Authority));
            if (origin == null || (origin.Scheme != Uri.UriSchemeHttps && origin.Scheme != Uri.UriSchemeHttp) || String.IsNullOrWhiteSpace(key))
                throw new ApiErrorException(503, "IntegrationNotConfigured", "The tile proxy needs directory.map.styleUrl (or directory.map.tileBaseUrl) and directory.map.proxyKey.");
            return origin;
        }

        private static string CleanPath(string path)
        {
            string p = Uri.UnescapeDataString(path ?? String.Empty).Replace('\\', '/').TrimStart('/');
            if (p.Length == 0 || p.Length > 500 || p.Contains("..", StringComparison.Ordinal) || p.Contains("://", StringComparison.Ordinal) || p.Contains('@') || p.Any(c => Char.IsControl(c) || c == '#' || c == '?'))
                throw ApiErrorException.Field("path", "Not a tile path.");
            return String.Join("/", p.Split('/').Select(Uri.EscapeDataString));
        }

        private static string CleanQuery(string? query, string keyParameter)
        {
            if (String.IsNullOrWhiteSpace(query)) return String.Empty;
            string q = query.TrimStart('?');
            if (q.Length > 500) throw ApiErrorException.Field("query", "Too long.");
            return String.Join("&", q.Split('&', StringSplitOptions.RemoveEmptyEntries).Where(pair =>
            {
                string name = Uri.UnescapeDataString(pair.Split('=')[0]);
                return !String.Equals(name, keyParameter, StringComparison.OrdinalIgnoreCase) && !String.Equals(name, "key", StringComparison.OrdinalIgnoreCase) &&
                    !String.Equals(name, "api_key", StringComparison.OrdinalIgnoreCase) && !String.Equals(name, "access_token", StringComparison.OrdinalIgnoreCase);
            }));
        }

        private static byte[] Rewrite(byte[] body, Uri origin, string proxyBase, string? key, string parameter)
        {
            string text = Encoding.UTF8.GetString(body);
            string root = origin.GetLeftPart(UriPartial.Authority) + origin.AbsolutePath.TrimEnd('/');
            text = text.Replace(root, proxyBase.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
            if (!String.IsNullOrEmpty(key))
            {
                text = text.Replace(parameter + "=" + Uri.EscapeDataString(key), String.Empty, StringComparison.Ordinal);
                text = text.Replace(parameter + "=" + key, String.Empty, StringComparison.Ordinal);
                text = text.Replace(key, String.Empty, StringComparison.Ordinal);
            }
            return Encoding.UTF8.GetBytes(text);
        }

        private void Remember(string cacheKey, TileProxyResponse answer)
        {
            int max = _Services.Settings.Directory.TileCacheEntries;
            if (max <= 0) return;
            if (_Cache.TryAdd(cacheKey, answer)) _Order.Enqueue(cacheKey);
            else _Cache[cacheKey] = answer;
            while (_Cache.Count > max && _Order.TryDequeue(out string? oldest)) _Cache.TryRemove(oldest, out TileProxyResponse? _);
        }

        #endregion
    }
}
