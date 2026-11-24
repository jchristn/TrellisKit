namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One key of the engine string catalog (shared/src/strings/catalog.json).
    /// </summary>
    public class StringCatalogEntry
    {
        /// <summary>
        /// Catalog key (web.* or mobile.*).
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Client type: web or mobile.
        /// </summary>
        public string Client { get; set; } = String.Empty;

        /// <summary>
        /// Built-in text.
        /// </summary>
        public string DefaultValue { get; set; } = String.Empty;

        /// <summary>
        /// Tokens the text may use, without braces.
        /// </summary>
        public List<string> Tokens { get; set; } = new List<string>();

        /// <summary>
        /// Longest override.
        /// </summary>
        public int MaxLength { get; set; } = 200;
    }
}
