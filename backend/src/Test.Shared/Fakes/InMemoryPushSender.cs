namespace Test.Shared.Fakes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Push sender that records every message and answers with scripted tickets and receipts. By default every ticket
    /// is ok with a fresh id and every receipt is ok.
    /// </summary>
    public sealed class InMemoryPushSender : IPushSender
    {
        private readonly object _Lock = new object();
        private readonly List<PushMessage> _Messages = new List<PushMessage>();
        private readonly Dictionary<string, PushTicket> _TicketsByToken = new Dictionary<string, PushTicket>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _TicketToToken = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, PushReceipt> _ReceiptsByToken = new Dictionary<string, PushReceipt>(StringComparer.Ordinal);

        /// <inheritdoc />
        public bool IsConfigured
        {
            get { return true; }
        }

        /// <summary>
        /// When true, SendAsync throws HttpRequestException.
        /// </summary>
        public bool FailSends { get; set; } = false;

        /// <summary>
        /// Receipt requests made.
        /// </summary>
        public int ReceiptRequests { get; private set; } = 0;

        /// <summary>
        /// Every message sent, oldest first.
        /// </summary>
        public IReadOnlyList<PushMessage> Messages
        {
            get
            {
                lock (_Lock) return _Messages.ToList();
            }
        }

        /// <summary>
        /// Messages to one token.
        /// </summary>
        /// <param name="to">Token value.</param>
        /// <returns>Messages.</returns>
        public List<PushMessage> To(string to)
        {
            lock (_Lock) return _Messages.Where(m => m.To == to).ToList();
        }

        /// <summary>
        /// Answer messages to a token with an error ticket (for example DeviceNotRegistered).
        /// </summary>
        /// <param name="to">Token value.</param>
        /// <param name="error">Error code.</param>
        public void ScriptTicketError(string to, string error)
        {
            lock (_Lock) _TicketsByToken[to] = new PushTicket { Status = "error", Message = error, Error = error };
        }

        /// <summary>
        /// Answer receipts of tickets for a token with an error receipt.
        /// </summary>
        /// <param name="to">Token value.</param>
        /// <param name="error">Error code.</param>
        public void ScriptReceiptError(string to, string error)
        {
            lock (_Lock) _ReceiptsByToken[to] = new PushReceipt { Status = "error", Message = error, Error = error };
        }

        /// <summary>
        /// Forget every recorded message.
        /// </summary>
        public void Clear()
        {
            lock (_Lock) _Messages.Clear();
        }

        /// <inheritdoc />
        public Task<List<PushTicket>> SendAsync(List<PushMessage> messages, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(messages);
            if (FailSends) throw new HttpRequestException("Scripted push failure.");
            List<PushTicket> tickets = new List<PushTicket>();
            lock (_Lock)
            {
                foreach (PushMessage message in messages)
                {
                    _Messages.Add(message);
                    if (_TicketsByToken.TryGetValue(message.To, out PushTicket? scripted))
                    {
                        tickets.Add(new PushTicket { Status = scripted.Status, Message = scripted.Message, Error = scripted.Error });
                        continue;
                    }
                    string id = "tkt-" + Guid.NewGuid().ToString("N");
                    _TicketToToken[id] = message.To;
                    tickets.Add(new PushTicket { Status = "ok", Id = id });
                }
            }
            return Task.FromResult(tickets);
        }

        /// <inheritdoc />
        public Task<Dictionary<string, PushReceipt>> GetReceiptsAsync(List<string> ticketIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ticketIds);
            Dictionary<string, PushReceipt> result = new Dictionary<string, PushReceipt>(StringComparer.Ordinal);
            lock (_Lock)
            {
                ReceiptRequests++;
                foreach (string id in ticketIds)
                {
                    if (!_TicketToToken.TryGetValue(id, out string? to)) continue;
                    result[id] = _ReceiptsByToken.TryGetValue(to, out PushReceipt? scripted) ? scripted : new PushReceipt { Status = "ok" };
                }
            }
            return Task.FromResult(result);
        }
    }
}
