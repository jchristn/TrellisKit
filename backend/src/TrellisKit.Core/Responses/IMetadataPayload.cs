namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A payload that carries its object's labels and tags (docs/ENUMERATION_AND_METADATA.md section 1). Both are
    /// always present: labels may be empty and tags may be empty, never null.
    /// </summary>
    public interface IMetadataPayload
    {
        /// <summary>
        /// Labels visible to the caller (staff: entries only for staff). Never null.
        /// </summary>
        List<string> Labels { get; set; }

        /// <summary>
        /// Tags visible to the caller (staff: keys only for staff). Never null.
        /// </summary>
        Dictionary<string, string> Tags { get; set; }
    }
}
