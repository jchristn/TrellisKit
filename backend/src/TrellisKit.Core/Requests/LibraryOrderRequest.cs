namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of PUT /api/v1/admin/series/order ({ seriesIds }) and PUT /api/v1/admin/series/{id}/lessons/order ({ lessonIds }).
    /// </summary>
    public class LibraryOrderRequest
    {
        #region Public-Members

        /// <summary>
        /// Series in the new order.
        /// </summary>
        public List<string>? SeriesIds { get; set; } = null;

        /// <summary>
        /// Lessons in the new order.
        /// </summary>
        public List<string>? LessonIds { get; set; } = null;

        /// <summary>
        /// Custom levels in the new order (PUT /api/v1/admin/library/levels/order).
        /// </summary>
        public List<string>? LevelIds { get; set; } = null;

        #endregion
    }
}
