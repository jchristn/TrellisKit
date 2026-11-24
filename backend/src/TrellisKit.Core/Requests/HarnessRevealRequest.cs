namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of POST /api/v1/admin/ai/harnesses/{runtime}/reveal.
    /// </summary>
    public class HarnessRevealRequest
    {
        #region Public-Members

        /// <summary>
        /// Why the credential is revealed (10 to 500 characters; written to the audit trail).
        /// </summary>
        public string? Reason { get; set; } = null;

        #endregion
    }
}
