namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// A Nominatim compatible geocoder (self-hosted or a hosted compatible endpoint): GET {baseUrl}/search?q=...&amp;format=jsonv2.
    /// Sends the generic User-Agent TrellisKit-Geocoder/&lt;engine version&gt;, the optional email contact parameter and
    /// key, and never any member data. Calls are throttled to <see cref="RequestsPerSecond"/>. Thread safe. The geocoder owns the HTTP client and disposes it.
    /// </summary>
    public sealed class NominatimGeocoder : IGeocoder, IDisposable
    {
        #region Public-Members

        /// <inheritdoc />
        public string Provider
        {
            get { return "Nominatim"; }
        }

        /// <inheritdoc />
        public bool IsConfigured
        {
            get { return _BaseUrl != null; }
        }

        /// <summary>
        /// Most requests per second. Default 1. Minimum 1, maximum 50.
        /// </summary>
        public int RequestsPerSecond
        {
            get { return _RequestsPerSecond; }
            set { _RequestsPerSecond = Math.Clamp(value, 1, 50); }
        }

        #endregion

        #region Private-Members

        private readonly HttpClient _Http;
        private readonly Uri? _BaseUrl;
        private readonly string? _ApiKey;
        private readonly string? _Email;
        private readonly string _UserAgent;
        private readonly SemaphoreSlim _Gate = new SemaphoreSlim(1, 1);
        private DateTime _NextUtc = DateTime.MinValue;
        private int _RequestsPerSecond = 1;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="http">HTTP client (the caller applies the address policy; disposed with the geocoder).</param>
        /// <param name="baseUrl">Base URL, or null when not configured.</param>
        /// <param name="apiKey">Optional key sent as the key parameter, or null.</param>
        /// <param name="email">Optional contact email parameter, or null.</param>
        /// <param name="engineVersion">Engine version for the User-Agent.</param>
        /// <exception cref="ArgumentNullException">The client is null.</exception>
        public NominatimGeocoder(HttpClient http, Uri? baseUrl, string? apiKey, string? email, string engineVersion)
        {
            _Http = http ?? throw new ArgumentNullException(nameof(http));
            _BaseUrl = baseUrl;
            _ApiKey = String.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
            _Email = String.IsNullOrWhiteSpace(email) ? null : email.Trim();
            _UserAgent = "TrellisKit-Geocoder/" + (String.IsNullOrWhiteSpace(engineVersion) ? "0" : engineVersion.Trim());
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<List<GeocodeCandidate>> SearchAsync(string query, int limit, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(query)) throw new ArgumentNullException(nameof(query));
            if (_BaseUrl == null) throw new IntegrationNotConfiguredException("The Nominatim geocoder has no base URL (directory.geocoder.baseUrl).");
            await ThrottleAsync(token).ConfigureAwait(false);
            string url = _BaseUrl.ToString().TrimEnd('/') + "/search?format=jsonv2&addressdetails=0&limit=" + Math.Clamp(limit, 1, 10).ToString(CultureInfo.InvariantCulture) +
                "&q=" + Uri.EscapeDataString(query.Trim()) + (_Email == null ? String.Empty : "&email=" + Uri.EscapeDataString(_Email)) + (_ApiKey == null ? String.Empty : "&key=" + Uri.EscapeDataString(_ApiKey));
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.TryAddWithoutValidation("User-Agent", _UserAgent);
                request.Headers.TryAddWithoutValidation("Accept", "application/json");
                using (HttpResponseMessage response = await _Http.SendAsync(request, token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) throw new HttpRequestException("The geocoder answered " + (int)response.StatusCode + ".");
                    string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    return Parse(body);
                }
            }
        }

        #endregion

        #region Disposal

        /// <summary>
        /// Dispose the HTTP client.
        /// </summary>
        public void Dispose()
        {
            _Http.Dispose();
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        private async Task ThrottleAsync(CancellationToken token)
        {
            await _Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                DateTime now = DateTime.UtcNow;
                if (_NextUtc > now) await Task.Delay(_NextUtc - now, token).ConfigureAwait(false);
                _NextUtc = DateTime.UtcNow.AddMilliseconds(1000.0 / _RequestsPerSecond);
            }
            finally
            {
                _Gate.Release();
            }
        }

        private static List<GeocodeCandidate> Parse(string body)
        {
            List<GeocodeCandidate> result = new List<GeocodeCandidate>();
            using (JsonDocument doc = JsonDocument.Parse(body))
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
                foreach (JsonElement item in doc.RootElement.EnumerateArray())
                {
                    double? lat = Number(item, "lat");
                    double? lng = Number(item, "lon");
                    if (lat == null || lng == null) continue;
                    GeocodeCandidate candidate = new GeocodeCandidate
                    {
                        Label = item.TryGetProperty("display_name", out JsonElement name) && name.ValueKind == JsonValueKind.String ? name.GetString() ?? String.Empty : String.Empty,
                        Lat = lat.Value,
                        Lng = lng.Value
                    };
                    if (item.TryGetProperty("boundingbox", out JsonElement box) && box.ValueKind == JsonValueKind.Array && box.GetArrayLength() == 4)
                    {
                        candidate.South = Element(box[0]);
                        candidate.North = Element(box[1]);
                        candidate.West = Element(box[2]);
                        candidate.East = Element(box[3]);
                    }
                    result.Add(candidate);
                }
            }
            return result;
        }

        private static double? Number(JsonElement item, string name)
        {
            return item.TryGetProperty(name, out JsonElement value) ? Element(value) : null;
        }

        private static double? Element(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Number) return value.GetDouble();
            if (value.ValueKind == JsonValueKind.String && Double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)) return parsed;
            return null;
        }

        #endregion
    }
}
