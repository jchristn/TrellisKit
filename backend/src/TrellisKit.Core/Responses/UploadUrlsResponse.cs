namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Fresh upload URLs (POST /api/v1/media/{id}/upload-urls).
    /// </summary>
    public class UploadUrlsResponse
    {
        #region Public-Members

        /// <summary>
        /// Upload instructions.
        /// </summary>
        public UploadInstructions Upload { get; set; } = new UploadInstructions();

        #endregion
    }
}
