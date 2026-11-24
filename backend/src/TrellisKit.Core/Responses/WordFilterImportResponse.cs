namespace TrellisKit.Core.Responses
{
    /// <summary>
    /// Result of POST /api/v1/admin/moderation/word-filter/import.
    /// </summary>
    public class WordFilterImportResponse
    {
        #region Public-Members

        /// <summary>
        /// Terms added.
        /// </summary>
        public int Added { get; set; } = 0;

        /// <summary>
        /// Terms skipped (duplicates, empty, or too long).
        /// </summary>
        public int Skipped { get; set; } = 0;

        #endregion
    }
}
