namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;

    /// <summary>
    /// POST /api/v1/platform/properties body. The id is always generated (app_{k-sortable}_{random}); the document is a property.json object or a clone of another property's document.
    /// </summary>
    public class PropertyCreateRequest
    {
        #region Public-Members

        /// <summary>
        /// Display name (overrides the document name).
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Cosmetic slug (overrides the document slug).
        /// </summary>
        public string? Slug { get; set; } = null;

        /// <summary>
        /// The property.json document (its id is replaced). Required unless CloneFromPropertyId is set.
        /// </summary>
        public JsonElement? Document { get; set; } = null;

        /// <summary>
        /// Copy the configuration of this property (never its members or content).
        /// </summary>
        public string? CloneFromPropertyId { get; set; } = null;

        /// <summary>
        /// Domains (at most 20); null uses the document domains.
        /// </summary>
        public List<PropertyDomainRequest>? Domains { get; set; } = null;

        /// <summary>
        /// Apps (at most 10); null uses the document mobile ids.
        /// </summary>
        public List<PropertyAppRequest>? Apps { get; set; } = null;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
