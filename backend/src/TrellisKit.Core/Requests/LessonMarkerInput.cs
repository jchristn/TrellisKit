namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// A marker in a lesson write.
    /// </summary>
    public class LessonMarkerInput
    {
        #region Public-Members

        /// <summary>
        /// Offset in seconds.
        /// </summary>
        public int Seconds { get; set; } = 0;

        /// <summary>
        /// Label (1 to 80 characters).
        /// </summary>
        public string? Label { get; set; } = null;

        #endregion
    }
}
