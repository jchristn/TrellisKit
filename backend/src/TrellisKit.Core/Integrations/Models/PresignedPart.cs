namespace TrellisKit.Core.Integrations.Models
{
    using System;

    /// <summary>
    /// A presigned part PUT of a multipart upload. Part PUTs send no extra headers.
    /// </summary>
    public class PresignedPart
    {
        #region Public-Members

        /// <summary>
        /// One-based part number.
        /// </summary>
        public int PartNumber { get; set; } = 1;

        /// <summary>
        /// HTTP method, always PUT.
        /// </summary>
        public string Method { get; set; } = "PUT";

        /// <summary>
        /// Absolute URL.
        /// </summary>
        public string Url { get; set; } = String.Empty;

        #endregion
    }
}
