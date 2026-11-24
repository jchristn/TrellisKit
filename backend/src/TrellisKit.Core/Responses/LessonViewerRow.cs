namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One member who watched a lesson (staff).
    /// </summary>
    public class LessonViewerRow
    {
        #region Public-Members

        /// <summary>
        /// Member.
        /// </summary>
        public UserSummary User { get; set; } = new UserSummary();

        /// <summary>
        /// Progress.
        /// </summary>
        public LessonProgressView Progress { get; set; } = new LessonProgressView();

        /// <summary>
        /// Playback sessions.
        /// </summary>
        public long Plays { get; set; } = 0;

        /// <summary>
        /// Labels of the progress row.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags of the progress row.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
