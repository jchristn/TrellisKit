namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One day of adoption counts.
    /// </summary>
    public class UpdateAdoptionDay : CatalogEntryMetadata
    {
        /// <summary>
        /// UTC day YYYY-MM-DD.
        /// </summary>
        public string Date { get; set; } = String.Empty;

        /// <summary>
        /// Manifest responses that offered the update.
        /// </summary>
        public long Checks { get; set; } = 0;

        /// <summary>
        /// Downloaded events.
        /// </summary>
        public long Downloads { get; set; } = 0;

        /// <summary>
        /// Applied events.
        /// </summary>
        public long Applied { get; set; } = 0;

        /// <summary>
        /// Failure events.
        /// </summary>
        public long Failures { get; set; } = 0;
    }
}
