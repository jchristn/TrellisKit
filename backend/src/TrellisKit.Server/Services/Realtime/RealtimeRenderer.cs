namespace TrellisKit.Server.Services.Realtime
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// One registered event renderer (see <see cref="RealtimeRendererRegistry"/>): turns a bus event into the data of
    /// one recipient's frame.
    /// Thread safety: immutable.
    /// </summary>
    public sealed class RealtimeRenderer
    {
        #region Public-Members

        /// <summary>
        /// Bus event kind, for example notification.created.
        /// </summary>
        public string Kind { get; }

        /// <summary>
        /// Frame type sent to clients (usually the kind).
        /// </summary>
        public string FrameType { get; }

        /// <summary>
        /// Capability a connection must hold to receive the frame, or null for every authenticated connection.
        /// </summary>
        public string? RequiredCapability { get; }

        /// <summary>
        /// Whether queued frames of this type are dropped first on outbound overflow (typing and receipts, section 4.12).
        /// </summary>
        public bool Droppable { get; }

        /// <summary>
        /// Renders the frame data for one recipient; null skips that recipient (for example no longer a participant).
        /// </summary>
        public Func<RealtimeRenderContext, CancellationToken, Task<object?>> Render { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a renderer.
        /// </summary>
        /// <param name="kind">Event kind.</param>
        /// <param name="frameType">Frame type, or null for the kind.</param>
        /// <param name="requiredCapability">Required capability, or null.</param>
        /// <param name="droppable">Dropped first on overflow.</param>
        /// <param name="render">Renderer.</param>
        /// <exception cref="ArgumentNullException">The kind or renderer is null.</exception>
        public RealtimeRenderer(string kind, string? frameType, string? requiredCapability, bool droppable, Func<RealtimeRenderContext, CancellationToken, Task<object?>> render)
        {
            if (String.IsNullOrWhiteSpace(kind)) throw new ArgumentNullException(nameof(kind));
            Kind = kind;
            FrameType = String.IsNullOrWhiteSpace(frameType) ? kind : frameType;
            RequiredCapability = requiredCapability;
            Droppable = droppable;
            Render = render ?? throw new ArgumentNullException(nameof(render));
        }

        #endregion
    }
}
