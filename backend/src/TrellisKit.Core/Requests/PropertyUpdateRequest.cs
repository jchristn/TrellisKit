namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// PUT /api/v1/platform/properties/{id} body; omitted fields are unchanged.
    /// </summary>
    public class PropertyUpdateRequest
    {
        #region Public-Members

        /// <summary>
        /// Display name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Replaces the domain list.
        /// </summary>
        public List<PropertyDomainRequest>? Domains { get; set; } = null;

        /// <summary>
        /// Replaces the app list.
        /// </summary>
        public List<PropertyAppRequest>? Apps { get; set; } = null;

        /// <summary>
        /// Replaces the labels.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Replaces the tags.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
