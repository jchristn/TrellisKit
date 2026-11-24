namespace TrellisKit.Core.Integrations.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Verifies Amazon SNS messages (contract section 4.9) and confirms subscriptions.
    /// Thread safety: implementations are safe to call concurrently.
    /// </summary>
    public interface ISnsMessageVerifier
    {
        /// <summary>
        /// Parse and verify a message: signature (version 2 SHA256 or version 1 SHA1), signing certificate URL
        /// (https on sns.&lt;region&gt;.amazonaws.com), and the configured topic.
        /// </summary>
        /// <param name="rawJson">Request body exactly as received.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Verified message.</returns>
        /// <exception cref="WebhookSignatureException">Verification failed.</exception>
        Task<SnsMessage> VerifyAsync(string rawJson, CancellationToken token = default);

        /// <summary>
        /// Confirm a verified SubscriptionConfirmation by fetching its SubscribeURL.
        /// </summary>
        /// <param name="message">Verified message.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The message is null.</exception>
        /// <exception cref="WebhookSignatureException">The SubscribeURL is not an SNS URL.</exception>
        Task ConfirmSubscriptionAsync(SnsMessage message, CancellationToken token = default);
    }
}
