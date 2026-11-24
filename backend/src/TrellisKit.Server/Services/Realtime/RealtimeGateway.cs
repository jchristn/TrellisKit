namespace TrellisKit.Server.Services.Realtime
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Net.WebSockets;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Routes;
    using TrellisKit.Server.Serialization;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.WebSockets;

    /// <summary>
    /// The deployment-level WebSocket gateway of one server task (docs/PHASE5_CONTRACT.md section 4 and as-built note
    /// A): pre-upgrade checks run from the server's pre-routing hook; GET /api/v1/ws then upgrades through Watson and
    /// <see cref="HandleAsync"/> runs the connection (hello within realtime.authTimeoutSeconds, frame parsing and
    /// limits, auth.expiring and auth.refresh, state, the inbound handler seam, the application heartbeat). Bus events
    /// (<see cref="IRealtimeBus"/>) and local deliveries are dispatched on 8 ordered lanes (partitioned by conversation,
    /// else recipient) to the sockets of the event's property only, rendered per recipient through that property's
    /// <see cref="RealtimeRendererRegistry"/>. A sweep loop enforces the hello timeout, silence drop, token expiry,
    /// kill switches, disabled properties, slow consumers, and the 60 second registry touch and session re-validation.
    /// <see cref="DrainAsync"/> implements SIGTERM draining (shutdown, then 1012 over realtime.drainSeconds).
    /// Thread safety: safe to use concurrently.
    /// </summary>
    public sealed class RealtimeGateway : IRealtimeLocalSink, IAsyncDisposable
    {
        #region Public-Members

        /// <summary>
        /// WebSocket route path.
        /// </summary>
        public const string Path = Constants.ApiPrefix + "/ws";

        /// <summary>
        /// Protocol version accepted in hello.protocol.
        /// </summary>
        public const string ProtocolVersion = "tk.v1";

        /// <summary>
        /// This task's id (realtimeconnections.taskid).
        /// </summary>
        public string TaskId { get; } = "tsk_" + Guid.NewGuid().ToString("N");

        /// <summary>
        /// Whether the task is draining (no new upgrades; health reports Draining).
        /// </summary>
        public bool IsDraining
        {
            get { return _Draining; }
        }

        /// <summary>
        /// Open sockets (authenticated or waiting for hello).
        /// </summary>
        public int ConnectionCount
        {
            get { return _Connections.Count; }
        }

        /// <summary>
        /// Test and diagnostics override of realtime.heartbeatSeconds (welcome.heartbeatSeconds and the silence drop),
        /// or null. Minimum 1, maximum 55.
        /// </summary>
        public int? HeartbeatSecondsOverride
        {
            get { return _HeartbeatOverride; }
            set { _HeartbeatOverride = value == null ? null : Math.Clamp(value.Value, 1, 55); }
        }

        /// <summary>
        /// Test and diagnostics override of realtime.slowConsumerSeconds, or null. Minimum 1, maximum 300.
        /// </summary>
        public int? SlowConsumerSecondsOverride
        {
            get { return _SlowOverride; }
            set { _SlowOverride = value == null ? null : Math.Clamp(value.Value, 1, 300); }
        }

        /// <summary>
        /// Sweep interval in milliseconds. Default 1000. Minimum 100, maximum 10000.
        /// </summary>
        public int SweepIntervalMs
        {
            get { return _SweepIntervalMs; }
            set { _SweepIntervalMs = Math.Clamp(value, 100, 10000); }
        }

        /// <summary>
        /// Seconds between registry touches and session re-validations of a connection. Default 60. Minimum 1, maximum 3600.
        /// </summary>
        public int RevalidateSeconds
        {
            get { return _RevalidateSeconds; }
            set { _RevalidateSeconds = Math.Clamp(value, 1, 3600); }
        }

        /// <summary>
        /// Seconds between kill switch evaluations of a connection. Default 2. Minimum 1, maximum 60.
        /// </summary>
        public int GateCheckSeconds
        {
            get { return _GateCheckSeconds; }
            set { _GateCheckSeconds = Math.Clamp(value, 1, 60); }
        }

        #endregion

        #region Private-Members

        private const int _LaneCount = 8;
        private const int _MaxOutboundFrameBytes = 65536;
        private static readonly string[] _KnownFrameTypes = { "hello", "auth.refresh", "ping", "state", "typing", "ack", "read" };
        private readonly AppSettings _Settings;
        private readonly PropertyDirectory _Directory;
        private readonly IRealtimeBus _Bus;
        private readonly AppLogger _Logger;
        private readonly ConcurrentDictionary<string, RealtimeConnection> _Connections = new ConcurrentDictionary<string, RealtimeConnection>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, RealtimeConnection>> _ByProperty = new ConcurrentDictionary<string, ConcurrentDictionary<string, RealtimeConnection>>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, RealtimeConnection>> _ByUser = new ConcurrentDictionary<string, ConcurrentDictionary<string, RealtimeConnection>>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, int> _PendingByIp = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, DateTime> _RecentEvents = new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, int> _RecentCloses = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        private readonly SlidingWindowRateLimiter _UpgradeLimiter = new SlidingWindowRateLimiter();
        private readonly Channel<RealtimeEvent>[] _Lanes = new Channel<RealtimeEvent>[_LaneCount];
        private readonly List<Task> _LaneTasks = new List<Task>();
        private readonly CancellationTokenSource _Lifetime = new CancellationTokenSource();
        private readonly object _StartLock = new object();
        private Task? _Sweep = null;
        private volatile bool _Draining = false;
        private bool _Started = false;
        private bool _Disposed = false;
        private int? _HeartbeatOverride = null;
        private int? _SlowOverride = null;
        private int _SweepIntervalMs = 1000;
        private int _RevalidateSeconds = 60;
        private int _GateCheckSeconds = 2;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the gateway. Call <see cref="Start"/> before serving.
        /// </summary>
        /// <param name="settings">Running settings (realtime.* is read live where the contract says Live).</param>
        /// <param name="directory">Property directory (resolution of sockets and events).</param>
        /// <param name="bus">Deployment-level realtime bus.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public RealtimeGateway(AppSettings settings, PropertyDirectory directory, IRealtimeBus bus, AppLogger logger)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _Bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            for (int i = 0; i < _LaneCount; i++) _Lanes[i] = Channel.CreateUnbounded<RealtimeEvent>(new UnboundedChannelOptions { SingleReader = true });
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a request path is the WebSocket route.
        /// </summary>
        /// <param name="path">Request path without query.</param>
        /// <returns>True for /api/v1/ws.</returns>
        public static bool IsPath(string? path)
        {
            return String.Equals((path ?? String.Empty).TrimEnd('/'), Path, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Subscribe to the bus and start the dispatch lanes and the sweep loop (idempotent).
        /// </summary>
        /// <param name="token">Lifetime token.</param>
        public void Start(CancellationToken token)
        {
            lock (_StartLock)
            {
                if (_Started || _Disposed) return;
                _Started = true;
                _Bus.EventReceived += OnBusEventAsync;
                _Bus.Reconnected += OnBusReconnectedAsync;
                CancellationToken lifetime = _Lifetime.Token;
                token.Register(() => _Lifetime.Cancel());
                for (int i = 0; i < _LaneCount; i++)
                {
                    ChannelReader<RealtimeEvent> reader = _Lanes[i].Reader;
                    _LaneTasks.Add(Task.Run(() => LaneLoopAsync(reader, lifetime), CancellationToken.None));
                }
                _Sweep = Task.Run(() => SweepLoopAsync(lifetime), CancellationToken.None);
            }
        }

        /// <summary>
        /// The pre-upgrade checks of GET /api/v1/ws (run from the server's pre-routing hook after property resolution):
        /// a plain GET gets 426; a present Origin must be a web origin of the property (403 OriginNotAllowed); the
        /// realtime kill switch or realtime.enabled false gives 503 FeatureDisabled; draining and capacity give 503 with
        /// Retry-After; anonymous upgrades per address per minute and pending hellos per address give 429 with
        /// Retry-After; Sec-WebSocket-Protocol gives 400 SubprotocolNotSupported. A refused upgrade gets a normal HTTP
        /// error and never reaches Watson's handshake.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="runtime">Resolved property.</param>
        /// <returns>True when a response was sent (refused); false to continue to the WebSocket route.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<bool> CheckUpgradeAsync(HttpContextBase ctx, PropertyRuntime runtime)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            ArgumentNullException.ThrowIfNull(runtime);
            RealtimeSettings rt = _Settings.Realtime;
            string? upgrade = ctx.Request.RetrieveHeaderValue("Upgrade");
            if (upgrade == null || upgrade.IndexOf("websocket", StringComparison.OrdinalIgnoreCase) < 0)
            {
                ctx.Response.Headers["Upgrade"] = "websocket";
                await RouteHelper.SendErrorAsync(ctx, 426, "UpgradeRequired", "GET /api/v1/ws is a WebSocket endpoint; send an RFC 6455 upgrade.").ConfigureAwait(false);
                return true;
            }
            string? origin = ctx.Request.RetrieveHeaderValue("Origin");
            if (!String.IsNullOrWhiteSpace(origin) && !OriginAllowed(runtime, origin))
            {
                RealtimeTelemetry.Count(RealtimeTelemetry.Connects, "outcome", "rejected_origin");
                await RouteHelper.SendErrorAsync(ctx, 403, "OriginNotAllowed", "This origin may not open a realtime connection to this property.").ConfigureAwait(false);
                return true;
            }
            string? killed = await runtime.Services.Realtime.KillSwitchMessageAsync(Path, null, name => ctx.Request.RetrieveHeaderValue(name), ctx.Token).ConfigureAwait(false);
            if (killed != null || !rt.Enabled)
            {
                RealtimeTelemetry.Count(RealtimeTelemetry.Connects, "outcome", "disabled");
                ctx.Response.Headers["Retry-After"] = "300";
                await RouteHelper.SendErrorAsync(ctx, 503, "FeatureDisabled", killed ?? "Realtime connections are turned off; poll the REST routes instead.").ConfigureAwait(false);
                return true;
            }
            if (_Draining)
            {
                RealtimeTelemetry.Count(RealtimeTelemetry.Connects, "outcome", "over_capacity");
                ctx.Response.Headers["Retry-After"] = "5";
                await RouteHelper.SendErrorAsync(ctx, 503, "ServiceUnavailable", "This server is restarting; reconnect shortly.").ConfigureAwait(false);
                return true;
            }
            if (_Connections.Count >= rt.MaxConnectionsPerTask)
            {
                RealtimeTelemetry.Count(RealtimeTelemetry.Connects, "outcome", "over_capacity");
                ctx.Response.Headers["Retry-After"] = "30";
                await RouteHelper.SendErrorAsync(ctx, 503, "ServiceUnavailable", "This server holds its maximum number of realtime connections; retry later.").ConfigureAwait(false);
                return true;
            }
            string ip = ClientAddressResolver.Resolve(ctx, _Settings.Rest) ?? ctx.Request.Source?.IpAddress ?? "unknown";
            if (!_UpgradeLimiter.TryAcquire("upgrade:" + ip, rt.AnonymousUpgradesPerMinute, TimeSpan.FromMinutes(1), DateTime.UtcNow, out int retryAfter))
            {
                RealtimeTelemetry.Count(RealtimeTelemetry.Connects, "outcome", "rate_limited");
                ctx.Response.Headers["Retry-After"] = Math.Max(1, retryAfter).ToString(CultureInfo.InvariantCulture);
                await RouteHelper.SendErrorAsync(ctx, 429, "RateLimited", "Too many realtime upgrades from this address.").ConfigureAwait(false);
                return true;
            }
            if (_PendingByIp.TryGetValue(ip, out int pending) && pending >= rt.MaxPendingHellos)
            {
                RealtimeTelemetry.Count(RealtimeTelemetry.Connects, "outcome", "rate_limited");
                ctx.Response.Headers["Retry-After"] = rt.AuthTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
                await RouteHelper.SendErrorAsync(ctx, 429, "RateLimited", "Too many realtime connections from this address are waiting for hello.").ConfigureAwait(false);
                return true;
            }
            if (!String.IsNullOrEmpty(ctx.Request.RetrieveHeaderValue("Sec-WebSocket-Protocol")))
            {
                RealtimeTelemetry.Count(RealtimeTelemetry.Connects, "outcome", "protocol");
                await RouteHelper.SendErrorAsync(ctx, 400, "SubprotocolNotSupported", "Request no subprotocol; send the protocol version in hello.protocol (tk.v1).").ConfigureAwait(false);
                return true;
            }
            return false;
        }

        /// <summary>
        /// The Watson WebSocket handler of GET /api/v1/ws: runs one connection until it closes.
        /// </summary>
        /// <param name="ctx">HTTP context of the upgrade.</param>
        /// <param name="session">WebSocket session.</param>
        /// <returns>Task that completes when the connection ends.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task HandleAsync(HttpContextBase ctx, WebSocketSession session)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            ArgumentNullException.ThrowIfNull(session);
            PropertyRuntime? runtime = _Directory.ResolutionOf(ctx)?.Runtime;
            if (runtime == null)
            {
                await CloseQuietlyAsync(session, RealtimeCloseCodes.PropertyMismatch, "Property not found.").ConfigureAwait(false);
                return;
            }
            TrellisKitTelemetry.CurrentProperty = runtime.Id;
            NameValueCollection headers = new NameValueCollection(StringComparer.OrdinalIgnoreCase);
            foreach (string? key in ctx.Request.Headers.AllKeys)
                if (key != null) headers[key] = ctx.Request.Headers[key];
            string ip = ClientAddressResolver.Resolve(ctx, _Settings.Rest) ?? session.RemoteIp;
            RealtimeConnection connection = new RealtimeConnection(IdGenerator.Generate("rtc_"), runtime, session, headers, ip, _Settings.Realtime.MaxInboundFramesPerSecond * 2);
            _Connections[connection.ConnectionId] = connection;
            _ByProperty.GetOrAdd(runtime.Id, k => new ConcurrentDictionary<string, RealtimeConnection>(StringComparer.Ordinal))[connection.ConnectionId] = connection;
            _PendingByIp.AddOrUpdate(ip, 1, (k, v) => v + 1);
            connection.StartWriter();
            try
            {
                await ReceiveLoopAsync(connection).ConfigureAwait(false);
            }
            finally
            {
                await CleanupAsync(connection).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public void DispatchLocal(RealtimeEvent realtimeEvent)
        {
            ArgumentNullException.ThrowIfNull(realtimeEvent);
            if (String.IsNullOrEmpty(realtimeEvent.PropertyId) || String.IsNullOrEmpty(realtimeEvent.Kind)) return;
            string key = realtimeEvent.PropertyId + "|" + (realtimeEvent.ConversationId ?? realtimeEvent.UserIds.FirstOrDefault() ?? realtimeEvent.SessionId ?? realtimeEvent.EventId);
            int lane = (int)((uint)StringComparer.Ordinal.GetHashCode(key) % _LaneCount);
            _Lanes[lane].Writer.TryWrite(realtimeEvent);
        }

        /// <inheritdoc />
        public bool HasLocalConnection(string propertyId, string userId)
        {
            if (String.IsNullOrEmpty(propertyId) || String.IsNullOrEmpty(userId)) return false;
            return _ByUser.TryGetValue(propertyId + "|" + userId, out ConcurrentDictionary<string, RealtimeConnection>? map) && map.Values.Any(c => c.Authenticated && !c.Closing);
        }

        /// <summary>
        /// The ids of a member's authenticated sockets on this task (diagnostics and tests).
        /// </summary>
        /// <param name="propertyId">Property id.</param>
        /// <param name="userId">Member.</param>
        /// <returns>Connection ids, oldest first.</returns>
        public List<string> ConnectionIdsOf(string propertyId, string userId)
        {
            if (!_ByUser.TryGetValue(propertyId + "|" + userId, out ConcurrentDictionary<string, RealtimeConnection>? map)) return new List<string>();
            return map.Values.Where(c => c.Authenticated).OrderBy(c => c.ConnectedUtc).Select(c => c.ConnectionId).ToList();
        }

        /// <summary>
        /// The close code a recently ended connection was closed with (by the server, or the client's code), or null.
        /// Kept for the last 1000 connections (diagnostics and tests).
        /// </summary>
        /// <param name="connectionId">Connection id.</param>
        /// <returns>Code, or null.</returns>
        public int? CloseCodeOf(string connectionId)
        {
            if (String.IsNullOrEmpty(connectionId)) return null;
            if (_RecentCloses.TryGetValue(connectionId, out int code)) return code;
            if (_Connections.TryGetValue(connectionId, out RealtimeConnection? open)) return open.RequestedCloseCode;
            return null;
        }

        /// <summary>
        /// The access token expiry the gateway tracks for a connection, or null (diagnostics and tests).
        /// </summary>
        /// <param name="connectionId">Connection id.</param>
        /// <returns>UTC expiry, or null.</returns>
        public DateTime? TokenExpiryOf(string connectionId)
        {
            return _Connections.TryGetValue(connectionId ?? String.Empty, out RealtimeConnection? connection) && connection.Authenticated ? connection.TokenExpiresUtc : null;
        }

        /// <summary>
        /// Send resync.required with a reason to every authenticated socket (the bus reconnected).
        /// </summary>
        /// <param name="reason">Gap, Overflow, BusReconnected, or Resume.</param>
        public void ResyncAll(string reason)
        {
            foreach (RealtimeConnection connection in _Connections.Values.Where(c => c.Authenticated && !c.Closing).ToList())
            {
                Send(connection, "resync.required", new Dictionary<string, object?> { ["reason"] = reason }, null, null, false);
                RealtimeTelemetry.Count(RealtimeTelemetry.Resyncs, "reason", reason);
            }
        }

        /// <summary>
        /// Drain the task (SIGTERM, docs/PHASE5_CONTRACT.md section 4.11): stop accepting upgrades (health reports
        /// Draining), send shutdown with a jittered reconnectAfterMs (0 to 5000), close every socket with 1012 spread over
        /// realtime.drainSeconds, and delete this task's registry rows. Idempotent.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task DrainAsync(CancellationToken token = default)
        {
            if (_Draining) return;
            _Draining = true;
            List<RealtimeConnection> connections = _Connections.Values.OrderBy(c => c.OpenedUtc).ToList();
            _Logger.Info("[RealtimeGateway] draining " + connections.Count + " sockets over " + _Settings.Realtime.DrainSeconds + " s");
            foreach (RealtimeConnection connection in connections.Where(c => c.Authenticated && !c.Closing))
                Send(connection, "shutdown", new Dictionary<string, object?> { ["reconnectAfterMs"] = Random.Shared.Next(0, 5001) }, null, null, false);
            int totalMs = _Settings.Realtime.DrainSeconds * 1000;
            Stopwatch clock = Stopwatch.StartNew();
            for (int i = 0; i < connections.Count; i++)
            {
                long due = connections.Count <= 1 ? 0 : (long)totalMs * i / connections.Count;
                long wait = due - clock.ElapsedMilliseconds;
                if (wait > 0)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(wait), token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
                connections[i].RequestClose(RealtimeCloseCodes.Restart, "Service restart");
            }
            foreach (RealtimeConnection connection in connections) connection.RequestClose(RealtimeCloseCodes.Restart, "Service restart");
            DateTime until = DateTime.UtcNow.AddMilliseconds(Math.Max(2000, totalMs - clock.ElapsedMilliseconds + 2000));
            while (!_Connections.IsEmpty && DateTime.UtcNow < until && !token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(50, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            try
            {
                await _Directory.Database.RealtimeConnections.DeleteForTaskAsync(TaskId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _Logger.Warn("[RealtimeGateway] registry cleanup on drain failed: " + e.Message);
            }
        }

        /// <summary>
        /// Stop the loops and unsubscribe from the bus. Open sockets are closed by Watson when it stops.
        /// </summary>
        /// <returns>Task.</returns>
        public async ValueTask DisposeAsync()
        {
            lock (_StartLock)
            {
                if (_Disposed) return;
                _Disposed = true;
            }
            _Bus.EventReceived -= OnBusEventAsync;
            _Bus.Reconnected -= OnBusReconnectedAsync;
            try
            {
                _Lifetime.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already cancelled.
            }
            foreach (Channel<RealtimeEvent> lane in _Lanes) lane.Writer.TryComplete();
            List<Task> tasks = new List<Task>(_LaneTasks);
            if (_Sweep != null) tasks.Add(_Sweep);
            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
            foreach (RealtimeConnection connection in _Connections.Values) connection.Stop();
        }

        #endregion

        #region Private-Methods

        private int HeartbeatSeconds
        {
            get { return _HeartbeatOverride ?? _Settings.Realtime.HeartbeatSeconds; }
        }

        private bool OriginAllowed(PropertyRuntime runtime, string origin)
        {
            string normalized = origin.Trim().TrimEnd('/');
            if (_Settings.Cors.ResolveAllowedOrigin(normalized) != null) return true;
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri)) return false;
            if (!String.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)) return false;
            string host = PropertyDirectory.NormalizeHost(uri.Authority);
            if (host.Length == 0) return false;
            string web = PropertyDirectory.NormalizeHost(runtime.Configuration.Domains?.Web);
            if (web.Length > 0 && String.Equals(web, host, StringComparison.Ordinal)) return true;
            return runtime.Domains.Any(d => d.VerifiedUtc != null && String.Equals(d.Host, host, StringComparison.Ordinal));
        }

        private async Task ReceiveLoopAsync(RealtimeConnection connection)
        {
            while (true)
            {
                WebSocketMessage? message;
                try
                {
                    message = await connection.Session.ReceiveAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }
                if (message == null) return;
                connection.LastInboundUtc = DateTime.UtcNow;
                if (connection.Closing) continue;
                if (message.MessageType == WebSocketMessageType.Close) return;
                if (message.MessageType == WebSocketMessageType.Binary)
                {
                    RealtimeTelemetry.Count(RealtimeTelemetry.Frames, "direction", "in", "type", "binary");
                    connection.RequestClose(RealtimeCloseCodes.UnsupportedData, "Binary frames are not supported.");
                    continue;
                }
                try
                {
                    await ProcessTextAsync(connection, message.Text).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    _Logger.Error("[RealtimeGateway] [p:" + connection.PropertyId + "] frame handling failed on " + connection.ConnectionId, e);
                    connection.RequestClose(RealtimeCloseCodes.ServerError, "Server error.");
                }
            }
        }

        private async Task ProcessTextAsync(RealtimeConnection connection, string text)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(text);
            }
            catch (JsonException)
            {
                RealtimeTelemetry.Count(RealtimeTelemetry.Frames, "direction", "in", "type", "malformed");
                connection.RequestClose(RealtimeCloseCodes.ProtocolViolation, "Malformed JSON.");
                return;
            }
            using (document)
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out JsonElement typeElement) || typeElement.ValueKind != JsonValueKind.String || String.IsNullOrEmpty(typeElement.GetString()))
                {
                    RealtimeTelemetry.Count(RealtimeTelemetry.Frames, "direction", "in", "type", "malformed");
                    connection.RequestClose(RealtimeCloseCodes.ProtocolViolation, "A frame needs a type.");
                    return;
                }
                string type = typeElement.GetString()!;
                string? frameId = null;
                if (root.TryGetProperty("id", out JsonElement idElement))
                {
                    if (idElement.ValueKind == JsonValueKind.String) frameId = idElement.GetString();
                    else if (idElement.ValueKind == JsonValueKind.Number) frameId = idElement.GetRawText();
                    if (frameId != null && frameId.Length > 64) frameId = frameId.Substring(0, 64);
                }
                JsonElement data = root.TryGetProperty("data", out JsonElement dataElement) ? dataElement : default;
                RealtimeTelemetry.Count(RealtimeTelemetry.Frames, "direction", "in", "type", _KnownFrameTypes.Contains(type, StringComparer.Ordinal) ? type : "other");

                if (!connection.Authenticated)
                {
                    if (!String.Equals(type, "hello", StringComparison.Ordinal))
                    {
                        connection.RequestClose(RealtimeCloseCodes.ProtocolViolation, "The first frame must be hello.");
                        return;
                    }
                    await HelloAsync(connection, frameId, data).ConfigureAwait(false);
                    return;
                }

                RealtimeSettings rt = _Settings.Realtime;
                DateTime now = DateTime.UtcNow;
                if (!connection.Bucket.TryTake(rt.MaxInboundFramesPerSecond, rt.MaxInboundFramesPerSecond * 2, now, out int retryAfterMs))
                {
                    RealtimeTelemetry.Count(RealtimeTelemetry.Dropped, "reason", "rate_limited");
                    connection.Violations.Enqueue(now);
                    while (connection.Violations.Count > 0 && connection.Violations.Peek() < now.AddMinutes(-1)) connection.Violations.Dequeue();
                    Send(connection, "error", new Dictionary<string, object?> { ["code"] = "RateLimited", ["message"] = "Too many frames; slow down.", ["frameId"] = frameId, ["retryAfterMs"] = Math.Max(1, retryAfterMs) }, null, null, false);
                    if (connection.Violations.Count >= rt.MaxViolations) connection.RequestClose(RealtimeCloseCodes.RateLimited, "Rate limited.");
                    return;
                }

                switch (type)
                {
                    case "hello":
                        connection.RequestClose(RealtimeCloseCodes.ProtocolViolation, "hello is the first frame only.");
                        return;
                    case "ping":
                        Send(connection, "pong", new Dictionary<string, object?>(), null, null, false);
                        return;
                    case "auth.refresh":
                        await RefreshAsync(connection, frameId, data).ConfigureAwait(false);
                        return;
                    case "state":
                        await StateAsync(connection, frameId, data).ConfigureAwait(false);
                        return;
                }

                ApplicationServices services = connection.Runtime.Services;
                RealtimeInboundHandler? handler = services.Realtime.Inbound.Find(type);
                if (handler == null)
                {
                    if (RealtimeInboundHandlerRegistry.ReservedTypes.Contains(type, StringComparer.Ordinal)) return;
                    Error(connection, "UnknownFrame", "Unknown frame type " + Truncate(type, 40) + ".", frameId);
                    return;
                }
                if (handler.RequiredCapability != null && !connection.HasCapability(handler.RequiredCapability))
                {
                    Error(connection, "CapabilityRequired", "This connection does not hold the " + handler.RequiredCapability + " capability.", frameId);
                    return;
                }
                if (String.Equals(type, "typing", StringComparison.Ordinal) && !TypingAllowed(connection, data, now)) return;
                RealtimeInboundContext context = new RealtimeInboundContext(services, connection, type, frameId, data,
                    (t, d) =>
                    {
                        Send(connection, t, d, null, null, false);
                        return Task.CompletedTask;
                    },
                    (code, message, id) =>
                    {
                        Error(connection, code, message, id);
                        return Task.CompletedTask;
                    });
                try
                {
                    await handler.Handle(context, _Lifetime.Token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _Logger.Warn("[RealtimeGateway] [p:" + connection.PropertyId + "] " + type + " handler failed: " + e.Message);
                    Error(connection, "InternalError", "The frame could not be handled.", frameId);
                }
            }
        }

        private bool TypingAllowed(RealtimeConnection connection, JsonElement data, DateTime now)
        {
            if (data.ValueKind != JsonValueKind.Object) return true;
            string? state = data.TryGetProperty("state", out JsonElement s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            string? conversationId = data.TryGetProperty("conversationId", out JsonElement c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            if (!String.Equals(state, "Start", StringComparison.Ordinal) || String.IsNullOrEmpty(conversationId)) return true;
            if (connection.TypingStarts.TryGetValue(conversationId, out DateTime last) && (now - last).TotalMilliseconds < _Settings.Realtime.TypingMinIntervalMs) return false;
            if (connection.TypingStarts.Count > 200) connection.TypingStarts.Clear();
            connection.TypingStarts[conversationId] = now;
            return true;
        }

        private async Task HelloAsync(RealtimeConnection connection, string? frameId, JsonElement data)
        {
            using (Activity? activity = TrellisKitTelemetry.StartSpan("realtime.hello"))
            {
                activity?.SetTag("realtime.connection_id", connection.ConnectionId);
                ApplicationServices services = connection.Runtime.Services;
                RealtimeSettings rt = _Settings.Realtime;
                if (data.ValueKind != JsonValueKind.Object)
                {
                    connection.RequestClose(RealtimeCloseCodes.ProtocolViolation, "hello needs a data object.");
                    return;
                }
                string? protocol = StringOf(data, "protocol");
                if (protocol != null && !String.Equals(protocol, ProtocolVersion, StringComparison.Ordinal))
                {
                    RealtimeTelemetry.Count(RealtimeTelemetry.Connects, "outcome", "protocol");
                    Error(connection, "ProtocolNotSupported", "Unsupported protocol; this server speaks " + ProtocolVersion + ".", frameId);
                    connection.RequestClose(RealtimeCloseCodes.ProtocolViolation, "Unsupported protocol.");
                    return;
                }
                string? accessToken = StringOf(data, "accessToken");
                SessionPrincipal? principal = String.IsNullOrEmpty(accessToken) ? null : await services.Tokens.ValidateAccessTokenAsync(accessToken, CancellationToken.None).ConfigureAwait(false);
                if (principal == null || principal.PrincipalKind != PrincipalKindEnum.Member || !principal.AgeAttested)
                {
                    RealtimeTelemetry.Count(RealtimeTelemetry.Connects, "outcome", "auth_failed");
                    activity?.SetTag("outcome", "auth_failed");
                    Error(connection, "Unauthorized", "A valid member access token of this property is required.", frameId);
                    connection.RequestClose(RealtimeCloseCodes.Unauthorized, "Unauthorized.");
                    return;
                }
                string? propertyId = StringOf(data, "propertyId");
                if (propertyId != null && !String.Equals(propertyId, connection.PropertyId, StringComparison.Ordinal))
                {
                    RealtimeTelemetry.Count(RealtimeTelemetry.Connects, "outcome", "auth_failed");
                    Error(connection, "PropertyMismatch", "hello.propertyId does not name the property this connection resolved to.", frameId);
                    connection.RequestClose(RealtimeCloseCodes.PropertyMismatch, "Property mismatch.");
                    return;
                }
                if (!services.RateLimiter.TryAcquire("realtime:connect:" + principal.UserId, rt.ConnectsPerMinute, TimeSpan.FromMinutes(1), DateTime.UtcNow, out int retryAfter))
                {
                    RealtimeTelemetry.Count(RealtimeTelemetry.Connects, "outcome", "rate_limited");
                    Send(connection, "error", new Dictionary<string, object?> { ["code"] = "RateLimited", ["message"] = "Too many new connections; retry later.", ["frameId"] = frameId, ["retryAfterMs"] = Math.Max(1, retryAfter) * 1000 }, null, null, false);
                    connection.RequestClose(RealtimeCloseCodes.RateLimited, "Rate limited.");
                    return;
                }
                List<string> requested = RequestedCapabilities(data);
                RealtimeAccess access = await services.Realtime.EvaluateAccessAsync(principal, connection.Header, CancellationToken.None).ConfigureAwait(false);
                List<string> granted = requested.Where(c => access.Capabilities.Contains(c, StringComparer.Ordinal)).ToList();
                if (granted.Count == 0)
                {
                    RealtimeTelemetry.Count(RealtimeTelemetry.Connects, "outcome", "auth_failed");
                    Error(connection, "MembershipRequired", "A membership is required for every requested capability.", frameId);
                    connection.RequestClose(RealtimeCloseCodes.MembershipRequired, "Membership required.");
                    return;
                }
                DateTime now = DateTime.UtcNow;
                connection.UserId = principal.UserId;
                connection.SessionId = principal.SessionId;
                connection.AccessToken = accessToken!;
                connection.Principal = principal;
                connection.TokenExpiresUtc = services.Tokens.ReadAccessTokenExpiry(accessToken!) ?? now.AddMinutes(_Settings.Auth.AccessTokenMinutes);
                connection.ExpiringSent = false;
                connection.Client = ClientOf(StringOf(data, "client") ?? connection.Header("X-Client"));
                string? appState = StringOf(data, "appState");
                connection.AppState = String.Equals(appState, "Background", StringComparison.Ordinal) ? "Background" : "Foreground";
                connection.ConnectedUtc = now;
                connection.LastInboundUtc = now;
                connection.LastRevalidatedUtc = now;
                connection.LastGateCheckUtc = now;
                connection.Requested = requested;
                connection.AccessTier = access.AccessTier;
                connection.MessagingKilled = access.MessagingKilled;
                connection.SetCapabilities(granted);
                connection.Authenticated = true;
                ReleasePending(connection);
                ConcurrentDictionary<string, RealtimeConnection> mine = _ByUser.GetOrAdd(connection.PropertyId + "|" + connection.UserId, k => new ConcurrentDictionary<string, RealtimeConnection>(StringComparer.Ordinal));
                mine[connection.ConnectionId] = connection;
                List<RealtimeConnection> open = mine.Values.Where(c => c.Authenticated && !c.Closing).OrderBy(c => c.ConnectedUtc).ToList();
                for (int i = 0; i < open.Count - rt.MaxConnectionsPerUser; i++)
                {
                    if (open[i] == connection) continue;
                    open[i].RequestClose(RealtimeCloseCodes.Replaced, "Replaced by a newer connection.");
                }
                RealtimeTelemetry.ChangeConnections(connection.Client.ToLowerInvariant(), 1);
                try
                {
                    await services.Realtime.Registry.UpsertAsync(RecordOf(connection), CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    _Logger.Warn("[RealtimeGateway] [p:" + connection.PropertyId + "] registry upsert failed: " + e.Message);
                }
                Dictionary<string, object?> welcome = new Dictionary<string, object?>
                {
                    ["connectionId"] = connection.ConnectionId,
                    ["userId"] = connection.UserId,
                    ["serverTime"] = now,
                    ["heartbeatSeconds"] = HeartbeatSeconds,
                    ["maxInboundFrameBytes"] = rt.MaxInboundFrameBytes,
                    ["capabilities"] = granted,
                    ["accessTier"] = access.AccessTier.ToString(),
                    ["resume"] = new Dictionary<string, object?> { ["accepted"] = false, ["syncRequired"] = true }
                };
                Send(connection, "welcome", welcome, null, null, false);
                RealtimeTelemetry.Count(RealtimeTelemetry.Connects, "outcome", "ok");
                activity?.SetTag("outcome", "ok");
            }
        }

        private async Task RefreshAsync(RealtimeConnection connection, string? frameId, JsonElement data)
        {
            ApplicationServices services = connection.Runtime.Services;
            string? accessToken = data.ValueKind == JsonValueKind.Object ? StringOf(data, "accessToken") : null;
            SessionPrincipal? principal = String.IsNullOrEmpty(accessToken) ? null : await services.Tokens.ValidateAccessTokenAsync(accessToken, CancellationToken.None).ConfigureAwait(false);
            if (principal == null || !String.Equals(principal.SessionId, connection.SessionId, StringComparison.Ordinal) || !String.Equals(principal.UserId, connection.UserId, StringComparison.Ordinal))
            {
                Error(connection, "Unauthorized", "auth.refresh needs a valid access token of the same session.", frameId);
                connection.RequestClose(RealtimeCloseCodes.Unauthorized, "Unauthorized.");
                return;
            }
            connection.AccessToken = accessToken!;
            connection.Principal = principal;
            connection.TokenExpiresUtc = services.Tokens.ReadAccessTokenExpiry(accessToken!) ?? DateTime.UtcNow.AddMinutes(_Settings.Auth.AccessTokenMinutes);
            connection.ExpiringSent = false;
        }

        private async Task StateAsync(RealtimeConnection connection, string? frameId, JsonElement data)
        {
            if (data.ValueKind != JsonValueKind.Object)
            {
                Error(connection, "ValidationFailed", "state needs a data object.", frameId);
                return;
            }
            string? appState = StringOf(data, "appState");
            if (appState != null && appState != "Foreground" && appState != "Background")
            {
                Error(connection, "ValidationFailed", "appState must be Foreground or Background.", frameId);
                return;
            }
            string? active = null;
            if (data.TryGetProperty("activeConversationId", out JsonElement activeElement))
            {
                if (activeElement.ValueKind == JsonValueKind.String) active = activeElement.GetString();
                else if (activeElement.ValueKind != JsonValueKind.Null)
                {
                    Error(connection, "ValidationFailed", "activeConversationId must be a string or null.", frameId);
                    return;
                }
                if (active != null && active.Length > 64)
                {
                    Error(connection, "ValidationFailed", "activeConversationId is too long.", frameId);
                    return;
                }
                connection.ActiveConversationId = String.IsNullOrEmpty(active) ? null : active;
            }
            if (appState != null) connection.AppState = appState;
            try
            {
                await connection.Runtime.Services.Realtime.Registry.UpsertAsync(RecordOf(connection), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _Logger.Warn("[RealtimeGateway] [p:" + connection.PropertyId + "] registry state update failed: " + e.Message);
            }
        }

        private async Task CleanupAsync(RealtimeConnection connection)
        {
            connection.Stop();
            _Connections.TryRemove(connection.ConnectionId, out RealtimeConnection? _);
            if (_ByProperty.TryGetValue(connection.PropertyId, out ConcurrentDictionary<string, RealtimeConnection>? byProperty)) byProperty.TryRemove(connection.ConnectionId, out RealtimeConnection? _);
            if (connection.UserId.Length > 0 && _ByUser.TryGetValue(connection.PropertyId + "|" + connection.UserId, out ConcurrentDictionary<string, RealtimeConnection>? byUser))
            {
                byUser.TryRemove(connection.ConnectionId, out RealtimeConnection? _);
                if (byUser.IsEmpty) _ByUser.TryRemove(connection.PropertyId + "|" + connection.UserId, out ConcurrentDictionary<string, RealtimeConnection>? _);
            }
            ReleasePending(connection);
            int code = connection.RequestedCloseCode ?? (connection.Session.CloseStatus != null ? (int)connection.Session.CloseStatus.Value : RealtimeCloseCodes.Normal);
            if (_RecentCloses.Count > 1000) _RecentCloses.Clear();
            _RecentCloses[connection.ConnectionId] = code;
            RealtimeTelemetry.Count(RealtimeTelemetry.Closes, "code", code.ToString(CultureInfo.InvariantCulture));
            if (!connection.Authenticated) return;
            RealtimeTelemetry.ChangeConnections(connection.Client.ToLowerInvariant(), -1);
            try
            {
                await connection.Runtime.Services.Realtime.Registry.DeleteAsync(connection.ConnectionId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _Logger.Warn("[RealtimeGateway] [p:" + connection.PropertyId + "] registry delete failed: " + e.Message);
            }
        }

        private void ReleasePending(RealtimeConnection connection)
        {
            if (connection.PendingReleased) return;
            connection.PendingReleased = true;
            int remaining = _PendingByIp.AddOrUpdate(connection.RemoteIp, 0, (k, v) => Math.Max(0, v - 1));
            if (remaining == 0) _PendingByIp.TryRemove(new KeyValuePair<string, int>(connection.RemoteIp, 0));
        }

        private void Send(RealtimeConnection connection, string type, object? data, string? conversationId, long? seq, bool droppable)
        {
            string dataJson = data == null ? "{}" : ApiJson.Serialize(data);
            SendRaw(connection, type, dataJson, conversationId, seq, droppable);
        }

        private void SendRaw(RealtimeConnection connection, string type, string dataJson, string? conversationId, long? seq, bool droppable)
        {
            RealtimeSettings rt = _Settings.Realtime;
            RealtimeOutboundFrame frame = new RealtimeOutboundFrame(type, Frame(connection, type, dataJson), conversationId, seq, droppable);
            connection.Enqueue(frame, rt.MaxQueuedFrames, rt.MaxQueuedBytes, () => new RealtimeOutboundFrame("resync.required", Frame(connection, "resync.required", "{\"reason\":\"Overflow\"}"), null, null, false));
        }

        private static string Frame(RealtimeConnection connection, string type, string dataJson)
        {
            return "{\"type\":" + JsonSerializer.Serialize(type) + ",\"id\":\"" + connection.NextFrameId() + "\",\"ts\":\"" +
                DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture) + "\",\"data\":" + dataJson + "}";
        }

        private void Error(RealtimeConnection connection, string code, string message, string? frameId)
        {
            Send(connection, "error", new Dictionary<string, object?> { ["code"] = code, ["message"] = message, ["frameId"] = frameId }, null, null, false);
        }

        private static string? StringOf(JsonElement data, string name)
        {
            if (data.ValueKind != JsonValueKind.Object) return null;
            return data.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }

        private static List<string> RequestedCapabilities(JsonElement data)
        {
            if (!data.TryGetProperty("capabilities", out JsonElement list) || list.ValueKind != JsonValueKind.Array) return RealtimeCapabilities.All.ToList();
            List<string> requested = new List<string>();
            foreach (JsonElement item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) continue;
                string? value = item.GetString();
                if (RealtimeCapabilities.IsKnown(value) && !requested.Contains(value!, StringComparer.Ordinal)) requested.Add(value!);
            }
            return requested;
        }

        private static string ClientOf(string? value)
        {
            if (String.Equals(value, "mobile", StringComparison.OrdinalIgnoreCase)) return "Mobile";
            if (String.Equals(value, "web", StringComparison.OrdinalIgnoreCase)) return "Web";
            return "Other";
        }

        private static string Truncate(string value, int length)
        {
            return value.Length <= length ? value : value.Substring(0, length);
        }

        private RealtimeConnectionRecord RecordOf(RealtimeConnection connection)
        {
            return new RealtimeConnectionRecord
            {
                Id = connection.ConnectionId,
                PropertyId = connection.PropertyId,
                UserId = connection.UserId,
                SessionId = connection.SessionId,
                TaskId = TaskId,
                Client = connection.Client,
                AppState = connection.AppState,
                ActiveConversationId = connection.ActiveConversationId,
                ConnectedUtc = connection.ConnectedUtc,
                LastSeenUtc = DateTime.UtcNow
            };
        }

        private static async Task CloseQuietlyAsync(WebSocketSession session, int code, string reason)
        {
            try
            {
                await session.CloseAsync((WebSocketCloseStatus)code, reason, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Best effort.
            }
        }

        private Task OnBusEventAsync(RealtimeEvent realtimeEvent)
        {
            DispatchLocal(realtimeEvent);
            return Task.CompletedTask;
        }

        private Task OnBusReconnectedAsync()
        {
            ResyncAll("BusReconnected");
            return Task.CompletedTask;
        }

        private async Task LaneLoopAsync(ChannelReader<RealtimeEvent> reader, CancellationToken token)
        {
            try
            {
                while (await reader.WaitToReadAsync(token).ConfigureAwait(false))
                {
                    while (reader.TryRead(out RealtimeEvent? realtimeEvent))
                    {
                        try
                        {
                            await DispatchAsync(realtimeEvent, token).ConfigureAwait(false);
                        }
                        catch (Exception e) when (e is not OperationCanceledException)
                        {
                            _Logger.Warn("[RealtimeGateway] dispatch of " + realtimeEvent.Kind + " failed: " + e.Message);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Shutdown.
            }
            catch (ChannelClosedException)
            {
                // Shutdown.
            }
        }

        private List<RealtimeConnection> TargetsOf(RealtimeEvent realtimeEvent)
        {
            if (realtimeEvent.UserIds.Count == 0)
            {
                if (!_ByProperty.TryGetValue(realtimeEvent.PropertyId, out ConcurrentDictionary<string, RealtimeConnection>? all)) return new List<RealtimeConnection>();
                return all.Values.Where(c => c.Authenticated && !c.Closing).ToList();
            }
            List<RealtimeConnection> targets = new List<RealtimeConnection>();
            foreach (string userId in realtimeEvent.UserIds.Distinct(StringComparer.Ordinal))
            {
                if (!_ByUser.TryGetValue(realtimeEvent.PropertyId + "|" + userId, out ConcurrentDictionary<string, RealtimeConnection>? mine)) continue;
                targets.AddRange(mine.Values.Where(c => c.Authenticated && !c.Closing));
            }
            return targets;
        }

        private async Task DispatchAsync(RealtimeEvent realtimeEvent, CancellationToken token)
        {
            DateTime now = DateTime.UtcNow;
            if (!_RecentEvents.TryAdd(realtimeEvent.EventId, now)) return;
            using (Activity? activity = TrellisKitTelemetry.StartSpan("realtime.dispatch"))
            {
                activity?.SetTag("realtime.kind", realtimeEvent.Kind);
                activity?.SetTag("realtime.event_id", realtimeEvent.EventId);
                PropertyRuntime? runtime = _Directory.Find(realtimeEvent.PropertyId);
                // Only the event's own property is searched for recipients: a payload naming another property's members
                // reaches no socket of this property.
                List<RealtimeConnection> targets = runtime == null ? new List<RealtimeConnection>() : TargetsOf(realtimeEvent).Where(c => ReferenceEquals(c.Runtime, runtime) || c.PropertyId == runtime.Id).ToList();
                if (runtime == null) TrellisKitTelemetry.CurrentProperty = null;
                else TrellisKitTelemetry.CurrentProperty = runtime.Id;
                if (realtimeEvent.Kind == RealtimeEventKinds.SessionRevoked && realtimeEvent.SessionId != null && runtime != null && _ByProperty.TryGetValue(runtime.Id, out ConcurrentDictionary<string, RealtimeConnection>? ofProperty))
                    targets = ofProperty.Values.Where(c => c.Authenticated && !c.Closing && String.Equals(c.SessionId, realtimeEvent.SessionId, StringComparison.Ordinal)).ToList();
                if (runtime == null || targets.Count == 0)
                {
                    RealtimeTelemetry.Count(RealtimeTelemetry.BusEvents, "kind", KindLabel(realtimeEvent.Kind), "outcome", "no_local_recipients");
                    activity?.SetTag("outcome", "no_local_recipients");
                    return;
                }
                RealtimeTelemetry.Count(RealtimeTelemetry.BusEvents, "kind", KindLabel(realtimeEvent.Kind), "outcome", "delivered_local");
                activity?.SetTag("outcome", "delivered_local");
                ApplicationServices services = runtime.Services;
                switch (realtimeEvent.Kind)
                {
                    case RealtimeEventKinds.SessionRevoked:
                        foreach (RealtimeConnection connection in targets)
                        {
                            if (String.Equals(realtimeEvent.State, "PropertyDisabled", StringComparison.Ordinal))
                            {
                                Error(connection, "Unauthorized", "This community is not available.", null);
                                connection.RequestClose(RealtimeCloseCodes.Unauthorized, "Property disabled.");
                                continue;
                            }
                            services.Tokens.DropCachedSession(connection.SessionId);
                            await RevalidateAsync(connection, false).ConfigureAwait(false);
                        }
                        return;
                    case RealtimeEventKinds.AccessChanged:
                        foreach (RealtimeConnection connection in targets)
                        {
                            services.Tokens.DropCachedSession(connection.SessionId);
                            await RevalidateAsync(connection, true).ConfigureAwait(false);
                        }
                        return;
                }
                RealtimeRenderer? renderer = services.Realtime.Renderers.Find(realtimeEvent.Kind);
                if (renderer == null) return;
                foreach (IGrouping<string, RealtimeConnection> recipient in targets.GroupBy(c => c.UserId, StringComparer.Ordinal))
                {
                    List<RealtimeConnection> eligible = recipient.Where(c => renderer.RequiredCapability == null || c.HasCapability(renderer.RequiredCapability)).ToList();
                    if (eligible.Count == 0) continue;
                    object? data;
                    try
                    {
                        data = await renderer.Render(new RealtimeRenderContext(services, realtimeEvent, recipient.Key, services.Realtime.Cache), token).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        _Logger.Warn("[RealtimeGateway] [p:" + runtime.Id + "] render of " + realtimeEvent.Kind + " failed: " + e.Message);
                        continue;
                    }
                    if (data == null) continue;
                    string dataJson = ApiJson.Serialize(data);
                    if (dataJson.Length > _MaxOutboundFrameBytes)
                    {
                        _Logger.Warn("[RealtimeGateway] [p:" + runtime.Id + "] " + realtimeEvent.Kind + " frame over 64 KB dropped; send references");
                        continue;
                    }
                    foreach (RealtimeConnection connection in eligible)
                    {
                        SendRaw(connection, renderer.FrameType, dataJson, realtimeEvent.ConversationId, realtimeEvent.Seq, renderer.Droppable);
                        if (realtimeEvent.Kind == RealtimeEventKinds.FeatureDisabled && String.Equals(realtimeEvent.State, RealtimeCapabilities.Messaging, StringComparison.Ordinal))
                            connection.SetCapabilities(connection.Capabilities.Where(c => c != RealtimeCapabilities.Messaging).ToList());
                    }
                }
            }
        }

        private static string KindLabel(string kind)
        {
            return RealtimeEventKinds.All.Contains(kind, StringComparer.Ordinal) ? kind : "other";
        }

        private async Task RevalidateAsync(RealtimeConnection connection, bool sendAlways)
        {
            if (!connection.Authenticated || connection.Closing) return;
            ApplicationServices services = connection.Runtime.Services;
            SessionPrincipal? principal = await services.Tokens.ValidateAccessTokenAsync(connection.AccessToken, CancellationToken.None).ConfigureAwait(false);
            if (principal == null || !String.Equals(principal.SessionId, connection.SessionId, StringComparison.Ordinal))
            {
                Error(connection, "Unauthorized", "The session ended; sign in again.", null);
                connection.RequestClose(RealtimeCloseCodes.Unauthorized, "Unauthorized.");
                return;
            }
            connection.Principal = principal;
            RealtimeAccess access = await services.Realtime.EvaluateAccessAsync(principal, connection.Header, CancellationToken.None).ConfigureAwait(false);
            List<string> granted = connection.Requested.Where(c => access.Capabilities.Contains(c, StringComparer.Ordinal)).ToList();
            if (granted.Count == 0)
            {
                Error(connection, "MembershipRequired", "A membership is required for every requested capability.", null);
                connection.RequestClose(RealtimeCloseCodes.MembershipRequired, "Membership required.");
                return;
            }
            bool changed = connection.SetCapabilities(granted) || connection.AccessTier != access.AccessTier;
            connection.AccessTier = access.AccessTier;
            connection.MessagingKilled = access.MessagingKilled;
            if (sendAlways || changed)
                Send(connection, "access.changed", new Dictionary<string, object?> { ["capabilities"] = granted, ["accessTier"] = access.AccessTier.ToString() }, null, null, false);
        }

        private async Task SweepLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_SweepIntervalMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                try
                {
                    await SweepAsync().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    _Logger.Warn("[RealtimeGateway] sweep failed: " + e.Message);
                }
            }
        }

        private async Task SweepAsync()
        {
            DateTime now = DateTime.UtcNow;
            RealtimeSettings rt = _Settings.Realtime;
            int slowSeconds = _SlowOverride ?? rt.SlowConsumerSeconds;
            List<Task> work = new List<Task>();
            Dictionary<string, List<string>> touch = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (RealtimeConnection connection in _Connections.Values.ToList())
            {
                if (connection.Closing)
                {
                    if ((now - connection.CloseRequestedUtc).TotalSeconds > 10) work.Add(connection.CloseNowAsync(connection.RequestedCloseCode ?? RealtimeCloseCodes.Normal, "Closed."));
                    continue;
                }
                DateTime? sending = connection.SendStartedUtc;
                if (sending != null && (now - sending.Value).TotalSeconds > slowSeconds)
                {
                    RealtimeTelemetry.Count(RealtimeTelemetry.Dropped, "reason", "slow_consumer");
                    work.Add(connection.CloseNowAsync(RealtimeCloseCodes.TryAgainLater, "Slow consumer."));
                    continue;
                }
                if (!connection.Authenticated)
                {
                    if ((now - connection.OpenedUtc).TotalSeconds > rt.AuthTimeoutSeconds)
                    {
                        RealtimeTelemetry.Count(RealtimeTelemetry.Connects, "outcome", "timeout");
                        connection.RequestClose(RealtimeCloseCodes.HelloTimeout, "hello timeout.");
                    }
                    continue;
                }
                PropertyRuntime? runtime = _Directory.Find(connection.PropertyId);
                if (runtime == null || !runtime.IsEnabled)
                {
                    Error(connection, "Unauthorized", "This community is not available.", null);
                    connection.RequestClose(RealtimeCloseCodes.Unauthorized, "Property disabled.");
                    continue;
                }
                if ((now - connection.LastInboundUtc).TotalSeconds > HeartbeatSeconds * 2 + 5)
                {
                    connection.RequestClose(RealtimeCloseCodes.Normal, "Heartbeat timeout.");
                    continue;
                }
                if (now >= connection.TokenExpiresUtc)
                {
                    Error(connection, "Unauthorized", "The access token expired; refresh it and reconnect.", null);
                    connection.RequestClose(RealtimeCloseCodes.Unauthorized, "Token expired.");
                    continue;
                }
                if (!connection.ExpiringSent && connection.TokenExpiresUtc - now <= TimeSpan.FromMinutes(5))
                {
                    connection.ExpiringSent = true;
                    Send(connection, "auth.expiring", new Dictionary<string, object?> { ["expiresUtc"] = connection.TokenExpiresUtc }, null, null, false);
                }
                if ((now - connection.LastGateCheckUtc).TotalSeconds >= _GateCheckSeconds)
                {
                    connection.LastGateCheckUtc = now;
                    work.Add(CheckKillSwitchesAsync(connection));
                }
                if ((now - connection.LastRevalidatedUtc).TotalSeconds >= _RevalidateSeconds)
                {
                    connection.LastRevalidatedUtc = now;
                    if (!touch.TryGetValue(connection.PropertyId, out List<string>? ids))
                    {
                        ids = new List<string>();
                        touch[connection.PropertyId] = ids;
                    }
                    ids.Add(connection.ConnectionId);
                    work.Add(RevalidateAsync(connection, false));
                }
            }
            foreach (KeyValuePair<string, List<string>> pair in touch)
            {
                PropertyRuntime? runtime = _Directory.Find(pair.Key);
                if (runtime != null) work.Add(runtime.Services.Realtime.Registry.TouchAsync(pair.Value, CancellationToken.None));
            }
            foreach (KeyValuePair<string, DateTime> seen in _RecentEvents.Where(p => (now - p.Value).TotalSeconds > 60).ToList())
                _RecentEvents.TryRemove(seen);
            try
            {
                await Task.WhenAll(work).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _Logger.Warn("[RealtimeGateway] sweep work failed: " + e.Message);
            }
        }

        private async Task CheckKillSwitchesAsync(RealtimeConnection connection)
        {
            RealtimeModule module = connection.Runtime.Services.Realtime;
            string? realtime = await module.KillSwitchMessageAsync(Path, connection.Principal, connection.Header, CancellationToken.None).ConfigureAwait(false);
            if (realtime != null)
            {
                Error(connection, "FeatureDisabled", realtime, null);
                connection.RequestClose(RealtimeCloseCodes.FeatureDisabled, "Realtime disabled.");
                return;
            }
            string? messaging = await module.KillSwitchMessageAsync(Constants.ApiPrefix + "/conversations", connection.Principal, connection.Header, CancellationToken.None).ConfigureAwait(false);
            if (messaging != null && !connection.MessagingKilled)
            {
                connection.MessagingKilled = true;
                bool had = connection.HasCapability(RealtimeCapabilities.Messaging);
                connection.SetCapabilities(connection.Capabilities.Where(c => c != RealtimeCapabilities.Messaging).ToList());
                if (had || connection.Requested.Contains(RealtimeCapabilities.Messaging, StringComparer.Ordinal))
                    Send(connection, "feature.disabled", new Dictionary<string, object?> { ["feature"] = RealtimeCapabilities.Messaging, ["message"] = messaging }, null, null, false);
            }
            else if (messaging == null && connection.MessagingKilled)
            {
                connection.MessagingKilled = false;
                await RevalidateAsync(connection, true).ConfigureAwait(false);
            }
        }

        #endregion
    }
}
