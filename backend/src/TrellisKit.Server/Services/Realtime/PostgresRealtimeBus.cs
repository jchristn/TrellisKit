namespace TrellisKit.Server.Services.Realtime
{
    using System;
    using System.Diagnostics;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Database.Postgresql;
    using TrellisKit.Core.Observability;

    /// <summary>
    /// The default realtime bus (docs/PHASE5_CONTRACT.md section 4.11 and as-built note A): one dedicated,
    /// deployment-level Npgsql connection runs LISTEN on <see cref="Channel"/> (tk_realtime, suffixed with _&lt;schema&gt;
    /// when database.schema is set, so deployments and parallel test schemas sharing one database never cross talk).
    /// Writers append <see cref="NotifyStatement"/> to a write's transaction batch so an event is delivered only when the
    /// write commits; <see cref="PublishAsync"/> issues pg_notify directly. When the listener connection drops, the bus
    /// reconnects with backoff (1 second doubling to 30 seconds) and raises <see cref="Reconnected"/> because NOTIFY is
    /// not durable. While idle it samples pg_notification_queue_usage() into trelliskit.realtime.bus.queue_usage.
    /// The listener connection is a reviewed unbound connection (docs/MULTIPROPERTY_CONTRACT.md section 4.3 rule 1): it
    /// reads no table, and every payload names its property, which receivers check before any property-bound read.
    /// Thread safety: safe to use concurrently; events are raised on the listener loop in arrival order.
    /// </summary>
    public sealed class PostgresRealtimeBus : IRealtimeBus
    {
        #region Public-Members

        /// <summary>
        /// Base channel name.
        /// </summary>
        public const string BaseChannel = "tk_realtime";

        /// <summary>
        /// Largest serialized payload in bytes (PostgreSQL allows 8000; the contract caps payloads at 1 KB).
        /// </summary>
        public const int MaxPayloadBytes = 1024;

        /// <inheritdoc />
        public string Mode
        {
            get { return "Postgres"; }
        }

        /// <summary>
        /// The channel this bus listens and notifies on.
        /// </summary>
        public string Channel { get; }

        /// <summary>
        /// Whether the listener connection is currently listening.
        /// </summary>
        public bool IsListening
        {
            get { return _Listening; }
        }

        /// <summary>
        /// Backend process id of the listener connection, or null when not connected (tests terminate it with
        /// pg_terminate_backend to exercise reconnects).
        /// </summary>
        public int? ListenerProcessId
        {
            get { return _ProcessId; }
        }

        /// <summary>
        /// Seconds the listener waits for a notification before sampling the queue usage (liveness check). Default 15.
        /// Minimum 1, maximum 300.
        /// </summary>
        public int IdleCheckSeconds
        {
            get { return _IdleCheckSeconds; }
            set { _IdleCheckSeconds = Math.Clamp(value, 1, 300); }
        }

        /// <inheritdoc />
        public event Func<RealtimeEvent, Task>? EventReceived;

        /// <inheritdoc />
        public event Func<Task>? Reconnected;

        #endregion

        #region Private-Members

        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        private readonly string _ConnectionString;
        private readonly DatabaseDriverBase _Database;
        private readonly AppLogger _Logger;
        private readonly object _StartLock = new object();
        private CancellationTokenSource? _Source = null;
        private Task? _Loop = null;
        private volatile bool _Listening = false;
        private int? _ProcessId = null;
        private int _IdleCheckSeconds = 15;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the bus. Call <see cref="StartAsync"/> to begin listening.
        /// </summary>
        /// <param name="settings">Database settings (host, credentials, schema).</param>
        /// <param name="database">Deployment driver (pg_notify for <see cref="PublishAsync"/>).</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public PostgresRealtimeBus(DatabaseSettings settings, DatabaseDriverBase database, AppLogger logger)
        {
            ArgumentNullException.ThrowIfNull(settings);
            _Database = (database ?? throw new ArgumentNullException(nameof(database))).Deployment;
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _ConnectionString = PostgresqlDatabaseDriver.DedicatedConnectionString(settings);
            Channel = ChannelFor(settings.Schema);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The channel name for a schema: tk_realtime, or tk_realtime_&lt;schema&gt; (lowercase, characters outside a to z,
        /// 0 to 9, and underscore replaced by underscores, at most 63 characters).
        /// </summary>
        /// <param name="schema">database.schema, or null.</param>
        /// <returns>Channel.</returns>
        public static string ChannelFor(string? schema)
        {
            if (String.IsNullOrWhiteSpace(schema)) return BaseChannel;
            StringBuilder builder = new StringBuilder(BaseChannel + "_");
            foreach (char c in schema.Trim().ToLowerInvariant())
                builder.Append((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' ? c : '_');
            string channel = builder.ToString();
            return channel.Length > 63 ? channel.Substring(0, 63) : channel;
        }

        /// <summary>
        /// Serialize an event to its bus payload (single-letter keys, nulls omitted).
        /// </summary>
        /// <param name="realtimeEvent">Event.</param>
        /// <returns>JSON.</returns>
        /// <exception cref="ArgumentNullException">The event is null.</exception>
        public static string Serialize(RealtimeEvent realtimeEvent)
        {
            ArgumentNullException.ThrowIfNull(realtimeEvent);
            return JsonSerializer.Serialize(realtimeEvent, _Json);
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentException">The payload is larger than <see cref="MaxPayloadBytes"/>.</exception>
        public string? NotifyStatement(RealtimeEvent realtimeEvent)
        {
            ArgumentNullException.ThrowIfNull(realtimeEvent);
            string payload = Serialize(realtimeEvent);
            if (Encoding.UTF8.GetByteCount(payload) > MaxPayloadBytes)
                throw new ArgumentException("A realtime event payload is larger than " + MaxPayloadBytes + " bytes; send references only.", nameof(realtimeEvent));
            return "SELECT pg_notify('" + Channel + "', '" + payload.Replace("'", "''", StringComparison.Ordinal) + "');";
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentException">The payload is larger than <see cref="MaxPayloadBytes"/>.</exception>
        public async Task PublishAsync(RealtimeEvent realtimeEvent, CancellationToken token = default)
        {
            string statement = NotifyStatement(realtimeEvent)!;
            await _Database.ExecuteQueryAsync(statement, false, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken token = default)
        {
            lock (_StartLock)
            {
                if (_Disposed) throw new ObjectDisposedException(nameof(PostgresRealtimeBus));
                if (_Loop != null) return Task.CompletedTask;
                _Source = CancellationTokenSource.CreateLinkedTokenSource(token);
                CancellationToken loopToken = _Source.Token;
                _Loop = Task.Run(() => ListenLoopAsync(loopToken), CancellationToken.None);
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Wait until the listener is listening (startup and tests).
        /// </summary>
        /// <param name="timeout">Longest wait.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when listening.</returns>
        public async Task<bool> WaitUntilListeningAsync(TimeSpan timeout, CancellationToken token = default)
        {
            DateTime until = DateTime.UtcNow + timeout;
            while (!_Listening && DateTime.UtcNow < until) await Task.Delay(25, token).ConfigureAwait(false);
            return _Listening;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            Task? loop;
            lock (_StartLock)
            {
                if (_Disposed) return;
                _Disposed = true;
                _Source?.Cancel();
                loop = _Loop;
            }
            if (loop != null)
            {
                try
                {
                    await loop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected on shutdown.
                }
            }
            _Source?.Dispose();
            EventReceived = null;
            Reconnected = null;
        }

        #endregion

        #region Private-Methods

        private async Task ListenLoopAsync(CancellationToken token)
        {
            bool listenedBefore = false;
            int backoffMs = 1000;
            while (!token.IsCancellationRequested)
            {
                NpgsqlConnection? connection = null;
                try
                {
                    connection = new NpgsqlConnection(_ConnectionString);
                    await connection.OpenAsync(token).ConfigureAwait(false);
                    connection.Notification += OnNotification;
                    await using (NpgsqlCommand listen = new NpgsqlCommand("LISTEN \"" + Channel + "\";", connection))
                    {
                        await listen.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                    _ProcessId = connection.ProcessID;
                    _Listening = true;
                    backoffMs = 1000;
                    if (listenedBefore)
                    {
                        RealtimeTelemetry.Count(RealtimeTelemetry.BusReconnects);
                        _Logger.Warn("[PostgresRealtimeBus] listener reconnected on " + Channel + "; clients resync");
                        await RaiseReconnectedAsync().ConfigureAwait(false);
                    }
                    listenedBefore = true;
                    while (!token.IsCancellationRequested)
                    {
                        bool received = await connection.WaitAsync(TimeSpan.FromSeconds(_IdleCheckSeconds), token).ConfigureAwait(false);
                        if (received) continue;
                        await using (NpgsqlCommand usage = new NpgsqlCommand("SELECT pg_notification_queue_usage();", connection))
                        {
                            object? value = await usage.ExecuteScalarAsync(token).ConfigureAwait(false);
                            if (value is double fraction) RealtimeTelemetry.SetQueueUsage(fraction);
                        }
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception e)
                {
                    if (_Listening || !listenedBefore) _Logger.Warn("[PostgresRealtimeBus] listener on " + Channel + " failed: " + e.Message);
                }
                finally
                {
                    _Listening = false;
                    _ProcessId = null;
                    if (connection != null)
                    {
                        connection.Notification -= OnNotification;
                        try
                        {
                            await connection.DisposeAsync().ConfigureAwait(false);
                        }
                        catch (Exception)
                        {
                            // The connection is already broken.
                        }
                    }
                }
                try
                {
                    await Task.Delay(backoffMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                backoffMs = Math.Min(backoffMs * 2, 30000);
            }
        }

        private void OnNotification(object sender, NpgsqlNotificationEventArgs args)
        {
            if (!String.Equals(args.Channel, Channel, StringComparison.Ordinal)) return;
            using (Activity? activity = TrellisKitTelemetry.StartSpan("realtime.bus.receive"))
            {
                RealtimeEvent? realtimeEvent;
                try
                {
                    realtimeEvent = JsonSerializer.Deserialize<RealtimeEvent>(args.Payload, _Json);
                }
                catch (JsonException)
                {
                    realtimeEvent = null;
                }
                if (realtimeEvent == null || realtimeEvent.Version != 1 || String.IsNullOrEmpty(realtimeEvent.PropertyId) || String.IsNullOrEmpty(realtimeEvent.Kind))
                {
                    activity?.SetTag("outcome", "malformed");
                    return;
                }
                activity?.SetTag("realtime.kind", realtimeEvent.Kind);
                activity?.SetTag("realtime.event_id", realtimeEvent.EventId);
                double lag = (DateTime.UtcNow - realtimeEvent.PublishedUtc.ToUniversalTime()).TotalSeconds;
                TrellisKitTelemetry.Record(RealtimeTelemetry.BusLag, Math.Max(0, lag), "kind", realtimeEvent.Kind);
                Func<RealtimeEvent, Task>? handlers = EventReceived;
                if (handlers == null) return;
                foreach (Func<RealtimeEvent, Task> handler in handlers.GetInvocationList())
                {
                    try
                    {
                        Task pending = handler(realtimeEvent);
                        if (!pending.IsCompleted) _ = pending.ContinueWith(t => _Logger.Warn("[PostgresRealtimeBus] receiver failed: " + t.Exception?.GetBaseException().Message), TaskContinuationOptions.OnlyOnFaulted);
                    }
                    catch (Exception e)
                    {
                        _Logger.Warn("[PostgresRealtimeBus] receiver failed: " + e.Message);
                    }
                }
            }
        }

        private async Task RaiseReconnectedAsync()
        {
            Func<Task>? handlers = Reconnected;
            if (handlers == null) return;
            foreach (Func<Task> handler in handlers.GetInvocationList())
            {
                try
                {
                    await handler().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    _Logger.Warn("[PostgresRealtimeBus] reconnect handler failed: " + e.Message);
                }
            }
        }

        #endregion
    }
}
