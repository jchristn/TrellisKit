namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Fetches and caches RSA keys from a JWKS endpoint. Keys are refetched when the cache is older than
    /// <see cref="CacheDuration"/>, or when a token names an unknown kid, but never more often than
    /// <see cref="MinimumRefetchInterval"/>. When a refetch fails, the previous keys stay in use.
    /// Thread safe.
    /// </summary>
    internal sealed class JwksCache
    {
        #region Internal-Members

        /// <summary>
        /// How long fetched keys are used before a background refresh is attempted. Default 1 hour. Minimum 1 minute, maximum 24 hours.
        /// </summary>
        internal TimeSpan CacheDuration
        {
            get { return _CacheDuration; }
            set { _CacheDuration = Clamp(value, TimeSpan.FromMinutes(1), TimeSpan.FromHours(24)); }
        }

        /// <summary>
        /// Minimum time between fetch attempts, which bounds the load a stream of tokens with unknown kids can cause.
        /// Default 60 seconds. Minimum 0 (always allowed), maximum 1 hour.
        /// </summary>
        internal TimeSpan MinimumRefetchInterval
        {
            get { return _MinimumRefetchInterval; }
            set { _MinimumRefetchInterval = Clamp(value, TimeSpan.Zero, TimeSpan.FromHours(1)); }
        }

        #endregion

        #region Private-Members

        private readonly HttpClient _HttpClient;
        private readonly string _Url;
        private readonly SemaphoreSlim _FetchLock = new SemaphoreSlim(1, 1);
        private TimeSpan _CacheDuration = TimeSpan.FromHours(1);
        private TimeSpan _MinimumRefetchInterval = TimeSpan.FromSeconds(60);
        private volatile JwksKeySet? _KeySet = null;
        private DateTime _LastAttemptUtc = DateTime.MinValue;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the cache.
        /// </summary>
        /// <param name="httpClient">HTTP client.</param>
        /// <param name="url">JWKS URL.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        internal JwksCache(HttpClient httpClient, string url)
        {
            _HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _Url = url ?? throw new ArgumentNullException(nameof(url));
        }

        #endregion

        #region Internal-Methods

        /// <summary>
        /// Find the key for a kid, fetching the JWKS when needed.
        /// </summary>
        /// <param name="kid">Key identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Key parameters, or null when the kid is unknown.</returns>
        /// <exception cref="ArgumentNullException">The kid is null.</exception>
        /// <exception cref="JwtValidationException">No keys are available because the JWKS could not be fetched.</exception>
        internal async Task<RSAParameters?> GetKeyAsync(string kid, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(kid);

            JwksKeySet? current = _KeySet;
            if (current != null && !IsStale(current) && current.Keys.TryGetValue(kid, out RSAParameters cached)) return cached;

            await _FetchLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                current = _KeySet;
                if (current != null && !IsStale(current) && current.Keys.TryGetValue(kid, out RSAParameters fetchedMeanwhile)) return fetchedMeanwhile;

                DateTime now = DateTime.UtcNow;
                bool mayFetch = _LastAttemptUtc == DateTime.MinValue || now - _LastAttemptUtc >= _MinimumRefetchInterval;
                Exception? failure = null;
                if (mayFetch)
                {
                    _LastAttemptUtc = now;
                    try
                    {
                        _KeySet = await FetchAsync(token).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is HttpRequestException || e is JsonException || e is FormatException || e is JwtValidationException || (e is TaskCanceledException && !token.IsCancellationRequested))
                    {
                        failure = e;
                    }
                }

                current = _KeySet;
                if (current == null)
                {
                    if (failure != null) throw new JwtValidationException("Signing keys could not be fetched from " + SafeHost() + ".", failure);
                    throw new JwtValidationException("Signing keys from " + SafeHost() + " are unavailable; retry later.");
                }
                if (current.Keys.TryGetValue(kid, out RSAParameters found)) return found;
                return null;
            }
            finally
            {
                _FetchLock.Release();
            }
        }

        #endregion

        #region Private-Methods

        private bool IsStale(JwksKeySet keySet)
        {
            return DateTime.UtcNow - keySet.FetchedUtc > _CacheDuration;
        }

        private async Task<JwksKeySet> FetchAsync(CancellationToken token)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, _Url))
            using (HttpResponseMessage response = await _HttpClient.SendAsync(request, token).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException("JWKS endpoint returned HTTP " + (int)response.StatusCode + ".");
                }
                string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                Dictionary<string, RSAParameters> keys = new Dictionary<string, RSAParameters>(StringComparer.Ordinal);
                using (JsonDocument document = JsonDocument.Parse(body))
                {
                    if (!JsonClaims.TryGetArray(document.RootElement, "keys", out JsonElement array))
                    {
                        throw new JwtValidationException("The JWKS document has no keys array.");
                    }
                    foreach (JsonElement jwk in array.EnumerateArray())
                    {
                        string? kty = JsonClaims.GetString(jwk, "kty");
                        string? kid = JsonClaims.GetString(jwk, "kid");
                        string? n = JsonClaims.GetString(jwk, "n");
                        string? e = JsonClaims.GetString(jwk, "e");
                        string? use = JsonClaims.GetString(jwk, "use");
                        if (!String.Equals(kty, "RSA", StringComparison.Ordinal)) continue;
                        if (String.IsNullOrEmpty(kid) || String.IsNullOrEmpty(n) || String.IsNullOrEmpty(e)) continue;
                        if (use != null && !String.Equals(use, "sig", StringComparison.Ordinal)) continue;
                        RSAParameters parameters = new RSAParameters
                        {
                            Modulus = Base64Url.Decode(n),
                            Exponent = Base64Url.Decode(e)
                        };
                        keys[kid] = parameters;
                    }
                }
                return new JwksKeySet(keys, DateTime.UtcNow);
            }
        }

        private string SafeHost()
        {
            if (Uri.TryCreate(_Url, UriKind.Absolute, out Uri? uri)) return uri.Host;
            return "the JWKS endpoint";
        }

        private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        #endregion
    }
}
