namespace Test.Shared.IntegrationTests
{
    using System;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// SMTP email sender against an in-process SMTP listener.
    /// </summary>
    public static class EmailSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "IntegrationEmail";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "SMTP email", new[]
            {
                Case("NotConfigured", "Without a host the sender reports not configured and refuses to send", async ct =>
                {
                    EmailSettings settings = new EmailSettings();
                    SmtpEmailSender sender = new SmtpEmailSender(settings, "Example Community", "no-reply@example.test");
                    Check.True(!sender.IsConfigured, "Not configured");
                    await Check.ThrowsAsync<IntegrationNotConfiguredException>(() => sender.SendAsync(Message(), ct), "Send").ConfigureAwait(false);
                    settings.SmtpHost = "127.0.0.1";
                    Check.True(sender.IsConfigured, "Configured after a live change");
                }),
                Case("Send.Multipart", "A message with text and HTML is delivered as multipart/alternative from the configured sender", async ct =>
                {
                    using (SmtpTestServer server = new SmtpTestServer())
                    using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        timeout.CancelAfter(TimeSpan.FromSeconds(20));
                        Task serving = server.ServeOneAsync(timeout.Token);
                        EmailSettings settings = new EmailSettings { SmtpHost = "127.0.0.1", SmtpPort = server.Port, UseTls = false, FromAddress = String.Empty, FromName = String.Empty };
                        SmtpEmailSender sender = new SmtpEmailSender(settings, "Example Community", "no-reply@example.test");
                        await sender.SendAsync(Message(), timeout.Token).ConfigureAwait(false);
                        await serving.ConfigureAwait(false);

                        Check.True(server.MailFrom.Contains("no-reply@example.test", StringComparison.Ordinal), "MAIL FROM");
                        Check.True(server.RcptTo.Contains("member@example.com", StringComparison.Ordinal), "RCPT TO");
                        Check.True(server.Data.Contains("Subject: Verify your email", StringComparison.Ordinal), "Subject");
                        string fromLine = server.Data.Split("\r\n").FirstOrDefault(l => l.StartsWith("From:", StringComparison.Ordinal)) ?? String.Empty;
                        Check.True(fromLine.Contains("Example Community", StringComparison.Ordinal) && fromLine.Contains("<no-reply@example.test>", StringComparison.Ordinal), "From header");
                        Check.True(server.Data.Contains("multipart/alternative", StringComparison.OrdinalIgnoreCase), "multipart/alternative");
                        Check.True(server.Data.Contains("text/plain", StringComparison.OrdinalIgnoreCase) && server.Data.Contains("text/html", StringComparison.OrdinalIgnoreCase), "Both parts");
                    }
                }),
                Case("Send.Failure", "A connection failure becomes InvalidOperationException without credentials", async ct =>
                {
                    int port;
                    TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
                    probe.Start();
                    port = ((IPEndPoint)probe.LocalEndpoint).Port;
                    probe.Stop();
                    probe.Dispose();

                    EmailSettings settings = new EmailSettings { SmtpHost = "127.0.0.1", SmtpPort = port, UseTls = false, Username = "smtp-user", Password = "smtp-password-secret" };
                    SmtpEmailSender sender = new SmtpEmailSender(settings);
                    InvalidOperationException e = await Check.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(Message(), ct), "Refused").ConfigureAwait(false);
                    Check.True(e.Message.Contains("127.0.0.1:" + port, StringComparison.Ordinal), "Names the server");
                    Check.True(!e.Message.Contains("smtp-password-secret", StringComparison.Ordinal), "No password");
                })
            });
        }

        #endregion

        #region Private-Methods

        private static EmailMessage Message()
        {
            return new EmailMessage
            {
                To = "member@example.com",
                Subject = "Verify your email",
                TextBody = "Open https://example.test/verify-email?token=abc to verify.",
                HtmlBody = "<p>Open <a href=\"https://example.test/verify-email?token=abc\">this link</a> to verify.</p>",
                Category = "verify_email"
            };
        }

        private static TestCaseDescriptor Case(string caseId, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(Id, caseId, name, body, new[] { "fast", "unit", "integrations" });
        }

        #endregion
    }
}
