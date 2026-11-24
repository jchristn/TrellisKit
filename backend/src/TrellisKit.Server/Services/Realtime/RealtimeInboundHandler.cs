namespace TrellisKit.Server.Services.Realtime
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// One registered client frame handler (see <see cref="RealtimeInboundHandlerRegistry"/>).
    /// Thread safety: immutable.
    /// </summary>
    public sealed class RealtimeInboundHandler
    {
        #region Public-Members

        /// <summary>
        /// Client frame type, for example typing.
        /// </summary>
        public string Type { get; }

        /// <summary>
        /// Capability the connection must hold, or null. Without it the gateway replies error CapabilityRequired.
        /// </summary>
        public string? RequiredCapability { get; }

        /// <summary>
        /// The handler.
        /// </summary>
        public Func<RealtimeInboundContext, CancellationToken, Task> Handle { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a handler.
        /// </summary>
        /// <param name="type">Frame type.</param>
        /// <param name="requiredCapability">Required capability, or null.</param>
        /// <param name="handle">Handler.</param>
        /// <exception cref="ArgumentNullException">The type or handler is null.</exception>
        public RealtimeInboundHandler(string type, string? requiredCapability, Func<RealtimeInboundContext, CancellationToken, Task> handle)
        {
            if (String.IsNullOrWhiteSpace(type)) throw new ArgumentNullException(nameof(type));
            Type = type;
            RequiredCapability = requiredCapability;
            Handle = handle ?? throw new ArgumentNullException(nameof(handle));
        }

        #endregion
    }
}
