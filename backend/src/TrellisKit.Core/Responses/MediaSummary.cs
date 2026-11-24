namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Admin media library totals (contract section 10.3).
    /// </summary>
    public class MediaSummary
    {
        #region Public-Members

        /// <summary>
        /// Media items that are not deleted.
        /// </summary>
        public long TotalCount { get; set; } = 0;

        /// <summary>
        /// Bytes of originals plus image variants.
        /// </summary>
        public long TotalStoredBytes { get; set; } = 0;

        /// <summary>
        /// Counts by kind (Image, Video). Fixed keys.
        /// </summary>
        public Dictionary<string, long> ByKind { get; set; } = new Dictionary<string, long>();

        /// <summary>
        /// Counts by status (Uploading, Processing, Ready, Failed). Fixed keys.
        /// </summary>
        public Dictionary<string, long> ByStatus { get; set; } = new Dictionary<string, long>();

        /// <summary>
        /// Items that failed in the last 24 hours.
        /// </summary>
        public long Failed24h { get; set; } = 0;

        #endregion
    }
}
