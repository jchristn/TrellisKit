namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of PUT /api/v1/admin/property/domains: a provided list replaces the stored one.
    /// </summary>
    public class PropertyDomainsUpdateRequest
    {
        #region Public-Members

        /// <summary>
        /// Domains (at most 20), or null for unchanged.
        /// </summary>
        public List<PropertyDomainRequest>? Domains { get; set; } = null;

        /// <summary>
        /// Apps (at most 10), or null for unchanged.
        /// </summary>
        public List<PropertyAppRequest>? Apps { get; set; } = null;

        #endregion
    }
}
