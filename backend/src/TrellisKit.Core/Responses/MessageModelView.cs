namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Model that answered.
    /// </summary>
    public class MessageModelView
    {
        #region Public-Members

        /// <summary>
        /// Endpoint id.
        /// </summary>
        public string? EndpointId { get; set; } = null;

        /// <summary>
        /// Endpoint name.
        /// </summary>
        public string? EndpointName { get; set; } = null;

        /// <summary>
        /// Model.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Fallback used.
        /// </summary>
        public bool UsedFallback { get; set; } = false;

        #endregion
    }
}
