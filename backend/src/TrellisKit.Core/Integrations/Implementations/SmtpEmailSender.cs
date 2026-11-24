namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using MailKit;
    using MailKit.Net.Smtp;
    using MailKit.Security;
    using MimeKit;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Sends email over SMTP with MailKit: STARTTLS when <see cref="EmailSettings.UseTls"/> is true (otherwise no TLS),
    /// authentication only when a username is set, and a multipart/alternative body when HTML is supplied.
    /// Each message uses its own connection. Settings are read on every call. Thread safe.
    /// </summary>
    public class SmtpEmailSender : IEmailSender
    {
        #region Public-Members

        /// <summary>
        /// Timeout for each SMTP network operation, in milliseconds. Default 30000. Minimum 1000, maximum 300000.
        /// </summary>
        public int TimeoutMs
        {
            get { return _TimeoutMs; }
            set { _TimeoutMs = Math.Clamp(value, 1000, 300000); }
        }

        /// <inheritdoc />
        public bool IsConfigured
        {
            get { return !String.IsNullOrWhiteSpace(_Settings.SmtpHost); }
        }

        #endregion

        #region Private-Members

        private readonly EmailSettings _Settings;
        private readonly string _DefaultFromName;
        private readonly string _DefaultFromAddress;
        private int _TimeoutMs = 30000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the client.
        /// </summary>
        /// <param name="settings">Email settings (live instance).</param>
        /// <param name="defaultFromName">Sender name used when <see cref="EmailSettings.FromName"/> is empty (the property contact.emailFromName).</param>
        /// <param name="defaultFromAddress">Sender address used when <see cref="EmailSettings.FromAddress"/> is empty (the property contact.emailFromAddress).</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public SmtpEmailSender(EmailSettings settings, string defaultFromName = "", string defaultFromAddress = "")
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _DefaultFromName = defaultFromName ?? String.Empty;
            _DefaultFromAddress = defaultFromAddress ?? String.Empty;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException">The message is null.</exception>
        /// <exception cref="ArgumentException">The recipient is empty or not a valid address.</exception>
        public async Task SendAsync(EmailMessage message, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(message);
            string host = (_Settings.SmtpHost ?? String.Empty).Trim();
            if (host.Length < 1) throw new IntegrationNotConfiguredException("Email is not configured: set Email SmtpHost.");
            if (String.IsNullOrWhiteSpace(message.To)) throw new ArgumentException("The message has no recipient.", nameof(message));

            int port = _Settings.SmtpPort;
            bool useTls = _Settings.UseTls;
            string username = (_Settings.Username ?? String.Empty).Trim();
            string password = _Settings.Password ?? String.Empty;

            MimeMessage mime = BuildMessage(message);
            using (mime)
            using (SmtpClient client = new SmtpClient())
            {
                client.Timeout = _TimeoutMs;
                try
                {
                    await client.ConnectAsync(host, port, useTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, token).ConfigureAwait(false);
                    if (username.Length > 0) await client.AuthenticateAsync(username, password, token).ConfigureAwait(false);
                    await client.SendAsync(mime, token).ConfigureAwait(false);
                    await client.DisconnectAsync(true, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (AuthenticationException e)
                {
                    throw new InvalidOperationException("SMTP authentication with " + host + ":" + port + " failed for the configured username.", e);
                }
                catch (Exception e) when (!(e is OutOfMemoryException))
                {
                    throw new InvalidOperationException("Email delivery through " + host + ":" + port + " failed (" + e.GetType().Name + ": " + e.Message + ").", e);
                }
            }
        }

        #endregion

        #region Private-Methods

        private MimeMessage BuildMessage(EmailMessage message)
        {
            MailboxAddress to;
            MailboxAddress from;
            try
            {
                to = MailboxAddress.Parse(message.To.Trim());
            }
            catch (ParseException e)
            {
                throw new ArgumentException("The recipient is not a valid email address.", nameof(message), e);
            }
            try
            {
                string fromName = !String.IsNullOrWhiteSpace(message.FromName) ? message.FromName : String.IsNullOrWhiteSpace(_Settings.FromName) ? _DefaultFromName : _Settings.FromName;
                string fromAddress = !String.IsNullOrWhiteSpace(message.FromAddress) ? message.FromAddress : String.IsNullOrWhiteSpace(_Settings.FromAddress) ? _DefaultFromAddress : _Settings.FromAddress;
                from = new MailboxAddress(fromName, fromAddress.Trim());
            }
            catch (ParseException e)
            {
                throw new InvalidOperationException("The configured Email FromAddress is not a valid email address.", e);
            }

            MimeMessage mime = new MimeMessage();
            mime.From.Add(from);
            mime.To.Add(to);
            mime.Subject = message.Subject ?? String.Empty;
            if (!String.IsNullOrWhiteSpace(message.ReplyTo) && MailboxAddress.TryParse(message.ReplyTo.Trim(), out MailboxAddress? replyTo)) mime.ReplyTo.Add(replyTo);
            foreach (KeyValuePair<string, string> header in message.Headers)
            {
                if (String.IsNullOrWhiteSpace(header.Key)) continue;
                mime.Headers.Add(header.Key, header.Value ?? String.Empty);
            }

            BodyBuilder builder = new BodyBuilder { TextBody = message.TextBody ?? String.Empty };
            if (!String.IsNullOrEmpty(message.HtmlBody)) builder.HtmlBody = message.HtmlBody;
            mime.Body = builder.ToMessageBody();
            return mime;
        }

        #endregion
    }
}
