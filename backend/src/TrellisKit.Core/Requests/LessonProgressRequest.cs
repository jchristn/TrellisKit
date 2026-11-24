namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of PUT /api/v1/lessons/{id}/progress (docs/PHASE4_CONTRACT.md section 5.5).
    /// </summary>
    public class LessonProgressRequest
    {
        #region Public-Members

        /// <summary>
        /// Current position.
        /// </summary>
        public double? PositionSeconds { get; set; } = null;

        /// <summary>
        /// Seconds actually played since the last update.
        /// </summary>
        public double? PlayedSeconds { get; set; } = null;

        /// <summary>
        /// Client time of the update (offline replay keeps the original).
        /// </summary>
        public DateTime? ClientUpdatedUtc { get; set; } = null;

        /// <summary>
        /// Playback session identifier (one play per session).
        /// </summary>
        public string? SessionId { get; set; } = null;

        #endregion
    }
}
