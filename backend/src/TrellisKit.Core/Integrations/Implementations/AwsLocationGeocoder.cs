namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon;
    using Amazon.LocationService;
    using Amazon.LocationService.Model;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Amazon Location Service Places (SearchPlaceIndexForText), using an explicitly supplied credential pair or
    /// the default AWS chain when no complete pair is supplied. Thread safe; dispose to release the client.
    /// </summary>
    public sealed class AwsLocationGeocoder : IGeocoder, IDisposable
    {
        #region Public-Members

        /// <inheritdoc />
        public string Provider
        {
            get { return "AwsLocation"; }
        }

        /// <inheritdoc />
        public bool IsConfigured
        {
            get { return _IndexName != null && _Region != null; }
        }

        #endregion

        #region Private-Members

        private readonly string? _IndexName;
        private readonly string? _Region;
        private readonly TimeSpan _Timeout;
        private AmazonLocationServiceClient? _Client = null;
        private readonly object _Lock = new object();
        private bool _Disposed = false;
        private readonly string? _AccessKeyId;
        private readonly string? _SecretAccessKey;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="indexName">Place index name (directory.geocoder.placeIndexName), or null.</param>
        /// <param name="region">AWS region (directory.geocoder.region), or null.</param>
        /// <param name="timeout">Request timeout.</param>
        /// <param name="accessKeyId">AWS access key id, or null or empty for the default credential chain.</param>
        /// <param name="secretAccessKey">AWS secret access key (used only with an access key id), or null.</param>
        public AwsLocationGeocoder(string? indexName, string? region, TimeSpan timeout, string? accessKeyId = null, string? secretAccessKey = null)
        {
            _AccessKeyId = String.IsNullOrWhiteSpace(accessKeyId) || String.IsNullOrWhiteSpace(secretAccessKey) ? null : accessKeyId.Trim();
            _SecretAccessKey = _AccessKeyId == null ? null : secretAccessKey!.Trim();
            _IndexName = String.IsNullOrWhiteSpace(indexName) ? null : indexName.Trim();
            _Region = String.IsNullOrWhiteSpace(region) ? null : region.Trim();
            _Timeout = timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(10) : timeout;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<List<GeocodeCandidate>> SearchAsync(string query, int limit, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(query)) throw new ArgumentNullException(nameof(query));
            if (!IsConfigured) throw new IntegrationNotConfiguredException("The AwsLocation geocoder needs directory.geocoder.placeIndexName and directory.geocoder.region.");
            AmazonLocationServiceClient client = Client();
            SearchPlaceIndexForTextRequest request = new SearchPlaceIndexForTextRequest { IndexName = _IndexName, Text = query.Trim(), MaxResults = Math.Clamp(limit, 1, 10) };
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(_Timeout);
                SearchPlaceIndexForTextResponse response = await client.SearchPlaceIndexForTextAsync(request, timeout.Token).ConfigureAwait(false);
                List<GeocodeCandidate> result = new List<GeocodeCandidate>();
                foreach (SearchForTextResult item in response.Results ?? new List<SearchForTextResult>())
                {
                    List<double>? point = item.Place?.Geometry?.Point;
                    if (point == null || point.Count < 2) continue;
                    result.Add(new GeocodeCandidate { Label = item.Place?.Label ?? String.Empty, Lng = point[0], Lat = point[1], TimeZone = item.Place?.TimeZone?.Name });
                }
                return result.Take(Math.Clamp(limit, 1, 10)).ToList();
            }
        }

        /// <summary>
        /// Release the client.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        private AmazonLocationServiceClient Client()
        {
            lock (_Lock)
            {
                ObjectDisposedException.ThrowIf(_Disposed, this);
                _Client ??= _AccessKeyId != null
                    ? new AmazonLocationServiceClient(new Amazon.Runtime.BasicAWSCredentials(_AccessKeyId, _SecretAccessKey), RegionEndpoint.GetBySystemName(_Region))
                    : new AmazonLocationServiceClient(RegionEndpoint.GetBySystemName(_Region));
                return _Client;
            }
        }

        private void Dispose(bool disposing)
        {
            lock (_Lock)
            {
                if (_Disposed) return;
                _Disposed = true;
                if (disposing) _Client?.Dispose();
                _Client = null;
            }
        }

        #endregion
    }
}
