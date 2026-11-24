namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;

    /// <summary>
    /// Reviewer overrides before approval.
    /// </summary>
    public class SubmissionOverrideRequest
    {
        #region Public-Members

        /// <summary>
        /// Replacement proposed values.
        /// </summary>
        public Dictionary<string, JsonElement>? Fields { get; set; } = null;

        /// <summary>
        /// Replacement pin.
        /// </summary>
        public GeoRequest? Geo { get; set; } = null;

        #endregion
    }
}
