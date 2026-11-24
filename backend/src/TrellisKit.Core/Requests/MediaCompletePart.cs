namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// A part reported on complete.
    /// </summary>
    public class MediaCompletePart
    {
        #region Public-Members

        /// <summary>
        /// One-based part number.
        /// </summary>
        public int PartNumber { get; set; } = 0;

        /// <summary>
        /// ETag exactly as returned by the PUT (with quotes).
        /// </summary>
        public string? Etag { get; set; } = null;

        #endregion
    }
}
