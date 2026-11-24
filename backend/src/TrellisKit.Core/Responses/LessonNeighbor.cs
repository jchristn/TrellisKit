namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// The previous or next lesson in a series.
    /// </summary>
    public class LessonNeighbor
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Whether the caller may open it.
        /// </summary>
        public bool CanOpen { get; set; } = false;

        #endregion
    }
}
