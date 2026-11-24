namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Facebook Login availability.
    /// </summary>
    public class FacebookProviderInfo
    {
        #region Public-Members

        /// <summary>
        /// Whether enabled.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// App id, or null.
        /// </summary>
        public string? AppId { get; set; } = null;

        #endregion
    }
}
