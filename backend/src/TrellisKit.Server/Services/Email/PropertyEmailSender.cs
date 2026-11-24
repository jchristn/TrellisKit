namespace TrellisKit.Server.Services.Email
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// An <see cref="IEmailSender"/> that sends through the provider the property's email settings choose at send time
    /// (Mailgun, SMTP, or none), used by senders outside the administrative email pipeline (new feed post emails).
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class PropertyEmailSender : IEmailSender
    {
        #region Public-Members

        /// <inheritdoc />
        public bool IsConfigured
        {
            get
            {
                EmailProviderChoice choice = _Email.ProviderAsync().GetAwaiter().GetResult();
                return choice.Sender != null && choice.Sender.IsConfigured;
            }
        }

        #endregion

        #region Private-Members

        private readonly EmailService _Email;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the sender.
        /// </summary>
        /// <param name="email">The property's email service.</param>
        /// <exception cref="ArgumentNullException">The service is null.</exception>
        public PropertyEmailSender(EmailService email)
        {
            _Email = email ?? throw new ArgumentNullException(nameof(email));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task SendAsync(EmailMessage message, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(message);
            EmailProviderChoice choice = await _Email.ProviderAsync(token).ConfigureAwait(false);
            if (choice.Sender == null || !choice.Sender.IsConfigured) throw new IntegrationNotConfiguredException("Email is not configured for this property.");
            message.FromName ??= choice.FromName;
            message.FromAddress ??= choice.FromAddress;
            await choice.Sender.SendAsync(message, token).ConfigureAwait(false);
        }

        #endregion
    }
}
