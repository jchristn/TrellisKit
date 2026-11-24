namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Sign in with Apple availability.
    /// </summary>
    public class AppleProviderInfo
    {
        #region Public-Members

        /// <summary>
        /// Whether enabled.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Services ID for Sign in with Apple JS, or null.
        /// </summary>
        public string? WebClientId { get; set; } = null;

        #endregion
    }
}
