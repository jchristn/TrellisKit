namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// One template's outcome in POST /api/v1/admin/email/test/catalog.
    /// </summary>
    public class EmailCatalogTestResult
    {
        #region Public-Members

        /// <summary>
        /// Template key.
        /// </summary>
        public string Template { get; set; } = String.Empty;

        /// <summary>
        /// Whether the message was sent through a configured provider.
        /// </summary>
        public bool Ok { get; set; } = false;

        /// <summary>
        /// Outcome (no credentials).
        /// </summary>
        public string Message { get; set; } = String.Empty;

        #endregion
    }
}
