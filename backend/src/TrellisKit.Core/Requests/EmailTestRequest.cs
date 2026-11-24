namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of POST /api/v1/admin/email/test (and the email integration test).
    /// </summary>
    public class EmailTestRequest
    {
        #region Public-Members

        /// <summary>
        /// Template to send with sample values (default test).
        /// </summary>
        public string? Template { get; set; } = null;

        /// <summary>
        /// Recipient; default the caller's address.
        /// </summary>
        public string? To { get; set; } = null;

        #endregion
    }
}
