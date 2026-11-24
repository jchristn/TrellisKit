namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Headers of one multipart/form-data part. The body is read with <see cref="MultipartFormReader.ReadBodyAsync"/>.
    /// </summary>
    public class MultipartSection
    {
        #region Public-Members

        /// <summary>
        /// Part headers by lowercase name.
        /// </summary>
        public Dictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// The name parameter of Content-Disposition, or empty.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// The filename parameter of Content-Disposition, or null.
        /// </summary>
        public string? FileName { get; set; } = null;

        /// <summary>
        /// The part Content-Type, or application/octet-stream when absent.
        /// </summary>
        public string ContentType { get; set; } = "application/octet-stream";

        #endregion
    }
}
