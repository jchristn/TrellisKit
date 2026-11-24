namespace Test.Shared.Fakes
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Email sender that keeps every message in memory so tests can read links.
    /// </summary>
    public sealed class InMemoryEmailSender : IEmailSender
    {
        private static readonly Regex _Token = new Regex("token=([A-Za-z0-9_%-]+)", RegexOptions.Compiled);
        private readonly ConcurrentQueue<EmailMessage> _Messages = new ConcurrentQueue<EmailMessage>();

        /// <inheritdoc />
        public bool IsConfigured
        {
            get { return true; }
        }

        /// <summary>
        /// Every message sent, oldest first.
        /// </summary>
        public IReadOnlyList<EmailMessage> Messages
        {
            get { return _Messages.ToList(); }
        }

        /// <inheritdoc />
        public Task SendAsync(EmailMessage message, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(message);
            _Messages.Enqueue(message);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Messages to a recipient in a category.
        /// </summary>
        /// <param name="to">Recipient.</param>
        /// <param name="category">Category.</param>
        /// <returns>Messages.</returns>
        public List<EmailMessage> To(string to, string category)
        {
            return _Messages.Where(m => String.Equals(m.To, to, StringComparison.OrdinalIgnoreCase) && m.Category == category).ToList();
        }

        /// <summary>
        /// The token from the latest message to a recipient in a category.
        /// </summary>
        /// <param name="to">Recipient.</param>
        /// <param name="category">Category.</param>
        /// <returns>Token.</returns>
        /// <exception cref="InvalidOperationException">No such message.</exception>
        public string LatestToken(string to, string category)
        {
            EmailMessage message = To(to, category).LastOrDefault() ?? throw new InvalidOperationException("No " + category + " email to " + to + ".");
            Match match = _Token.Match(message.TextBody);
            if (!match.Success) throw new InvalidOperationException("No token in the " + category + " email.");
            return Uri.UnescapeDataString(match.Groups[1].Value);
        }
    }
}
