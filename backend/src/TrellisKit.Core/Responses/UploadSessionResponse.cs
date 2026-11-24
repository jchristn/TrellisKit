namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Upload session (201 from POST /api/v1/media/uploads).
    /// </summary>
    public class UploadSessionResponse
    {
        #region Public-Members

        /// <summary>
        /// Media with status Uploading.
        /// </summary>
        public MediaView Media { get; set; } = new MediaView();

        /// <summary>
        /// Upload instructions.
        /// </summary>
        public UploadInstructions Upload { get; set; } = new UploadInstructions();

        #endregion
    }
}
