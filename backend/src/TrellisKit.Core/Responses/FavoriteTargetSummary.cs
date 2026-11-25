namespace TrellisKit.Core.Responses
{
    /// <summary>
    /// What a saved list shows about a target. When the caller can no longer open the target (archived,
    /// unpublished, moderated, feature off, or access lost) <see cref="Available"/> is false and the title, image,
    /// and link are null, so a saved list never leaks content the member cannot open.
    /// </summary>
    public class FavoriteTargetSummary
    {
        #region Public-Members

        /// <summary>
        /// Whether the caller can open the target now.
        /// </summary>
        public bool Available { get; set; } = false;

        /// <summary>
        /// Title, or null when unavailable.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Image URL, or null.
        /// </summary>
        public string? ImageUrl { get; set; } = null;

        /// <summary>
        /// App path of the target (for example /lessons/{id}), or null when unavailable.
        /// </summary>
        public string? Href { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// The summary of a target the caller cannot open.
        /// </summary>
        /// <returns>Summary.</returns>
        public static FavoriteTargetSummary Unavailable()
        {
            return new FavoriteTargetSummary();
        }

        #endregion
    }
}
