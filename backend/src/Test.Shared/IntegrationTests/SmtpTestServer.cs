namespace Test.Shared.IntegrationTests
{
    using System;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A minimal in-process SMTP server on 127.0.0.1 that accepts one session (no TLS, no AUTH) and captures the message.
    /// </summary>
    public sealed class SmtpTestServer : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Listening port.
        /// </summary>
        public int Port { get; }

        /// <summary>
        /// MAIL FROM argument of the captured session.
        /// </summary>
        public string MailFrom { get; private set; } = String.Empty;

        /// <summary>
        /// RCPT TO argument of the captured session.
        /// </summary>
        public string RcptTo { get; private set; } = String.Empty;

        /// <summary>
        /// DATA section of the captured session (headers and body).
        /// </summary>
        public string Data { get; private set; } = String.Empty;

        #endregion

        #region Private-Members

        private readonly TcpListener _Listener;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Start listening on an ephemeral loopback port.
        /// </summary>
        public SmtpTestServer()
        {
            _Listener = new TcpListener(IPAddress.Loopback, 0);
            _Listener.Start();
            Port = ((IPEndPoint)_Listener.LocalEndpoint).Port;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Serve one SMTP session.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task that completes when the client quits or disconnects.</returns>
        public async Task ServeOneAsync(CancellationToken token)
        {
            using (TcpClient client = await _Listener.AcceptTcpClientAsync(token).ConfigureAwait(false))
            using (NetworkStream stream = client.GetStream())
            using (StreamReader reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
            using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true))
            {
                writer.NewLine = "\r\n";
                writer.AutoFlush = true;
                await writer.WriteLineAsync("220 127.0.0.1 ESMTP test").ConfigureAwait(false);
                while (!token.IsCancellationRequested)
                {
                    string? line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                    if (line == null) return;
                    string upper = line.ToUpperInvariant();
                    if (upper.StartsWith("EHLO", StringComparison.Ordinal) || upper.StartsWith("HELO", StringComparison.Ordinal))
                    {
                        await writer.WriteLineAsync("250-127.0.0.1").ConfigureAwait(false);
                        await writer.WriteLineAsync("250 8BITMIME").ConfigureAwait(false);
                    }
                    else if (upper.StartsWith("MAIL FROM:", StringComparison.Ordinal))
                    {
                        MailFrom = line.Substring(10).Trim();
                        await writer.WriteLineAsync("250 OK").ConfigureAwait(false);
                    }
                    else if (upper.StartsWith("RCPT TO:", StringComparison.Ordinal))
                    {
                        RcptTo = line.Substring(8).Trim();
                        await writer.WriteLineAsync("250 OK").ConfigureAwait(false);
                    }
                    else if (upper == "DATA")
                    {
                        await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>").ConfigureAwait(false);
                        StringBuilder data = new StringBuilder();
                        while (true)
                        {
                            string? dataLine = await reader.ReadLineAsync(token).ConfigureAwait(false);
                            if (dataLine == null || dataLine == ".") break;
                            data.Append(dataLine.StartsWith("..", StringComparison.Ordinal) ? dataLine.Substring(1) : dataLine).Append("\r\n");
                        }
                        Data = data.ToString();
                        await writer.WriteLineAsync("250 OK queued").ConfigureAwait(false);
                    }
                    else if (upper == "QUIT")
                    {
                        await writer.WriteLineAsync("221 Bye").ConfigureAwait(false);
                        return;
                    }
                    else
                    {
                        await writer.WriteLineAsync("250 OK").ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// Stop listening.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Listener.Stop();
            _Listener.Dispose();
            _Disposed = true;
        }

        #endregion
    }
}
