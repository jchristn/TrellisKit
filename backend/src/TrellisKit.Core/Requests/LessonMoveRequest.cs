namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of POST /api/v1/admin/lessons/{id}/move.
    /// </summary>
    public class LessonMoveRequest
    {
        #region Public-Members

        /// <summary>
        /// Target series.
        /// </summary>
        public string? SeriesId { get; set; } = null;

        /// <summary>
        /// Position in the target series (1-based; default last).
        /// </summary>
        public int? Position { get; set; } = null;

        #endregion
    }
}
