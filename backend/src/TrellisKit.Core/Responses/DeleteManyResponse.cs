namespace TrellisKit.Core.Responses
{
    /// <summary>
    /// Result of a bulk delete.
    /// </summary>
    public class DeleteManyResponse
    {
        #region Public-Members

        /// <summary>
        /// Number of rows deleted.
        /// </summary>
        public int DeletedCount { get; set; } = 0;

        #endregion
    }
}
