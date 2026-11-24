namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Google Sign-In availability.
    /// </summary>
    public class GoogleProviderInfo
    {
        #region Public-Members

        /// <summary>
        /// Whether enabled.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Web client id, or null.
        /// </summary>
        public string? WebClientId { get; set; } = null;

        /// <summary>
        /// iOS client id, or null.
        /// </summary>
        public string? IosClientId { get; set; } = null;

        /// <summary>
        /// Android client id, or null.
        /// </summary>
        public string? AndroidClientId { get; set; } = null;

        #endregion
    }
}
