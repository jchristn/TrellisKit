namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One object change of a content version (table appcontentchanges).
    /// </summary>
    public class ContentChange
    {
        /// <summary>
        /// Content version.
        /// </summary>
        public long Version { get; set; } = 0;

        /// <summary>
        /// Object type (FeatureFlag).
        /// </summary>
        public string ObjectType { get; set; } = String.Empty;

        /// <summary>
        /// Object id.
        /// </summary>
        public string ObjectId { get; set; } = String.Empty;

        /// <summary>
        /// Upsert or Delete.
        /// </summary>
        public string Change { get; set; } = String.Empty;
    }
}
