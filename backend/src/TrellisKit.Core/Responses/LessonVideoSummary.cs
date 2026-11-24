namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Video of a lesson list item.
    /// </summary>
    public class LessonVideoSummary
    {
        #region Public-Members

        /// <summary>
        /// Duration.
        /// </summary>
        public int DurationSeconds { get; set; } = 0;

        /// <summary>
        /// Poster thumbnail URL, or null.
        /// </summary>
        public string? PosterThumbUrl { get; set; } = null;

        /// <summary>
        /// Blurhash, or null.
        /// </summary>
        public string? Blurhash { get; set; } = null;

        #endregion
    }
}
