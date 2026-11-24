namespace TrellisKit.Server.Services.Realtime
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Linq;
    using System.Net.WebSockets;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Observability;
    using WatsonWebserver.Core.WebSockets;

    /// <summary>
    /// One socket held by <see cref="RealtimeGateway"/>: its authentication state, capabilities, inbound limits, and the
    /// bounded outbound queue with its writer loop (docs/PHASE5_CONTRACT.md sections 4.6 and 4.12). Frames of one
    /// conversation are kept in seq order inside the queue; on overflow droppable frames (typing, receipts) go first,
    /// then the queue is cleared and one resync.required Overflow is queued. Closes are queued behind the frames before
    /// them (so an error frame precedes its close), except the slow consumer and draining closes.
    /// Thread safety: state fields are written by the receive loop and the gateway sweep; the queue is locked.
    /// </summary>
    internal sealed class RealtimeConnection : IRealtimeConnectionView
    {
        #region Public-Members

        /// <summary>Connection id (rtc_).</summary>
        public string ConnectionId { get; }

        /// <summary>Property id.</summary>
        public string PropertyId { get; }

        /// <summary>Property runtime the socket resolved to.</summary>
        public PropertyRuntime Runtime { get; }

        /// <summary>Watson session.</summary>
        public WebSocketSession Session { get; }

        /// <summary>Upgrade request headers.</summary>
        public NameValueCollection Headers { get; }

        /// <summary>Client address.</summary>
        public string RemoteIp { get; }

        /// <summary>UTC time the socket opened.</summary>
        public DateTime OpenedUtc { get; } = DateTime.UtcNow;

        /// <summary>Whether hello succeeded.</summary>
        public bool Authenticated { get; set; } = false;

        /// <summary>Whether the pending hello slot of the client address was released.</summary>
        public bool PendingReleased { get; set; } = false;

        /// <summary>Member.</summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>Session.</summary>
        public string SessionId { get; set; } = String.Empty;

        /// <summary>Current access token.</summary>
        public string AccessToken { get; set; } = String.Empty;

        /// <summary>Session principal at the last validation.</summary>
        public SessionPrincipal? Principal { get; set; } = null;

        /// <summary>Access token expiry.</summary>
        public DateTime TokenExpiresUtc { get; set; } = DateTime.MaxValue;

        /// <summary>Whether auth.expiring was sent for the current token.</summary>
        public bool ExpiringSent { get; set; } = false;

        /// <summary>Client kind (Web, Mobile, Other).</summary>
        public string Client { get; set; } = "Other";

        /// <summary>App state.</summary>
        public string AppState { get; set; } = "Foreground";

        /// <summary>Open conversation, or null.</summary>
        public string? ActiveConversationId { get; set; } = null;

        /// <summary>UTC time of the welcome.</summary>
        public DateTime ConnectedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC time of the last inbound frame.</summary>
        public DateTime LastInboundUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC time of the last registry touch and principal re-validation.</summary>
        public DateTime LastRevalidatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC time of the last kill switch evaluation.</summary>
        public DateTime LastGateCheckUtc { get; set; } = DateTime.MinValue;

        /// <summary>Capabilities the client asked for.</summary>
        public List<string> Requested { get; set; } = new List<string>();

        /// <summary>Access tier at the last evaluation.</summary>
        public AccessTierEnum AccessTier { get; set; } = AccessTierEnum.SignedIn;

        /// <summary>Whether the messaging kill switch was on at the last evaluation.</summary>
        public bool MessagingKilled { get; set; } = false;

        /// <summary>Inbound token bucket.</summary>
        public RealtimeTokenBucket Bucket { get; }

        /// <summary>Rate limit violations in the last minute.</summary>
        public Queue<DateTime> Violations { get; } = new Queue<DateTime>();

        /// <summary>Last typing Start per conversation.</summary>
        public Dictionary<string, DateTime> TypingStarts { get; } = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        /// <summary>Whether a close was requested.</summary>
        public bool Closing
        {
            get { return _CloseCode != null; }
        }

        /// <summary>Requested close code, or null.</summary>
        public int? RequestedCloseCode
        {
            get { return _CloseCode; }
        }

        /// <summary>UTC time the close was requested.</summary>
        public DateTime CloseRequestedUtc { get; private set; } = DateTime.MaxValue;

        /// <summary>UTC time the current send started, or null when the writer is idle.</summary>
        public DateTime? SendStartedUtc
        {
            get
            {
                long ticks = Interlocked.Read(ref _SendStartedTicks);
                return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
            }
        }

        /// <summary>Frames queued now.</summary>
        public int QueuedFrames
        {
            get { lock (_QueueLock) return _Queue.Count; }
        }

        /// <inheritdoc />
        public IReadOnlyList<string> Capabilities
        {
            get { lock (_CapabilityLock) return _Capabilities.ToList(); }
        }

        #endregion

        #region Private-Members

        private readonly object _QueueLock = new object();
        private readonly object _CapabilityLock = new object();
        private readonly LinkedList<RealtimeOutboundFrame> _Queue = new LinkedList<RealtimeOutboundFrame>();
        private readonly SemaphoreSlim _Signal = new SemaphoreSlim(0);
        private readonly CancellationTokenSource _Lifetime = new CancellationTokenSource();
        private List<string> _Capabilities = new List<string>();
        private long _QueuedBytes = 0;
        private long _SendStartedTicks = 0;
        private long _FrameCounter = 0;
        private int? _CloseCode = null;
        private Task? _Writer = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a connection.
        /// </summary>
        /// <param name="connectionId">Id.</param>
        /// <param name="runtime">Property runtime.</param>
        /// <param name="session">Watson session.</param>
        /// <param name="headers">Upgrade headers.</param>
        /// <param name="remoteIp">Client address.</param>
        /// <param name="burst">Inbound burst.</param>
        public RealtimeConnection(string connectionId, PropertyRuntime runtime, WebSocketSession session, NameValueCollection headers, string remoteIp, int burst)
        {
            ConnectionId = connectionId;
            Runtime = runtime;
            PropertyId = runtime.Id;
            Session = session;
            Headers = headers;
            RemoteIp = remoteIp;
            Bucket = new RealtimeTokenBucket(burst);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public bool HasCapability(string capability)
        {
            lock (_CapabilityLock) return _Capabilities.Contains(capability, StringComparer.Ordinal);
        }

        /// <summary>
        /// Replace the granted capabilities.
        /// </summary>
        /// <param name="capabilities">Capabilities.</param>
        /// <returns>True when the set changed.</returns>
        public bool SetCapabilities(List<string> capabilities)
        {
            lock (_CapabilityLock)
            {
                bool changed = capabilities.Count != _Capabilities.Count || capabilities.Except(_Capabilities, StringComparer.Ordinal).Any();
                _Capabilities = capabilities.ToList();
                return changed;
            }
        }

        /// <summary>
        /// A header of the upgrade request, or null.
        /// </summary>
        /// <param name="name">Header name.</param>
        /// <returns>Value, or null.</returns>
        public string? Header(string name)
        {
            return Headers[name];
        }

        /// <summary>
        /// The next server frame id (unique per connection, increasing).
        /// </summary>
        /// <returns>Id, for example s-12.</returns>
        public string NextFrameId()
        {
            return "s-" + Interlocked.Increment(ref _FrameCounter).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Start the writer loop.
        /// </summary>
        public void StartWriter()
        {
            _Writer ??= Task.Run(WriteLoopAsync);
        }

        /// <summary>
        /// Queue a frame, applying the overflow rules.
        /// </summary>
        /// <param name="frame">Frame.</param>
        /// <param name="maxFrames">realtime.maxQueuedFrames.</param>
        /// <param name="maxBytes">realtime.maxQueuedBytes.</param>
        /// <param name="resync">Builds the resync.required Overflow frame.</param>
        /// <returns>Frames dropped (0 when none); negative when the frame was refused because the connection is closing.</returns>
        public int Enqueue(RealtimeOutboundFrame frame, int maxFrames, int maxBytes, Func<RealtimeOutboundFrame> resync)
        {
            int dropped = 0;
            bool overflowed = false;
            lock (_QueueLock)
            {
                if (_CloseCode != null) return -1;
                Insert(frame);
                if (_Queue.Count > maxFrames || _QueuedBytes > maxBytes)
                {
                    LinkedListNode<RealtimeOutboundFrame>? node = _Queue.First;
                    while (node != null)
                    {
                        LinkedListNode<RealtimeOutboundFrame>? next = node.Next;
                        if (node.Value.Droppable)
                        {
                            _QueuedBytes -= node.Value.Bytes;
                            _Queue.Remove(node);
                            dropped++;
                        }
                        node = next;
                    }
                    if (_Queue.Count > maxFrames || _QueuedBytes > maxBytes)
                    {
                        dropped += _Queue.Count;
                        _Queue.Clear();
                        _QueuedBytes = 0;
                        RealtimeOutboundFrame marker = resync();
                        _Queue.AddLast(marker);
                        _QueuedBytes += marker.Bytes;
                        overflowed = true;
                    }
                }
            }
            if (overflowed) RealtimeTelemetry.Count(RealtimeTelemetry.Resyncs, "reason", "Overflow");
            if (dropped > 0) TrellisKitTelemetry.Add(RealtimeTelemetry.Dropped, dropped, "reason", "overflow");
            _Signal.Release();
            return dropped;
        }

        /// <summary>
        /// Queue a close behind the frames already queued (idempotent; the first code wins).
        /// </summary>
        /// <param name="code">Close code.</param>
        /// <param name="reason">Reason.</param>
        /// <returns>True when this call requested the close.</returns>
        public bool RequestClose(int code, string reason)
        {
            lock (_QueueLock)
            {
                if (_CloseCode != null) return false;
                _CloseCode = code;
                CloseRequestedUtc = DateTime.UtcNow;
                _Queue.AddLast(RealtimeOutboundFrame.Close(code, reason));
            }
            _Signal.Release();
            return true;
        }

        /// <summary>
        /// Close now, bypassing the queue (slow consumer, forced closes). Never throws.
        /// </summary>
        /// <param name="code">Close code.</param>
        /// <param name="reason">Reason.</param>
        /// <returns>Task.</returns>
        public async Task CloseNowAsync(int code, string reason)
        {
            lock (_QueueLock)
            {
                if (_CloseCode == null)
                {
                    _CloseCode = code;
                    CloseRequestedUtc = DateTime.UtcNow;
                }
                _Queue.Clear();
                _QueuedBytes = 0;
            }
            await CloseSessionAsync(_CloseCode ?? code, reason).ConfigureAwait(false);
            try
            {
                await Session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Already disposed.
            }
        }

        /// <summary>
        /// A copy of the queued frames in write order (diagnostics and tests).
        /// </summary>
        /// <returns>Frames.</returns>
        public List<RealtimeOutboundFrame> SnapshotQueue()
        {
            lock (_QueueLock) return _Queue.ToList();
        }

        /// <summary>
        /// Stop the writer (after the socket ended).
        /// </summary>
        public void Stop()
        {
            try
            {
                _Lifetime.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already stopped.
            }
        }

        #endregion

        #region Private-Methods

        private void Insert(RealtimeOutboundFrame frame)
        {
            _QueuedBytes += frame.Bytes;
            if (frame.ConversationId == null || frame.Seq == null)
            {
                _Queue.AddLast(frame);
                return;
            }
            // Same-conversation frames stay in seq order (section 4.6); other frames keep arrival order.
            LinkedListNode<RealtimeOutboundFrame>? node = _Queue.Last;
            LinkedListNode<RealtimeOutboundFrame>? firstGreater = null;
            while (node != null)
            {
                if (node.Value.CloseCode == null && String.Equals(node.Value.ConversationId, frame.ConversationId, StringComparison.Ordinal) && node.Value.Seq != null)
                {
                    if (node.Value.Seq.Value <= frame.Seq.Value) break;
                    firstGreater = node;
                }
                node = node.Previous;
            }
            if (firstGreater == null) _Queue.AddLast(frame);
            else _Queue.AddBefore(firstGreater, frame);
        }

        private RealtimeOutboundFrame? Dequeue()
        {
            lock (_QueueLock)
            {
                LinkedListNode<RealtimeOutboundFrame>? first = _Queue.First;
                if (first == null) return null;
                _Queue.RemoveFirst();
                _QueuedBytes -= first.Value.Bytes;
                return first.Value;
            }
        }

        private async Task WriteLoopAsync()
        {
            CancellationToken token = _Lifetime.Token;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await _Signal.WaitAsync(token).ConfigureAwait(false);
                    RealtimeOutboundFrame? frame = Dequeue();
                    if (frame == null) continue;
                    if (frame.CloseCode != null)
                    {
                        await CloseSessionAsync(frame.CloseCode.Value, frame.CloseReason).ConfigureAwait(false);
                        return;
                    }
                    if (!Session.IsConnected) return;
                    Interlocked.Exchange(ref _SendStartedTicks, DateTime.UtcNow.Ticks);
                    try
                    {
                        await Session.SendTextAsync(frame.Json, token).ConfigureAwait(false);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _SendStartedTicks, 0);
                    }
                    RealtimeTelemetry.Count(RealtimeTelemetry.Frames, "direction", "out", "type", frame.Type);
                }
            }
            catch (Exception)
            {
                // The socket closed or the writer was stopped; the receive loop cleans up.
            }
        }

        private async Task CloseSessionAsync(int code, string reason)
        {
            try
            {
                if (Session.IsConnected) await Session.CloseAsync((WebSocketCloseStatus)code, reason, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // A broken socket cannot be closed cleanly; Watson disposes it.
            }
        }

        #endregion
    }
}
