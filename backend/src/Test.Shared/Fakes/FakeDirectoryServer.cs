namespace Test.Shared.Fakes
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
    /// A loopback HTTP server for the directory integration tests (geocoders and the tile proxy): answers by path
    /// (without the query string) and records every request line with its headers. Never a real provider.
    /// </summary>
    internal sealed class FakeDirectoryServer : IAsyncDisposable
    {
        private readonly TcpListener _Listener;
        private readonly CancellationTokenSource _Stop = new CancellationTokenSource();
        private readonly ConcurrentDictionary<string, Func<string, byte[]>> _Routes = new ConcurrentDictionary<string, Func<string, byte[]>>(StringComparer.Ordinal);
        private readonly ConcurrentQueue<string> _Requests = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> _Headers = new ConcurrentQueue<string>();
        private readonly Task _Loop;

        internal FakeDirectoryServer()
        {
            _Listener = new TcpListener(IPAddress.Loopback, 0);
            _Listener.Start();
            Port = ((IPEndPoint)_Listener.LocalEndpoint).Port;
            _Loop = Task.Run(AcceptAsync);
        }

        internal int Port { get; }

        internal string BaseUrl
        {
            get { return "http://127.0.0.1:" + Port; }
        }

        internal List<string> Requests
        {
            get { return new List<string>(_Requests); }
        }

        internal List<string> Headers
        {
            get { return new List<string>(_Headers); }
        }

        internal void Json(string path, string json)
        {
            Respond(path, 200, "application/json", _ => json);
        }

        internal void Respond(string path, int status, string contentType, Func<string, string> body)
        {
            _Routes[path] = target =>
            {
                byte[] bytes = Encoding.UTF8.GetBytes(body(target));
                string head = "HTTP/1.1 " + status + " " + (status == 200 ? "OK" : "Status") + "\r\nContent-Type: " + contentType + "\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\n\r\n";
                byte[] h = Encoding.ASCII.GetBytes(head);
                byte[] all = new byte[h.Length + bytes.Length];
                Buffer.BlockCopy(h, 0, all, 0, h.Length);
                Buffer.BlockCopy(bytes, 0, all, h.Length, bytes.Length);
                return all;
            };
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
                    string target = requestLine.Length > 1 ? requestLine[1] : "/";
                    _Requests.Enqueue(target);
                    _Headers.Enqueue(String.Join("\n", lines));
                    string path = target.Split('?')[0];
                    byte[] response = _Routes.TryGetValue(path, out Func<string, byte[]>? route)
                        ? route(target)
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
