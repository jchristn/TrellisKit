namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// The default geocoder: no automatic geocoding (staff place pins manually). Every search throws
    /// <see cref="IntegrationNotConfiguredException"/>.
    /// </summary>
    public sealed class NoneGeocoder : IGeocoder
    {
        #region Public-Members

        /// <inheritdoc />
        public string Provider
        {
            get { return "None"; }
        }

        /// <inheritdoc />
        public bool IsConfigured
        {
            get { return false; }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<List<GeocodeCandidate>> SearchAsync(string query, int limit, CancellationToken token = default)
        {
            throw new IntegrationNotConfiguredException("No geocoder is configured for this property (directory.geocoder.provider is None).");
        }

        #endregion
    }
}
