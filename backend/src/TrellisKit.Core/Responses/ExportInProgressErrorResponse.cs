namespace TrellisKit.Core.Responses
{
    /// <summary>
    /// 409 ExportInProgress body: the error plus the export that is already Pending or Running.
    /// </summary>
    public class ExportInProgressErrorResponse : ErrorResponse
    {
        #region Public-Members

        /// <summary>
        /// Identifier of the export in progress, or null when it finished in the meantime.
        /// </summary>
        public string? ExportId { get; set; } = null;

        #endregion
    }
}
