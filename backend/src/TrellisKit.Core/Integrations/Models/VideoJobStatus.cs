namespace TrellisKit.Core.Integrations.Models
{
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Status of an asynchronous transcode job, with the output dimensions once complete.
    /// </summary>
    public class VideoJobStatus
    {
        #region Public-Members

        /// <summary>
        /// Job state.
        /// </summary>
        public VideoJobStateEnum State { get; set; } = VideoJobStateEnum.Running;

        /// <summary>
        /// Error message when the job failed, or null.
        /// </summary>
        public string? ErrorMessage { get; set; } = null;

        /// <summary>
        /// Largest output width, or null.
        /// </summary>
        public int? Width { get; set; } = null;

        /// <summary>
        /// Largest output height, or null.
        /// </summary>
        public int? Height { get; set; } = null;

        #endregion
    }
}
