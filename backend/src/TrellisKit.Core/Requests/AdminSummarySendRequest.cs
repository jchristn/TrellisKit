namespace TrellisKit.Core.Requests
{
    /// <summary>
    /// Body of POST /api/v1/admin/summary/send.
    /// </summary>
    public class AdminSummarySendRequest
    {
        #region Public-Members

        /// <summary>
        /// Local date to summarize (yyyy-MM-dd), or null for yesterday in the summary time zone.
        /// </summary>
        public string? Date { get; set; } = null;

        /// <summary>
        /// One address to send a copy to, or null for the configured recipients. A copy to an explicit address is always
        /// sent; configured recipients receive each date once.
        /// </summary>
        public string? To { get; set; } = null;

        #endregion
    }
}
