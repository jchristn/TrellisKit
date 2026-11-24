namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// An extra header in an endpoint body.
    /// </summary>
    public class EndpointHeaderRequest
    {
        #region Public-Members

        /// <summary>
        /// Header name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Write-only value; omitted keeps the stored value of the same name.
        /// </summary>
        public string? Value { get; set; } = null;

        #endregion
    }
}
