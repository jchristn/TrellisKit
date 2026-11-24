namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Settings;

    /// <summary>
    /// Geocoding of venue addresses and place search (docs/PHASE4_CONTRACT.md section 3.5) over the property's selected
    /// <see cref="IGeocoder"/>. Configured base URLs must pass the outbound address policy (a base URL that resolves to a
    /// private or loopback address leaves the geocoder not configured); answers are cached in geocoderesults by the
    /// SHA-256 of the normalized address for directory.geocoder.cacheDays. Providers receive only the address or the
    /// typed place text. Thread safe.
    /// </summary>
    public class GeocodingService : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Outbound address policy for the Nominatim and Http base URLs. Default <see cref="PublicAddressPolicy"/>
        /// (public internet addresses, ports 80 and 443); tests may substitute a loopback policy.
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
        /// DNS resolver used by the address policy. Default <see cref="SystemDnsResolver"/>.
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
        /// Most place search candidates (fixed bound of the contract).
        /// </summary>
        public const int MaxCandidates = 5;

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private readonly string _EngineVersion;
        private readonly object _Lock = new object();
        private ILinkPreviewAddressPolicy _AddressPolicy = new PublicAddressPolicy();
        private IDnsResolver _Resolver = new SystemDnsResolver();
        private IGeocoder? _Geocoder = null;
        private string? _Signature = null;
        private DateTime _BuiltUtc = DateTime.MinValue;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public GeocodingService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            Version? version = typeof(GeocodingService).Assembly.GetName().Version;
            _EngineVersion = version == null ? "0.0.0" : version.ToString(3);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether the selected geocoder is configured and allowed.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when configured.</returns>
        public async Task<bool> IsConfiguredAsync(CancellationToken token = default)
        {
            IGeocoder geocoder = await GeocoderAsync(token).ConfigureAwait(false);
            return geocoder.IsConfigured;
        }

        /// <summary>
        /// The selected geocoder (rebuilt when the settings change and every 10 minutes, so DNS is re-checked).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Geocoder (a <see cref="NoneGeocoder"/> when none is usable).</returns>
        public async Task<IGeocoder> GeocoderAsync(CancellationToken token = default)
        {
            string provider = await Setting(DirectoryPropertySettings.GeocoderProvider, token).ConfigureAwait(false);
            string baseUrl = await Setting(DirectoryPropertySettings.GeocoderBaseUrl, token).ConfigureAwait(false);
            string apiKey = await Setting(DirectoryPropertySettings.GeocoderApiKey, token).ConfigureAwait(false);
            string header = await Setting(DirectoryPropertySettings.GeocoderAuthHeaderName, token).ConfigureAwait(false);
            string email = await Setting(DirectoryPropertySettings.GeocoderEmail, token).ConfigureAwait(false);
            string index = await Setting(DirectoryPropertySettings.GeocoderPlaceIndexName, token).ConfigureAwait(false);
            string accessKeyId = await Setting(DirectoryPropertySettings.GeocoderAccessKeyId, token).ConfigureAwait(false);
            string region = await Setting(DirectoryPropertySettings.GeocoderRegion, token).ConfigureAwait(false);
            string rps = await Setting(DirectoryPropertySettings.GeocoderRequestsPerSecond, token).ConfigureAwait(false);
            string signature = String.Join("|", provider, baseUrl, Hash(apiKey), header, email, index, region, accessKeyId, rps);
            lock (_Lock)
            {
                if (_Geocoder != null && _Signature == signature && DateTime.UtcNow - _BuiltUtc < TimeSpan.FromMinutes(10)) return _Geocoder;
            }
            IGeocoder built = await BuildAsync(provider, baseUrl, apiKey, header, email, index, region, accessKeyId, rps, token).ConfigureAwait(false);
            lock (_Lock)
            {
                DisposeCurrent();
                _Geocoder = built;
                _Signature = signature;
                _BuiltUtc = DateTime.UtcNow;
                return built;
            }
        }

        /// <summary>
        /// Drop the cached geocoder (settings changed).
        /// </summary>
        public void Invalidate()
        {
            lock (_Lock)
            {
                _Signature = null;
            }
        }

        /// <summary>
        /// Geocode a venue address with the cache. Returns null when the geocoder is not configured, nothing matched, or
        /// the provider failed (the caller leaves geo.source None).
        /// </summary>
        /// <param name="venue">Venue (only its address is sent).</param>
        /// <param name="purpose">Telemetry purpose (write).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Best candidate, or null.</returns>
        /// <exception cref="ArgumentNullException">The venue is null.</exception>
        public async Task<GeocodeCandidate?> GeocodeVenueAsync(Venue venue, string purpose, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(venue);
            string? query = AddressQuery(venue.Line1, venue.Line2, venue.Locality, venue.Region, venue.PostalCode, venue.CountryCode);
            if (query == null) return null;
            return await GeocodeAsync(query, purpose, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Geocode one query with the cache.
        /// </summary>
        /// <param name="query">Address text.</param>
        /// <param name="purpose">Telemetry purpose.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Best candidate, or null.</returns>
        public async Task<GeocodeCandidate?> GeocodeAsync(string query, string purpose, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(query)) return null;
            IGeocoder geocoder = await GeocoderAsync(token).ConfigureAwait(false);
            if (!geocoder.IsConfigured) return null;
            string hash = QueryHash(query);
            DateTime now = DateTime.UtcNow;
            GeocodeCacheEntry? cached = await _Services.Database.DirectoryListings.GeocodeCache.ReadAsync(geocoder.Provider, hash, now, token).ConfigureAwait(false);
            if (cached != null)
            {
                TrellisKitTelemetry.Add(DirectoryTelemetry.Geocode, 1, "provider", geocoder.Provider, "outcome", "cache_hit", "purpose", purpose);
                if (!cached.Found || cached.Lat == null || cached.Lng == null) return null;
                return new GeocodeCandidate { Lat = cached.Lat.Value, Lng = cached.Lng.Value, Label = cached.Formatted ?? String.Empty, TimeZone = cached.TimeZone };
            }
            List<GeocodeCandidate> candidates;
            Stopwatch watch = Stopwatch.StartNew();
            using (Activity? activity = TrellisKitTelemetry.StartSpan("directory.geocode"))
            {
                activity?.SetTag("geocode.provider", geocoder.Provider);
                try
                {
                    candidates = await geocoder.SearchAsync(query, 1, token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException || !token.IsCancellationRequested)
                {
                    TrellisKitTelemetry.Add(DirectoryTelemetry.Geocode, 1, "provider", geocoder.Provider, "outcome", "error", "purpose", purpose);
                    TrellisKitTelemetry.Record(DirectoryTelemetry.GeocodeDuration, watch.Elapsed.TotalSeconds, "provider", geocoder.Provider);
                    _Services.Logger.Warn("[GeocodingService] [p:" + _Services.PropertyId + "] " + geocoder.Provider + " geocode failed: " + e.GetType().Name);
                    return null;
                }
            }
            TrellisKitTelemetry.Record(DirectoryTelemetry.GeocodeDuration, watch.Elapsed.TotalSeconds, "provider", geocoder.Provider);
            GeocodeCandidate? best = candidates.FirstOrDefault(c => c.Lat >= -90 && c.Lat <= 90 && c.Lng >= -180 && c.Lng <= 180);
            TrellisKitTelemetry.Add(DirectoryTelemetry.Geocode, 1, "provider", geocoder.Provider, "outcome", best == null ? "miss" : "hit", "purpose", purpose);
            int days = Math.Clamp(await IntegerAsync(DirectoryPropertySettings.GeocoderCacheDays, 90, token).ConfigureAwait(false), 1, 3650);
            await _Services.Database.DirectoryListings.GeocodeCache.WriteAsync(new GeocodeCacheEntry
            {
                Id = IdGenerator.Generate(DirectoryIdPrefixes.GeocodeResult),
                QueryHash = hash,
                Provider = geocoder.Provider,
                Lat = best?.Lat,
                Lng = best?.Lng,
                TimeZone = best?.TimeZone,
                Formatted = best?.Label,
                Found = best != null,
                ExpiresUtc = now.AddDays(days),
                CreatedUtc = now
            }, token).ConfigureAwait(false);
            return best;
        }

        /// <summary>
        /// GET /api/v1/directory/geocode and POST /api/v1/directory/geocode/enumerate: place search for "near" queries. At most 5 candidates; the query text is not
        /// stored or logged (only a counter); rate limited per member.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Candidates.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 429 RateLimited, 503 IntegrationNotConfigured.</exception>
        public async Task<GeocodeSearchResponse> SearchAsync(ContentActor actor, GeocodeRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            string query = (request.Query ?? String.Empty).Trim();
            if (query.Length < 2 || query.Length > 200) throw ApiErrorException.Field("query", "2 to 200 characters.");
            IGeocoder geocoder = await GeocoderAsync(token).ConfigureAwait(false);
            if (!geocoder.IsConfigured) throw new ApiErrorException(503, "IntegrationNotConfigured", "Place search needs a configured geocoder (directory.geocoder.provider).");
            _Services.Limits.CheckMember(actor, "directory-geocode-day", _Services.Settings.Directory.GeocodePerDay, RateLimitService.Day);
            _Services.Limits.EnforceMember(actor, "directory-geocode-min", _Services.Settings.Directory.GeocodePerMinute, RateLimitService.Minute);
            _Services.Limits.EnforceMember(actor, "directory-geocode-day", _Services.Settings.Directory.GeocodePerDay, RateLimitService.Day);
            List<GeocodeCandidate> candidates;
            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                candidates = await geocoder.SearchAsync(query, MaxCandidates, token).ConfigureAwait(false);
            }
            catch (IntegrationNotConfiguredException)
            {
                throw;
            }
            catch (Exception e) when (e is not OperationCanceledException || !token.IsCancellationRequested)
            {
                TrellisKitTelemetry.Add(DirectoryTelemetry.Geocode, 1, "provider", geocoder.Provider, "outcome", "error", "purpose", "search");
                throw new ApiErrorException(502, "GeocoderUnavailable", "The place search provider did not answer; try again later.");
            }
            TrellisKitTelemetry.Record(DirectoryTelemetry.GeocodeDuration, watch.Elapsed.TotalSeconds, "provider", geocoder.Provider);
            TrellisKitTelemetry.Add(DirectoryTelemetry.Geocode, 1, "provider", geocoder.Provider, "outcome", candidates.Count == 0 ? "miss" : "hit", "purpose", "search");
            return new GeocodeSearchResponse
            {
                Candidates = candidates.Where(c => c.Lat >= -90 && c.Lat <= 90 && c.Lng >= -180 && c.Lng <= 180).Take(MaxCandidates).Select((c, rank) => new GeocodeCandidateView
                {
                    Id = Hash(c.Label + "|" + GeoMath.Round(c.Lat, 5).ToString("R", CultureInfo.InvariantCulture) + "|" + GeoMath.Round(c.Lng, 5).ToString("R", CultureInfo.InvariantCulture)),
                    Rank = rank,
                    Label = c.Label,
                    Lat = GeoMath.Round(c.Lat, 5),
                    Lng = GeoMath.Round(c.Lng, 5),
                    Bbox = c.South != null && c.West != null && c.North != null && c.East != null ? new GeoBox { South = c.South.Value, West = c.West.Value, North = c.North.Value, East = c.East.Value } : null
                }).ToList()
            };
        }

        /// <summary>
        /// The query text of an address (null when the address has nothing to geocode).
        /// </summary>
        /// <param name="line1">Line 1.</param>
        /// <param name="line2">Line 2.</param>
        /// <param name="locality">Locality.</param>
        /// <param name="region">Region.</param>
        /// <param name="postalCode">Postal code.</param>
        /// <param name="countryCode">Country code.</param>
        /// <returns>Query, or null.</returns>
        public static string? AddressQuery(string? line1, string? line2, string? locality, string? region, string? postalCode, string? countryCode)
        {
            List<string> parts = new[] { line1, line2, locality, region, postalCode, countryCode }.Where(p => !String.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).ToList();
            if (parts.Count == 0 || (String.IsNullOrWhiteSpace(line1) && String.IsNullOrWhiteSpace(locality) && String.IsNullOrWhiteSpace(postalCode))) return null;
            return String.Join(", ", parts);
        }

        /// <summary>
        /// The cache key of a query: SHA-256 of the normalized text (lowercase, single spaces, no surrounding punctuation).
        /// </summary>
        /// <param name="query">Query.</param>
        /// <returns>Hex hash.</returns>
        public static string QueryHash(string query)
        {
            StringBuilder sb = new StringBuilder();
            bool space = false;
            foreach (char ch in (query ?? String.Empty).Normalize(NormalizationForm.FormKC).ToLowerInvariant())
            {
                if (Char.IsLetterOrDigit(ch)) { if (space && sb.Length > 0) sb.Append(' '); sb.Append(ch); space = false; }
                else space = true;
            }
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
        }

        /// <summary>
        /// Release the HTTP client and provider clients.
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
                if (disposing) DisposeCurrent();
            }
        }

        private void DisposeCurrent()
        {
            if (_Geocoder is IDisposable disposable) disposable.Dispose();
            _Geocoder = null;
        }

        private async Task<IGeocoder> BuildAsync(string provider, string baseUrl, string apiKey, string header, string email, string index, string region, string accessKeyId, string rps, CancellationToken token)
        {
            TimeSpan timeout = TimeSpan.FromSeconds(_Services.Settings.Directory.GeocodeTimeoutSeconds);
            if (!Enum.TryParse(provider, true, out GeocoderProviderEnum kind) || kind == GeocoderProviderEnum.None) return new NoneGeocoder();
            if (kind == GeocoderProviderEnum.AwsLocation) return new AwsLocationGeocoder(index, region, timeout, accessKeyId, apiKey);
            Uri? url = await AllowedBaseAsync(baseUrl, token).ConfigureAwait(false);
            HttpClient http = new HttpClient(PolicyHttpHandlerFactory.Create(_AddressPolicy, _Resolver, timeout), true) { Timeout = timeout };
            if (kind == GeocoderProviderEnum.Nominatim)
            {
                NominatimGeocoder nominatim = new NominatimGeocoder(http, url, apiKey, email, _EngineVersion);
                if (Int32.TryParse(rps, NumberStyles.Integer, CultureInfo.InvariantCulture, out int perSecond)) nominatim.RequestsPerSecond = perSecond;
                return nominatim;
            }
            return new HttpGeocoder(http, url, apiKey, header, _EngineVersion);
        }

        private async Task<Uri?> AllowedBaseAsync(string baseUrl, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out Uri? uri)) return null;
            if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return null;
            try
            {
                await PolicyHttpHandlerFactory.AllowedAddressAsync(_AddressPolicy, _Resolver, uri.DnsSafeHost, uri.Port, token).ConfigureAwait(false);
                return uri;
            }
            catch (Exception e) when (e is HttpRequestException || e is System.Net.Sockets.SocketException)
            {
                _Services.Logger.Warn("[GeocodingService] [p:" + _Services.PropertyId + "] geocoder base URL refused by the outbound address policy: " + e.Message);
                return null;
            }
        }

        private Task<string> Setting(string key, CancellationToken token)
        {
            return _Services.PropertySettings.GetOrDefaultAsync(key, token);
        }

        private async Task<int> IntegerAsync(string key, int fallback, CancellationToken token)
        {
            string text = await Setting(key, token).ConfigureAwait(false);
            return Int32.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;
        }

        private static string Hash(string value)
        {
            return String.IsNullOrEmpty(value) ? String.Empty : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).Substring(0, 12);
        }

        #endregion
    }
}
