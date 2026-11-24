namespace TrellisKit.Server.Services
{
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Result of POST /api/v1/media/{id}/complete: 202 when this call moved the media to Processing, 200 when it was
    /// already Processing, Ready, or Failed (idempotent).
    /// </summary>
    public class MediaCompleteOutcome
    {
        #region Public-Members

        /// <summary>
        /// Media payload.
        /// </summary>
        public MediaView Media { get; set; } = new MediaView();

        /// <summary>
        /// Whether this call accepted the upload (202) rather than returning an earlier result (200).
        /// </summary>
        public bool Accepted { get; set; } = false;

        #endregion
    }
}
