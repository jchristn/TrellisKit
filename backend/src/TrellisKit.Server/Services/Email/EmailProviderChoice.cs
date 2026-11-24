namespace TrellisKit.Server.Services.Email
{
    using System;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Integrations.Interfaces;

    /// <summary>
    /// The provider that sends a property's administrative email right now, with its sender identity.
    /// </summary>
    public class EmailProviderChoice
    {
        #region Public-Members

        /// <summary>
        /// Provider.
        /// </summary>
        public EmailProviderEnum Provider { get; set; } = EmailProviderEnum.Log;

        /// <summary>
        /// Sender, or null for Log.
        /// </summary>
        public IEmailSender? Sender { get; set; } = null;

        /// <summary>
        /// From name.
        /// </summary>
        public string FromName { get; set; } = String.Empty;

        /// <summary>
        /// From address.
        /// </summary>
        public string FromAddress { get; set; } = String.Empty;

        /// <summary>
        /// Reply-to, or null.
        /// </summary>
        public string? ReplyTo { get; set; } = null;

        #endregion
    }
}
