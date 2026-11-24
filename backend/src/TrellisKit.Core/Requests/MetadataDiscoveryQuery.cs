namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of the label and tag discovery enumerations: an <see cref="EnumerationQuery"/> plus the object type, a
    /// prefix, and (for tag values) the key.
    /// </summary>
    public class MetadataDiscoveryQuery : EnumerationQuery
    {
        #region Public-Members

        /// <summary>
        /// Object type name to scope the counts to, or null for every type the caller can see.
        /// </summary>
        public string? TargetType { get; set; } = null;

        /// <summary>
        /// Only entries starting with this text (case-insensitive), or null.
        /// </summary>
        public string? Prefix { get; set; } = null;

        /// <summary>
        /// Tag key (required for tag values).
        /// </summary>
        public string? Key { get; set; } = null;

        #endregion
    }
}
