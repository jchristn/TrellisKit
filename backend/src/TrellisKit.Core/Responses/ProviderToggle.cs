namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Availability of an integration without public identifiers.
    /// </summary>
    public class ProviderToggle
    {
        #region Public-Members

        /// <summary>
        /// Whether enabled.
        /// </summary>
        public bool Enabled { get; set; } = false;

        #endregion
    }
}
