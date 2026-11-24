namespace TrellisKit.Server.Routes
{
    using System;
    using System.Globalization;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Observability;
    using TrellisKit.Server.Serialization;
    using TrellisKit.Server.Services.Ai;
    using WatsonWebserver.Core;

    /// <summary>
    /// Server-sent events over Watson chunked responses (docs/AI_CONTRACT.md section 10.1): text/event-stream with
    /// no-store caching and proxy buffering off, each event framed as "event: name / id: n / data: one-line JSON", a
    /// ": ping" comment every heartbeat interval, and one flush per event. A client that went away never fails the
    /// writer: later events are dropped and generation continues.
    /// Thread safety: writes are serialized.
    /// </summary>
    public sealed class SseWriter : IChatEventSink, IAsyncDisposable
    {
        #region Public-Members

        /// <summary>
        /// Whether the client is still connected (as far as the last write knows).
        /// </summary>
        public bool Connected
        {
            get { return !_Disconnected; }
        }

        #endregion

        #region Private-Members

        private readonly HttpContextBase _Ctx;
        private readonly SemaphoreSlim _Write = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _Stop = new CancellationTokenSource();
        private readonly int _HeartbeatSeconds;
        private Task? _Heartbeat = null;
        private long _Sequence = 0;
        private bool _Disconnected = false;
        private bool _Completed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the writer.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="heartbeatSeconds">Seconds between keep-alive comments (5 to 60).</param>
        /// <exception cref="ArgumentNullException">The context is null.</exception>
        public SseWriter(HttpContextBase ctx, int heartbeatSeconds)
        {
            _Ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _HeartbeatSeconds = Math.Clamp(heartbeatSeconds, 5, 60);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Send the response headers and start the heartbeat.
        /// </summary>
        /// <returns>Task.</returns>
        public async Task StartAsync()
        {
            _Ctx.Response.StatusCode = 200;
            _Ctx.Response.ContentType = "text/event-stream; charset=utf-8";
            _Ctx.Response.Headers["Cache-Control"] = "no-store";
            _Ctx.Response.Headers["X-Accel-Buffering"] = "no";
            _Ctx.Response.Headers["Connection"] = "keep-alive";
            _Ctx.Response.ChunkedTransfer = true;
            AiTelemetry.StreamDelta(1);
            await WriteAsync(": stream\n\n").ConfigureAwait(false);
            _Heartbeat = Task.Run(HeartbeatAsync);
        }

        /// <inheritdoc />
        public Task EmitAsync(string name, object data)
        {
            long id = Interlocked.Increment(ref _Sequence);
            string frame = "event: " + name + "\nid: " + id.ToString(CultureInfo.InvariantCulture) + "\ndata: " + ApiJson.Serialize(data) + "\n\n";
            return WriteAsync(frame);
        }

        /// <summary>
        /// Stop the heartbeat and end the response.
        /// </summary>
        /// <returns>Task.</returns>
        public async Task CompleteAsync()
        {
            if (_Completed) return;
            _Completed = true;
            _Stop.Cancel();
            if (_Heartbeat != null)
            {
                try
                {
                    await _Heartbeat.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Stopped.
                }
            }
            await _Write.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!_Disconnected) await _Ctx.Response.SendChunk(Array.Empty<byte>(), true, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                _Disconnected = true;
            }
            finally
            {
                _Write.Release();
                AiTelemetry.StreamDelta(-1);
            }
        }

        /// <summary>
        /// Complete the stream if needed and release resources.
        /// </summary>
        /// <returns>Task.</returns>
        public async ValueTask DisposeAsync()
        {
            await CompleteAsync().ConfigureAwait(false);
            _Stop.Dispose();
            _Write.Dispose();
        }

        #endregion

        #region Private-Methods

        private async Task WriteAsync(string text)
        {
            if (_Disconnected || _Completed) return;
            await _Write.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_Disconnected || _Completed) return;
                bool ok = await _Ctx.Response.SendChunk(Encoding.UTF8.GetBytes(text), false, CancellationToken.None).ConfigureAwait(false);
                if (!ok) _Disconnected = true;
            }
            catch (Exception)
            {
                _Disconnected = true;
            }
            finally
            {
                _Write.Release();
            }
        }

        private async Task HeartbeatAsync()
        {
            while (!_Stop.IsCancellationRequested && !_Disconnected)
            {
                await Task.Delay(TimeSpan.FromSeconds(_HeartbeatSeconds), _Stop.Token).ConfigureAwait(false);
                await WriteAsync(": ping\n\n").ConfigureAwait(false);
            }
        }

        #endregion
    }
}
