namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// GET /api/v1/admin/ai/endpoints/{id}/health.
    /// </summary>
    public class EndpointHealthDetailView
    {
        #region Public-Members

        /// <summary>
        /// Health state.
        /// </summary>
        public EndpointHealthView Health { get; set; } = new EndpointHealthView();

        /// <summary>
        /// Most recent records.
        /// </summary>
        public HealthRecentView Recent { get; set; } = new HealthRecentView();

        #endregion
    }
}
