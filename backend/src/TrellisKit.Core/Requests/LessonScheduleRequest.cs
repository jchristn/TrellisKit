namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of POST /api/v1/admin/lessons/{id}/schedule.
    /// </summary>
    public class LessonScheduleRequest
    {
        #region Public-Members

        /// <summary>
        /// When to publish (UTC).
        /// </summary>
        public DateTime? PublishAtUtc { get; set; } = null;

        #endregion
    }
}
