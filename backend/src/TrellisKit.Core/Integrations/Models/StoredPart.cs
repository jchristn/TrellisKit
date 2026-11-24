namespace TrellisKit.Core.Integrations.Models
{
    using System;

    /// <summary>
    /// A part of a multipart upload: as listed by storage (with its size), or as reported by the client on complete
    /// (size 0).
    /// </summary>
    public class StoredPart
    {
        #region Public-Members

        /// <summary>
        /// One-based part number.
        /// </summary>
        public int PartNumber { get; set; } = 1;

        /// <summary>
        /// ETag including its quotes, for example "9b2cf535f27731c974343645a3985328".
        /// </summary>
        public string ETag { get; set; } = String.Empty;

        /// <summary>
        /// Stored size in bytes (0 when unknown).
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        #endregion
    }
}
