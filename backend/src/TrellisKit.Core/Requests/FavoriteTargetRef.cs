namespace TrellisKit.Core.Requests
{
    /// <summary>
    /// A target in a save state request.
    /// </summary>
    public class FavoriteTargetRef
    {
        #region Public-Members

        /// <summary>
        /// Target type name (Lesson, Series, CatalogEntry, Venue, Instructor, Event, Post, Thread).
        /// </summary>
        public string? TargetType { get; set; } = null;

        /// <summary>
        /// Target identifier.
        /// </summary>
        public string? TargetId { get; set; } = null;

        #endregion
    }
}
