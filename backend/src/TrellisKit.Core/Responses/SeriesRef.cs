namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// A series reference inside a lesson.
    /// </summary>
    public class SeriesRef
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Slug.
        /// </summary>
        public string Slug { get; set; } = String.Empty;

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        #endregion
    }
}
