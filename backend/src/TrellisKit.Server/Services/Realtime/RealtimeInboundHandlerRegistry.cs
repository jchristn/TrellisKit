namespace TrellisKit.Server.Services.Realtime
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Client frame handlers keyed by frame type (docs/PHASE5_CONTRACT.md section 4.4), one registry per property
    /// (<see cref="RealtimeModule.Inbound"/>). The gateway itself handles hello, auth.refresh, ping, and state; the
    /// messaging module registers typing, ack, and read (and any future client type) here, for example
    /// <c>services.Realtime.Inbound.Register("typing", RealtimeCapabilities.Messaging, HandleTypingAsync)</c>.
    /// A handler runs after the gateway authenticated the connection, applied the inbound rate limit and size cap,
    /// checked the required capability, and, for typing with state Start, the per-conversation throttle
    /// (realtime.typingMinIntervalMs; extra Starts are dropped silently before the handler). Handlers of one connection
    /// run one at a time in arrival order; a handler that throws gets error InternalError and the connection stays open.
    /// Types listed in <see cref="ReservedTypes"/> without a handler are ignored silently (messaging not built or off);
    /// any other unknown type gets error UnknownFrame.
    /// Thread safety: safe to register and look up concurrently.
    /// </summary>
    public sealed class RealtimeInboundHandlerRegistry
    {
        #region Public-Members

        /// <summary>
        /// Client types the gateway handles itself; they cannot be registered.
        /// </summary>
        public static readonly IReadOnlyList<string> GatewayTypes = new List<string> { "hello", "auth.refresh", "ping", "state" };

        /// <summary>
        /// Contract client types that are ignored silently while no handler is registered.
        /// </summary>
        public static readonly IReadOnlyList<string> ReservedTypes = new List<string> { "typing", "ack", "read" };

        #endregion

        #region Private-Members

        private readonly ConcurrentDictionary<string, RealtimeInboundHandler> _Handlers = new ConcurrentDictionary<string, RealtimeInboundHandler>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register (or replace) the handler of a client frame type.
        /// </summary>
        /// <param name="type">Frame type.</param>
        /// <param name="requiredCapability">Capability the connection must hold (for example messaging), or null.</param>
        /// <param name="handle">Handler.</param>
        /// <exception cref="ArgumentNullException">The type or handler is null.</exception>
        /// <exception cref="ArgumentException">The type is one the gateway handles itself.</exception>
        public void Register(string type, string? requiredCapability, Func<RealtimeInboundContext, CancellationToken, Task> handle)
        {
            RealtimeInboundHandler handler = new RealtimeInboundHandler(type, requiredCapability, handle);
            foreach (string reserved in GatewayTypes)
                if (String.Equals(reserved, type, StringComparison.Ordinal)) throw new ArgumentException("The gateway handles " + type + " itself.", nameof(type));
            _Handlers[type] = handler;
        }

        /// <summary>
        /// Remove a handler.
        /// </summary>
        /// <param name="type">Frame type.</param>
        /// <returns>True when one was removed.</returns>
        public bool Unregister(string type)
        {
            if (String.IsNullOrEmpty(type)) return false;
            return _Handlers.TryRemove(type, out RealtimeInboundHandler? _);
        }

        /// <summary>
        /// The handler of a type, or null.
        /// </summary>
        /// <param name="type">Frame type.</param>
        /// <returns>Handler, or null.</returns>
        public RealtimeInboundHandler? Find(string? type)
        {
            if (String.IsNullOrEmpty(type)) return null;
            return _Handlers.TryGetValue(type, out RealtimeInboundHandler? handler) ? handler : null;
        }

        #endregion
    }
}
