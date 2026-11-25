namespace TrellisKit.Server.Services.CatalogImport
{
    using System;

    /// <summary>
    /// The outcome of one bounded catalog manifest fetch. Contains the body only for a successful JSON response.
    /// </summary>
    public sealed class CatalogManifestResponse
    {
        #region Public-Members

        /// <summary>
        /// HTTP status of the manifest response; 0 when no response arrived, -1 when robots.txt disallows the manifest.
        /// </summary>
        public int Status { get; set; } = 0;

        /// <summary>
        /// UTF-8 manifest body (at most <see cref="CatalogManifestFetcher.MaxBytes"/> bytes), or null on failure.
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Safe failure description, or null on success. Never contains source content.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Whether a later attempt may succeed (network failures, HTTP 429, and HTTP 5xx).
        /// </summary>
        public bool Retryable { get; set; } = false;

        #endregion
    }
}
