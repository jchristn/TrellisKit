namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Body of POST /api/v1/admin/ai/endpoints/{id}/reveal (docs/AI_CONTRACT.md section 2.5).
    /// </summary>
    public class SecretRevealRequest
    {
        #region Public-Members

        /// <summary>
        /// authValue or extraHeader.
        /// </summary>
        public string? Field { get; set; } = null;

        /// <summary>
        /// Header name for extraHeader.
        /// </summary>
        public string? HeaderName { get; set; } = null;

        /// <summary>
        /// Reason (10 to 500 characters).
        /// </summary>
        public string? Reason { get; set; } = null;

        #endregion
    }
}
