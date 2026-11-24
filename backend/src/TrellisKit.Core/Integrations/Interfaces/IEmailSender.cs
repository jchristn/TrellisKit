namespace TrellisKit.Core.Integrations.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Sends transactional email.
    /// Implementations are safe to call concurrently.
    /// </summary>
    public interface IEmailSender
    {
        /// <summary>
        /// Whether the integration has the settings it needs. When false, calls throw <see cref="IntegrationNotConfiguredException"/>.
        /// </summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Send a message.
        /// </summary>
        /// <param name="message">Message.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="InvalidOperationException">Delivery failed; the message names the cause without exposing credentials.</exception>
        /// <exception cref="IntegrationNotConfiguredException">Not configured.</exception>
        Task SendAsync(EmailMessage message, CancellationToken token = default);
    }
}
