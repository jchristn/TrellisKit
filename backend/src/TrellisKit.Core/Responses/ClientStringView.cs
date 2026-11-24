namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A published remote string override as clients receive it.
    /// </summary>
    public class ClientStringView
    {
        /// <summary>
        /// Catalog key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Override text.
        /// </summary>
        public string Value { get; set; } = String.Empty;

        /// <summary>
        /// Content version of the revision.
        /// </summary>
        public long ContentVersion { get; set; } = 0;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
