namespace TrellisKit.Server.Services.Realtime
{
    using System;

    /// <summary>
    /// What a renderer receives for one recipient of one bus event: the property-bound services of the event's
    /// property (already checked to be the property of the recipient's socket), the event, the recipient, and the
    /// per-property render cache (2 seconds by key) so the recipients of one event and concurrent events share reads.
    /// Thread safety: one instance per recipient; not shared.
    /// </summary>
    public sealed class RealtimeRenderContext
    {
        #region Public-Members

        /// <summary>
        /// Property-bound services.
        /// </summary>
        public ApplicationServices Services { get; }

        /// <summary>
        /// The bus event (references only).
        /// </summary>
        public RealtimeEvent Event { get; }

        /// <summary>
        /// Recipient member.
        /// </summary>
        public string RecipientUserId { get; }

        /// <summary>
        /// Shared render cache of the property.
        /// </summary>
        public RealtimeRenderCache Cache { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the context (the gateway does this).
        /// </summary>
        /// <param name="services">Services.</param>
        /// <param name="realtimeEvent">Event.</param>
        /// <param name="recipientUserId">Recipient.</param>
        /// <param name="cache">Cache.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public RealtimeRenderContext(ApplicationServices services, RealtimeEvent realtimeEvent, string recipientUserId, RealtimeRenderCache cache)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
            Event = realtimeEvent ?? throw new ArgumentNullException(nameof(realtimeEvent));
            RecipientUserId = recipientUserId ?? throw new ArgumentNullException(nameof(recipientUserId));
            Cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        #endregion
    }
}
