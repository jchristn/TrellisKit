namespace TrellisKit.Core.Settings
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Realtime WebSocket transport settings (docs/PHASE5_CONTRACT.md sections 4 and 14). Deployment-wide.
    /// </summary>
    public class RealtimeSettings
    {
        #region Public-Members

        /// <summary>
        /// Serve GET /api/v1/ws. When false, clients fall back to polling. Default true.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// How events cross server tasks: Postgres (LISTEN and NOTIFY) or InProcess (single task only). Default Postgres.
        /// </summary>
        public RealtimeBusEnum Bus { get; set; } = RealtimeBusEnum.Postgres;

        /// <summary>
        /// Clients send ping this often; a connection silent for two intervals plus 5 seconds is dropped. Default 25. Minimum 10, maximum 55.
        /// </summary>
        public int HeartbeatSeconds
        {
            get { return _HeartbeatSeconds; }
            set { _HeartbeatSeconds = Math.Clamp(value, 10, 55); }
        }

        /// <summary>
        /// A connection that sends no valid hello in time is closed 4408. Default 10. Minimum 3, maximum 60.
        /// </summary>
        public int AuthTimeoutSeconds
        {
            get { return _AuthTimeoutSeconds; }
            set { _AuthTimeoutSeconds = Math.Clamp(value, 3, 60); }
        }

        /// <summary>
        /// Upper bound for closing the sockets of a revoked session. Default 5. Minimum 1, maximum 60.
        /// </summary>
        public int RevocationSeconds
        {
            get { return _RevocationSeconds; }
            set { _RevocationSeconds = Math.Clamp(value, 1, 60); }
        }

        /// <summary>
        /// Largest client frame; larger frames close 1009. Default 16384. Minimum 1024, maximum 65536.
        /// </summary>
        public int MaxInboundFrameBytes
        {
            get { return _MaxInboundFrameBytes; }
            set { _MaxInboundFrameBytes = Math.Clamp(value, 1024, 65536); }
        }

        /// <summary>
        /// Client frames per second per connection (burst twice this). Default 20. Minimum 1, maximum 200.
        /// </summary>
        public int MaxInboundFramesPerSecond
        {
            get { return _MaxInboundFramesPerSecond; }
            set { _MaxInboundFramesPerSecond = Math.Clamp(value, 1, 200); }
        }

        /// <summary>
        /// Rate limit violations within a minute before the connection closes 4429. Default 5. Minimum 1, maximum 100.
        /// </summary>
        public int MaxViolations
        {
            get { return _MaxViolations; }
            set { _MaxViolations = Math.Clamp(value, 1, 100); }
        }

        /// <summary>
        /// Frames queued per connection before typing and receipts are dropped and a resync is requested. Default 256. Minimum 16, maximum 10000.
        /// </summary>
        public int MaxQueuedFrames
        {
            get { return _MaxQueuedFrames; }
            set { _MaxQueuedFrames = Math.Clamp(value, 16, 10000); }
        }

        /// <summary>
        /// Bytes queued per connection before overflow handling. Default 1048576. Minimum 65536, maximum 67108864.
        /// </summary>
        public int MaxQueuedBytes
        {
            get { return _MaxQueuedBytes; }
            set { _MaxQueuedBytes = Math.Clamp(value, 65536, 67108864); }
        }

        /// <summary>
        /// A connection whose writes stay blocked this long closes 1013. Default 30. Minimum 5, maximum 300.
        /// </summary>
        public int SlowConsumerSeconds
        {
            get { return _SlowConsumerSeconds; }
            set { _SlowConsumerSeconds = Math.Clamp(value, 5, 300); }
        }

        /// <summary>
        /// Beyond this the oldest connection closes 4409. Default 10. Minimum 1, maximum 100.
        /// </summary>
        public int MaxConnectionsPerUser
        {
            get { return _MaxConnectionsPerUser; }
            set { _MaxConnectionsPerUser = Math.Clamp(value, 1, 100); }
        }

        /// <summary>
        /// Upgrades beyond this return 503 with Retry-After. Default 10000. Minimum 1, maximum 100000.
        /// </summary>
        public int MaxConnectionsPerTask
        {
            get { return _MaxConnectionsPerTask; }
            set { _MaxConnectionsPerTask = Math.Clamp(value, 1, 100000); }
        }

        /// <summary>
        /// Authenticated connections a member may open each minute. Default 30. Minimum 1, maximum 1000.
        /// </summary>
        public int ConnectsPerMinute
        {
            get { return _ConnectsPerMinute; }
            set { _ConnectsPerMinute = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Upgrades one client address may make each minute before hello. Default 60. Minimum 1, maximum 10000.
        /// </summary>
        public int AnonymousUpgradesPerMinute
        {
            get { return _AnonymousUpgradesPerMinute; }
            set { _AnonymousUpgradesPerMinute = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// Open connections without a hello one client address may hold. Default 50. Minimum 1, maximum 10000.
        /// </summary>
        public int MaxPendingHellos
        {
            get { return _MaxPendingHellos; }
            set { _MaxPendingHellos = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// At most one typing Start per conversation per this interval. Default 3000. Minimum 500, maximum 10000.
        /// </summary>
        public int TypingMinIntervalMs
        {
            get { return _TypingMinIntervalMs; }
            set { _TypingMinIntervalMs = Math.Clamp(value, 500, 10000); }
        }

        /// <summary>
        /// On shutdown, sockets are closed 1012 over this period. Default 20. Minimum 0, maximum 120.
        /// </summary>
        public int DrainSeconds
        {
            get { return _DrainSeconds; }
            set { _DrainSeconds = Math.Clamp(value, 0, 120); }
        }

        #endregion

        #region Private-Members

        private int _HeartbeatSeconds = 25;
        private int _AuthTimeoutSeconds = 10;
        private int _RevocationSeconds = 5;
        private int _MaxInboundFrameBytes = 16384;
        private int _MaxInboundFramesPerSecond = 20;
        private int _MaxViolations = 5;
        private int _MaxQueuedFrames = 256;
        private int _MaxQueuedBytes = 1048576;
        private int _SlowConsumerSeconds = 30;
        private int _MaxConnectionsPerUser = 10;
        private int _MaxConnectionsPerTask = 10000;
        private int _ConnectsPerMinute = 30;
        private int _AnonymousUpgradesPerMinute = 60;
        private int _MaxPendingHellos = 50;
        private int _TypingMinIntervalMs = 3000;
        private int _DrainSeconds = 20;

        #endregion
    }
}
