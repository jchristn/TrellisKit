namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// Pending draft counts per content type.
    /// </summary>
    public class ContentDraftCounts
    {
        /// <summary>
        /// String override drafts.
        /// </summary>
        public long Strings { get; set; } = 0;

        /// <summary>
        /// Banner drafts.
        /// </summary>
        public long Banners { get; set; } = 0;

        /// <summary>
        /// Remote asset drafts.
        /// </summary>
        public long Assets { get; set; } = 0;
    }
}
