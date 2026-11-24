namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Result of an integration test call.
    /// </summary>
    public class IntegrationTestResponse
    {
        #region Public-Members

        /// <summary>
        /// Whether the call succeeded.
        /// </summary>
        public bool Ok { get; set; } = false;

        /// <summary>
        /// Outcome (no credentials).
        /// </summary>
        public string Message { get; set; } = String.Empty;

        #endregion
    }
}
