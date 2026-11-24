namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Response of POST /api/v1/admin/email/test/catalog: every template of the property's catalog sent once with
    /// sample data. Bounded by the catalog, which is fixed by the engine version plus the property's own templates.
    /// </summary>
    public class EmailCatalogTestResponse
    {
        #region Public-Members

        /// <summary>
        /// Recipient.
        /// </summary>
        public string To { get; set; } = String.Empty;

        /// <summary>
        /// Provider used (Mailgun, Smtp, or Log).
        /// </summary>
        public string Provider { get; set; } = String.Empty;

        /// <summary>
        /// Templates sent.
        /// </summary>
        public int Sent { get; set; } = 0;

        /// <summary>
        /// Templates not sent (email not configured, suppressed, or failed).
        /// </summary>
        public int Failed { get; set; } = 0;

        /// <summary>
        /// Per-template outcomes, in catalog order. Never null.
        /// </summary>
        public List<EmailCatalogTestResult> Results
        {
            get { return _Results; }
            set { _Results = value ?? new List<EmailCatalogTestResult>(); }
        }

        #endregion

        #region Private-Members

        private List<EmailCatalogTestResult> _Results = new List<EmailCatalogTestResult>();

        #endregion
    }
}
