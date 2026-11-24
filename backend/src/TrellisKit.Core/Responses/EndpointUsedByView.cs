namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Bounded preview of an endpoint's references.
    /// </summary>
    public class EndpointUsedByView
    {
        #region Public-Members

        /// <summary>
        /// Up to 10 references.
        /// </summary>
        public List<EndpointUsageItem> Objects { get; set; } = new List<EndpointUsageItem>();

        /// <summary>
        /// Total references.
        /// </summary>
        public long TotalRecords { get; set; } = 0;

        /// <summary>
        /// Route of the full enumeration.
        /// </summary>
        public string Enumerate { get; set; } = String.Empty;

        #endregion
    }
}
