namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Admin media detail: item fields plus processing details and variants (at most six).
    /// </summary>
    public class AdminMediaDetail : AdminMediaItem
    {
        #region Public-Members

        /// <summary>
        /// Processor label, or null.
        /// </summary>
        public string? Processor { get; set; } = null;

        /// <summary>
        /// Asynchronous job identifier, or null.
        /// </summary>
        public string? ProcessorJobId { get; set; } = null;

        /// <summary>
        /// When processing started, or null.
        /// </summary>
        public DateTime? ProcessingStartedUtc { get; set; } = null;

        /// <summary>
        /// Processing attempts.
        /// </summary>
        public int Attempts { get; set; } = 0;

        /// <summary>
        /// Stored variants (three sizes in two formats at most).
        /// </summary>
        public List<AdminMediaVariantView> Variants { get; set; } = new List<AdminMediaVariantView>();

        #endregion
    }
}
