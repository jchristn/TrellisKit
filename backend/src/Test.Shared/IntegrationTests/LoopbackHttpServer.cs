namespace Test.Shared.IntegrationTests
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Minimal HTTP/1.1 server on 127.0.0.1 for real-socket link preview fetch tests. Responses are scripted per path;
    /// each connection serves one request and closes. Records the request line and Host header of every request.
    /// </summary>
    internal sealed class LoopbackHttpServer : IAsyncDisposable
    {
        private readonly TcpListener _Listener;
        private readonly CancellationTokenSource _Stop = new CancellationTokenSource();
        private readonly ConcurrentDictionary<string, Func<byte[]>> _Routes = new ConcurrentDictionary<string, Func<byte[]>>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, int> _Delays = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        private readonly ConcurrentQueue<string> _Requests = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> _UserAgents = new ConcurrentQueue<string>();
        private int _Cookies = 0;
        private readonly Task _Loop;

        internal LoopbackHttpServer()
        {
            _Listener = new TcpListener(IPAddress.Loopback, 0);
            _Listener.Start();
            Port = ((IPEndPoint)_Listener.LocalEndpoint).Port;
            _Loop = Task.Run(AcceptAsync);
        }

        internal int Port { get; }

        internal List<string> Requests
        {
            get { return new List<string>(_Requests); }
        }

        internal List<string> UserAgents
        {
            get { return new List<string>(_UserAgents); }
        }

        internal int Cookies
        {
            get { return _Cookies; }
        }

        internal void Html(string path, string html, string contentType = "text/html; charset=utf-8")
        {
            Raw(path, 200, contentType, Encoding.UTF8.GetBytes(html));
        }

        internal void Raw(string path, int status, string contentType, byte[] body, string? extraHeaders = null)
        {
            _Routes[path] = () =>
            {
                string head = "HTTP/1.1 " + status + " " + (status == 200 ? "OK" : "Status") + "\r\nContent-Type: " + contentType + "\r\nContent-Length: " + body.Length +
                    "\r\nConnection: close\r\n" + (extraHeaders ?? String.Empty) + "\r\n";
                byte[] headBytes = Encoding.ASCII.GetBytes(head);
                byte[] all = new byte[headBytes.Length + body.Length];
                Buffer.BlockCopy(headBytes, 0, all, 0, headBytes.Length);
                Buffer.BlockCopy(body, 0, all, headBytes.Length, body.Length);
                return all;
            };
        }

        internal void Redirect(string path, string location)
        {
            Raw(path, 302, "text/plain", Array.Empty<byte>(), "Location: " + location + "\r\n");
        }

        internal void Delay(string path, int milliseconds)
        {
            _Delays[path] = milliseconds;
        }

        public async ValueTask DisposeAsync()
        {
            _Stop.Cancel();
            _Listener.Stop();
            try
            {
                await _Loop.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Shutdown.
            }
            _Stop.Dispose();
        }

        private async Task AcceptAsync()
        {
            while (!_Stop.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _Listener.AcceptTcpClientAsync(_Stop.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }
                _ = Task.Run(() => ServeAsync(client));
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    NetworkStream stream = client.GetStream();
                    StringBuilder head = new StringBuilder();
                    byte[] one = new byte[1];
                    while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                    {
                        int read = await stream.ReadAsync(one, 0, 1, _Stop.Token).ConfigureAwait(false);
                        if (read == 0) return;
                        head.Append((char)one[0]);
                        if (head.Length > 16384) return;
                    }
                    string[] lines = head.ToString().Split("\r\n");
                    string[] requestLine = lines[0].Split(' ');
                    string path = requestLine.Length > 1 ? requestLine[1] : "/";
                    string host = String.Empty;
                    foreach (string line in lines)
                    {
                        if (line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase)) host = line.Substring(5).Trim();
                        if (line.StartsWith("User-Agent:", StringComparison.OrdinalIgnoreCase)) _UserAgents.Enqueue(line.Substring(11).Trim());
                        if (line.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase)) Interlocked.Increment(ref _Cookies);
                    }
                    _Requests.Enqueue(path + " host=" + host);
                    if (_Delays.TryGetValue(path, out int delay)) await Task.Delay(delay, _Stop.Token).ConfigureAwait(false);
                    byte[] response = _Routes.TryGetValue(path, out Func<byte[]>? route)
                        ? route()
                        : Encoding.ASCII.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(response, 0, response.Length, _Stop.Token).ConfigureAwait(false);
                    await stream.FlushAsync(_Stop.Token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is IOException || e is OperationCanceledException || e is SocketException || e is ObjectDisposedException)
                {
                    // Client went away or the server is stopping.
                }
            }
        }
    }
}
