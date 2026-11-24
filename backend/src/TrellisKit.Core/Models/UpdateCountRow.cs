namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A label and count pair (binary versions, event counts per day).
    /// </summary>
    public class UpdateCountRow : TrellisKit.Core.Responses.CatalogEntryMetadata
    {
        /// <summary>
        /// Label, for example an app version or a UTC day.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Optional kind of the count, for example an event type.
        /// </summary>
        public string Type { get; set; } = String.Empty;

        /// <summary>
        /// Count.
        /// </summary>
        public long Count { get; set; } = 0;
    }
}
