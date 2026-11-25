namespace TrellisKit.Core.Models
{
    /// <summary>
    /// Result of storing a save: the stored row and whether it was created.
    /// </summary>
    public class FavoriteUpsertResult
    {
        #region Public-Members

        /// <summary>
        /// The stored save.
        /// </summary>
        public Favorite Favorite { get; set; } = new Favorite();

        /// <summary>
        /// Whether the call created the save (false when it already existed).
        /// </summary>
        public bool Created { get; set; } = false;

        #endregion
    }
}
