namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// A lesson list item (LessonSummary).
    /// </summary>
    public class LessonSummaryView : LessonBaseView
    {
        #region Public-Members

        /// <summary>
        /// Duration, poster thumbnail, and blurhash, or null.
        /// </summary>
        public LessonVideoSummary? Video { get; set; } = null;

        #endregion
    }
}
