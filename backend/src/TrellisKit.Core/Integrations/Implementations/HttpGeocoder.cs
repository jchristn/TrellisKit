namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// A geocoder for providers exposing the Pelias or Photon style GeoJSON API: GET {baseUrl}/search?text=...&amp;size=N,
    /// answering a FeatureCollection whose features carry geometry.coordinates [lng, lat], properties.label (Pelias) or
    /// properties.name with locality parts (Photon), and an optional bbox or properties.extent. The key is sent in the
    /// configured header, or as api_key when no header name is set. Thread safe. The geocoder owns the HTTP client and disposes it.
    /// </summary>
    public sealed class HttpGeocoder : IGeocoder, IDisposable
    {
        #region Public-Members

        /// <inheritdoc />
        public string Provider
        {
            get { return "Http"; }
        }

        /// <inheritdoc />
        public bool IsConfigured
        {
            get { return _BaseUrl != null; }
        }

        #endregion

        #region Private-Members

        private readonly HttpClient _Http;
        private readonly Uri? _BaseUrl;
        private readonly string? _ApiKey;
        private readonly string? _AuthHeaderName;
        private readonly string _UserAgent;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="http">HTTP client (the caller applies the address policy; disposed with the geocoder).</param>
        /// <param name="baseUrl">Base URL, or null when not configured.</param>
        /// <param name="apiKey">Key, or null.</param>
        /// <param name="authHeaderName">Header that carries the key, or null for the api_key parameter.</param>
        /// <param name="engineVersion">Engine version for the User-Agent.</param>
        /// <exception cref="ArgumentNullException">The client is null.</exception>
        public HttpGeocoder(HttpClient http, Uri? baseUrl, string? apiKey, string? authHeaderName, string engineVersion)
        {
            _Http = http ?? throw new ArgumentNullException(nameof(http));
            _BaseUrl = baseUrl;
            _ApiKey = String.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
            _AuthHeaderName = String.IsNullOrWhiteSpace(authHeaderName) ? null : authHeaderName.Trim();
            _UserAgent = "TrellisKit-Geocoder/" + (String.IsNullOrWhiteSpace(engineVersion) ? "0" : engineVersion.Trim());
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<List<GeocodeCandidate>> SearchAsync(string query, int limit, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(query)) throw new ArgumentNullException(nameof(query));
            if (_BaseUrl == null) throw new IntegrationNotConfiguredException("The Http geocoder has no base URL (directory.geocoder.baseUrl).");
            string url = _BaseUrl.ToString().TrimEnd('/') + "/search?text=" + Uri.EscapeDataString(query.Trim()) + "&size=" + Math.Clamp(limit, 1, 10).ToString(CultureInfo.InvariantCulture) +
                (_ApiKey != null && _AuthHeaderName == null ? "&api_key=" + Uri.EscapeDataString(_ApiKey) : String.Empty);
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.TryAddWithoutValidation("User-Agent", _UserAgent);
                request.Headers.TryAddWithoutValidation("Accept", "application/json");
                if (_ApiKey != null && _AuthHeaderName != null) request.Headers.TryAddWithoutValidation(_AuthHeaderName, _ApiKey);
                using (HttpResponseMessage response = await _Http.SendAsync(request, token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) throw new HttpRequestException("The geocoder answered " + (int)response.StatusCode + ".");
                    string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    return Parse(body).Take(Math.Clamp(limit, 1, 10)).ToList();
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

        private static List<GeocodeCandidate> Parse(string body)
        {
            List<GeocodeCandidate> result = new List<GeocodeCandidate>();
            using (JsonDocument doc = JsonDocument.Parse(body))
            {
                if (!doc.RootElement.TryGetProperty("features", out JsonElement features) || features.ValueKind != JsonValueKind.Array) return result;
                foreach (JsonElement feature in features.EnumerateArray())
                {
                    if (!feature.TryGetProperty("geometry", out JsonElement geometry) || !geometry.TryGetProperty("coordinates", out JsonElement coordinates) ||
                        coordinates.ValueKind != JsonValueKind.Array || coordinates.GetArrayLength() < 2) continue;
                    if (coordinates[0].ValueKind != JsonValueKind.Number || coordinates[1].ValueKind != JsonValueKind.Number) continue;
                    GeocodeCandidate candidate = new GeocodeCandidate { Lng = coordinates[0].GetDouble(), Lat = coordinates[1].GetDouble() };
                    JsonElement properties = feature.TryGetProperty("properties", out JsonElement p) && p.ValueKind == JsonValueKind.Object ? p : default;
                    candidate.Label = Label(properties);
                    if (feature.TryGetProperty("bbox", out JsonElement bbox) && bbox.ValueKind == JsonValueKind.Array && bbox.GetArrayLength() == 4)
                    {
                        candidate.West = bbox[0].GetDouble();
                        candidate.South = bbox[1].GetDouble();
                        candidate.East = bbox[2].GetDouble();
                        candidate.North = bbox[3].GetDouble();
                    }
                    else if (properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty("extent", out JsonElement extent) && extent.ValueKind == JsonValueKind.Array && extent.GetArrayLength() == 4)
                    {
                        candidate.West = extent[0].GetDouble();
                        candidate.North = extent[1].GetDouble();
                        candidate.East = extent[2].GetDouble();
                        candidate.South = extent[3].GetDouble();
                    }
                    if (properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty("timezone", out JsonElement zone) && zone.ValueKind == JsonValueKind.String) candidate.TimeZone = zone.GetString();
                    result.Add(candidate);
                }
            }
            return result;
        }

        private static string Label(JsonElement properties)
        {
            if (properties.ValueKind != JsonValueKind.Object) return String.Empty;
            if (properties.TryGetProperty("label", out JsonElement label) && label.ValueKind == JsonValueKind.String) return label.GetString() ?? String.Empty;
            List<string> parts = new List<string>();
            foreach (string name in new[] { "name", "street", "city", "state", "country" })
            {
                if (properties.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && !String.IsNullOrWhiteSpace(value.GetString())) parts.Add(value.GetString()!);
            }
            return String.Join(", ", parts.Distinct(StringComparer.Ordinal));
        }

        #endregion
    }
}
