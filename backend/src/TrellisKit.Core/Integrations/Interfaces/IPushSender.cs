namespace TrellisKit.Core.Integrations.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Sends push messages and reads their receipts (Expo push API). Implementations are safe to call concurrently.
    /// </summary>
    public interface IPushSender
    {
        /// <summary>
        /// Whether pushes can be sent. Expo works without an access token, so the Expo sender is always configured.
        /// </summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Send messages (in batches of at most 100 per request).
        /// </summary>
        /// <param name="messages">Messages.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One ticket per message, in the same order.</returns>
        /// <exception cref="ArgumentNullException">The list is null.</exception>
        /// <exception cref="HttpRequestException">The push service rejected a request or could not be reached.</exception>
        Task<List<PushTicket>> SendAsync(List<PushMessage> messages, CancellationToken token = default);

        /// <summary>
        /// Read receipts for tickets.
        /// </summary>
        /// <param name="ticketIds">Ticket identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Receipts keyed by ticket identifier; tickets without a receipt yet are absent.</returns>
        /// <exception cref="ArgumentNullException">The list is null.</exception>
        /// <exception cref="HttpRequestException">The push service rejected a request or could not be reached.</exception>
        Task<Dictionary<string, PushReceipt>> GetReceiptsAsync(List<string> ticketIds, CancellationToken token = default);
    }
}
