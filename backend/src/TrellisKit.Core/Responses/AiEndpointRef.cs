namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Short reference to an endpoint in analytics.
    /// </summary>
    public class AiEndpointRef
    {
        #region Public-Members

        /// <summary>
        /// Endpoint id.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Endpoint name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// API type.
        /// </summary>
        public string ApiType { get; set; } = String.Empty;

        /// <summary>
        /// Model.
        /// </summary>
        public string Model { get; set; } = String.Empty;

        /// <summary>
        /// Whether the endpoint was deleted.
        /// </summary>
        public bool Deleted { get; set; } = false;

        #endregion
    }
}
