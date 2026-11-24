namespace TrellisKit.Core.PropertyConfig
{
    using System;

    /// <summary>
    /// Contact and sender addresses of the property.
    /// </summary>
    public class PropertyContact
    {
        #region Public-Members

        /// <summary>
        /// Support email address. Required.
        /// </summary>
        public string SupportEmail { get; set; } = String.Empty;

        /// <summary>
        /// Default sender display name for outbound email. Required. The email.fromName setting overrides it.
        /// </summary>
        public string EmailFromName { get; set; } = String.Empty;

        /// <summary>
        /// Default sender address for outbound email. Required. The email.fromAddress setting overrides it.
        /// </summary>
        public string EmailFromAddress { get; set; } = String.Empty;

        #endregion
    }
}
