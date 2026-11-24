namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Place search candidates (at most 5).
    /// </summary>
    public class GeocodeSearchResponse
    {
        #region Public-Members

        /// <summary>
        /// Candidates.
        /// </summary>
        public List<GeocodeCandidateView> Candidates { get; set; } = new List<GeocodeCandidateView>();

        #endregion
    }
}
