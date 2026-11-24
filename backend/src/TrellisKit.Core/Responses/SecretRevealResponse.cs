namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A revealed endpoint secret (never cached or captured).
    /// </summary>
    public class SecretRevealResponse
    {
        #region Public-Members

        /// <summary>
        /// authValue or extraHeader.
        /// </summary>
        public string Field { get; set; } = String.Empty;

        /// <summary>
        /// Header name for extraHeader.
        /// </summary>
        public string? HeaderName { get; set; } = null;

        /// <summary>
        /// The secret value.
        /// </summary>
        public string Value { get; set; } = String.Empty;

        #endregion
    }
}
