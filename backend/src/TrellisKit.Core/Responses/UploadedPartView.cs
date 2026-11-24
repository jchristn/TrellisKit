namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// A part storage already has (upload-urls resume).
    /// </summary>
    public class UploadedPartView
    {
        #region Public-Members

        /// <summary>
        /// One-based part number.
        /// </summary>
        public int PartNumber { get; set; } = 1;

        /// <summary>
        /// ETag including quotes.
        /// </summary>
        public string Etag { get; set; } = String.Empty;

        /// <summary>
        /// Size in bytes.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        #endregion
    }
}
