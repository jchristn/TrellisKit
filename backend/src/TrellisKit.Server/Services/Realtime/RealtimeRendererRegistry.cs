namespace TrellisKit.Server.Services.Realtime
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Event renderers keyed by bus event kind, one registry per property (<see cref="RealtimeModule.Renderers"/>). The
    /// gateway's dispatcher receives a <see cref="RealtimeEvent"/> (references only), keeps the recipients in
    /// <see cref="RealtimeEvent.UserIds"/> that have a local socket of the event's property, and calls the kind's
    /// renderer once per recipient with the property-bound services; the returned object is the frame's data (camelCase
    /// JSON, at most 64 KB; larger events are sent as references). Frames of one conversation
    /// (<see cref="RealtimeEvent.ConversationId"/> with <see cref="RealtimeEvent.Seq"/>) are written in seq order per
    /// connection. Kinds without a renderer are dropped. The realtime module registers notification.created,
    /// unread.counts, ticket.updated, and feature.disabled; the messaging module registers message.*, conversation.*,
    /// receipt.*, and typing, for example
    /// <c>services.Realtime.Renderers.Register(RealtimeEventKinds.MessageCreated, null, RealtimeCapabilities.Messaging, false, RenderMessageAsync)</c>.
    /// session.revoked and access.changed are handled by the gateway itself (they act on connections).
    /// Thread safety: safe to register and look up concurrently.
    /// </summary>
    public sealed class RealtimeRendererRegistry
    {
        #region Private-Members

        private readonly ConcurrentDictionary<string, RealtimeRenderer> _Renderers = new ConcurrentDictionary<string, RealtimeRenderer>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register (or replace) the renderer of an event kind.
        /// </summary>
        /// <param name="kind">Event kind.</param>
        /// <param name="frameType">Frame type, or null for the kind.</param>
        /// <param name="requiredCapability">Capability a connection must hold, or null.</param>
        /// <param name="droppable">Dropped first on outbound overflow (typing, receipts).</param>
        /// <param name="render">Renderer.</param>
        /// <exception cref="ArgumentNullException">The kind or renderer is null.</exception>
        /// <exception cref="ArgumentException">The kind is session.revoked or access.changed.</exception>
        public void Register(string kind, string? frameType, string? requiredCapability, bool droppable, Func<RealtimeRenderContext, CancellationToken, Task<object?>> render)
        {
            RealtimeRenderer renderer = new RealtimeRenderer(kind, frameType, requiredCapability, droppable, render);
            if (kind == RealtimeEventKinds.SessionRevoked || kind == RealtimeEventKinds.AccessChanged)
                throw new ArgumentException("The gateway handles " + kind + " itself.", nameof(kind));
            _Renderers[kind] = renderer;
        }

        /// <summary>
        /// The renderer of a kind, or null.
        /// </summary>
        /// <param name="kind">Event kind.</param>
        /// <returns>Renderer, or null.</returns>
        public RealtimeRenderer? Find(string? kind)
        {
            if (String.IsNullOrEmpty(kind)) return null;
            return _Renderers.TryGetValue(kind, out RealtimeRenderer? renderer) ? renderer : null;
        }

        /// <summary>
        /// Remove a renderer.
        /// </summary>
        /// <param name="kind">Event kind.</param>
        /// <returns>True when one was removed.</returns>
        public bool Unregister(string kind)
        {
            if (String.IsNullOrEmpty(kind)) return false;
            return _Renderers.TryRemove(kind, out RealtimeRenderer? _);
        }

        #endregion
    }
}
