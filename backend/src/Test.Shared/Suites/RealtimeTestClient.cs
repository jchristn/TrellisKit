namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.WebSockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;

    /// <summary>
    /// A realtime test client over System.Net.WebSockets.ClientWebSocket (reused by the messaging suites through
    /// <see cref="RealtimeTestKit"/>). A background pump reads whole frames into a queue (receives are never cancelled,
    /// because cancelling a pending receive aborts the socket); <see cref="Pause"/> stops reading so the server's writes
    /// back up (backpressure tests). The close code the server sent is kept in <see cref="CloseCode"/>.
    /// </summary>
    internal sealed class RealtimeTestClient : IAsyncDisposable
    {
        internal ClientWebSocket Socket { get; }

        /// <summary>
        /// Close code received from the server (1006 for an abnormal end), or null while open.
        /// </summary>
        internal int? CloseCode
        {
            get { int code = Volatile.Read(ref _Close); return code == 0 ? null : code; }
        }

        /// <summary>
        /// Close reason received from the server.
        /// </summary>
        internal string? CloseReason { get; private set; } = null;

        /// <summary>
        /// The welcome frame's data after <see cref="RealtimeTestKit.HelloAsync"/>.
        /// </summary>
        internal JsonElement Welcome { get; set; }

        internal string ConnectionId
        {
            get { return Welcome.ValueKind == JsonValueKind.Object ? Welcome.GetProperty("connectionId").GetString()! : String.Empty; }
        }

        private readonly Channel<JsonElement> _Frames = Channel.CreateUnbounded<JsonElement>();
        private readonly TaskCompletionSource<bool> _Closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _PauseLock = new object();
        private TaskCompletionSource<bool>? _Resume = null;
        private int _Close = 0;
        private Task? _Pump = null;

        internal RealtimeTestClient(ClientWebSocket socket)
        {
            Socket = socket;
        }

        internal void StartPump()
        {
            _Pump ??= Task.Run(PumpAsync);
        }

        /// <summary>
        /// Stop reading from the socket (frames back up in the server's queue and the TCP buffers).
        /// </summary>
        internal void Pause()
        {
            lock (_PauseLock) _Resume ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// Resume reading.
        /// </summary>
        internal void Resume()
        {
            TaskCompletionSource<bool>? resume;
            lock (_PauseLock)
            {
                resume = _Resume;
                _Resume = null;
            }
            resume?.TrySetResult(true);
        }

        internal Task SendAsync(object frame, CancellationToken ct)
        {
            return SendTextAsync(JsonSerializer.Serialize(frame), ct);
        }

        internal async Task SendTextAsync(string text, CancellationToken ct)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            await Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }

        internal async Task SendBinaryAsync(byte[] bytes, CancellationToken ct)
        {
            await Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Binary, true, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// The next frame within a timeout, or null when the socket closed or nothing arrived.
        /// </summary>
        internal async Task<JsonElement?> NextAsync(TimeSpan timeout, CancellationToken ct)
        {
            using (CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                limit.CancelAfter(timeout);
                try
                {
                    if (await _Frames.Reader.WaitToReadAsync(limit.Token).ConfigureAwait(false) && _Frames.Reader.TryRead(out JsonElement frame)) return frame;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    return null;
                }
                catch (ChannelClosedException)
                {
                    return null;
                }
                return null;
            }
        }

        /// <summary>
        /// Read frames until one of a type arrives (others are skipped); fails on close or timeout.
        /// </summary>
        internal async Task<JsonElement> ExpectAsync(string type, TimeSpan timeout, CancellationToken ct, List<string>? skipped = null)
        {
            DateTime until = DateTime.UtcNow + timeout;
            while (true)
            {
                TimeSpan left = until - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) throw new InvalidOperationException("No " + type + " frame within " + timeout.TotalSeconds + " s (close " + CloseCode + " " + CloseReason + ").");
                JsonElement? frame = await NextAsync(left, ct).ConfigureAwait(false);
                if (frame == null)
                {
                    if (CloseCode != null) throw new InvalidOperationException("The socket closed with " + CloseCode + " (" + CloseReason + ") while waiting for " + type + ".");
                    continue;
                }
                string? got = frame.Value.GetProperty("type").GetString();
                if (got == type) return frame.Value;
                skipped?.Add(got ?? String.Empty);
            }
        }

        /// <summary>
        /// The data of the next frame of a type.
        /// </summary>
        internal async Task<JsonElement> ExpectDataAsync(string type, TimeSpan timeout, CancellationToken ct)
        {
            JsonElement frame = await ExpectAsync(type, timeout, ct).ConfigureAwait(false);
            return frame.GetProperty("data");
        }

        /// <summary>
        /// Read until the socket closes; returns the close code, or null when it stayed open.
        /// </summary>
        internal async Task<int?> WaitCloseAsync(TimeSpan timeout, CancellationToken ct)
        {
            Task done = await Task.WhenAny(_Closed.Task, Task.Delay(timeout, ct)).ConfigureAwait(false);
            return done == _Closed.Task ? CloseCode : null;
        }

        /// <summary>
        /// Whether no frame of a type arrives within a window (other frames are consumed).
        /// </summary>
        internal async Task<bool> NoneAsync(string type, TimeSpan window, CancellationToken ct)
        {
            DateTime until = DateTime.UtcNow + window;
            while (DateTime.UtcNow < until)
            {
                JsonElement? frame = await NextAsync(until - DateTime.UtcNow, ct).ConfigureAwait(false);
                if (frame == null) return true;
                if (frame.Value.GetProperty("type").GetString() == type) return false;
            }
            return true;
        }

        public async ValueTask DisposeAsync()
        {
            Resume();
            try
            {
                if (Socket.State == WebSocketState.Open)
                {
                    using (CancellationTokenSource limit = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                    {
                        await Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "test done", limit.Token).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception)
            {
                // Best effort.
            }
            if (_Pump != null)
            {
                await Task.WhenAny(_Pump, Task.Delay(3000)).ConfigureAwait(false);
            }
            Socket.Dispose();
        }

        private async Task PumpAsync()
        {
            byte[] buffer = new byte[64 * 1024];
            try
            {
                while (true)
                {
                    Task<bool>? gate;
                    lock (_PauseLock) gate = _Resume?.Task;
                    if (gate != null) await gate.ConfigureAwait(false);
                    using (MemoryStream message = new MemoryStream())
                    {
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await Socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None).ConfigureAwait(false);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                CloseReason = result.CloseStatusDescription;
                                Interlocked.CompareExchange(ref _Close, result.CloseStatus != null ? (int)result.CloseStatus.Value : 1005, 0);
                                try
                                {
                                    if (Socket.State == WebSocketState.CloseReceived) await Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "ack", CancellationToken.None).ConfigureAwait(false);
                                }
                                catch (Exception)
                                {
                                    // The server may already be gone.
                                }
                                return;
                            }
                            message.Write(buffer, 0, result.Count);
                        }
                        while (!result.EndOfMessage);
                        using (JsonDocument document = JsonDocument.Parse(message.ToArray()))
                        {
                            await _Frames.Writer.WriteAsync(document.RootElement.Clone()).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (Exception)
            {
                Interlocked.CompareExchange(ref _Close, 1006, 0);
            }
            finally
            {
                _Frames.Writer.TryComplete();
                _Closed.TrySetResult(true);
            }
        }
    }
}
