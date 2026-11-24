namespace TrellisKit.Core.Requests
{
    /// <summary>
    /// Body of POST /api/v1/admin/moderation/word-filter/test.
    /// </summary>
    public class WordFilterTestRequest
    {
        #region Public-Members

        /// <summary>
        /// Text to check (up to 10000 characters).
        /// </summary>
        public string? Text { get; set; } = null;

        #endregion
    }
}
