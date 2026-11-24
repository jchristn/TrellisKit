namespace TrellisKit.Server.Services.Realtime
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A read-only view of one authenticated realtime connection, handed to inbound handlers and renderers.
    /// Thread safety: values may change between reads (state frames, access changes); read each once.
    /// </summary>
    public interface IRealtimeConnectionView
    {
        /// <summary>
        /// Connection id (rtc_).
        /// </summary>
        string ConnectionId { get; }

        /// <summary>
        /// Property id.
        /// </summary>
        string PropertyId { get; }

        /// <summary>
        /// Member.
        /// </summary>
        string UserId { get; }

        /// <summary>
        /// Session of the presented access token.
        /// </summary>
        string SessionId { get; }

        /// <summary>
        /// Client kind from hello (Web, Mobile, Other).
        /// </summary>
        string Client { get; }

        /// <summary>
        /// App state: Foreground or Background.
        /// </summary>
        string AppState { get; }

        /// <summary>
        /// Conversation open on screen, or null.
        /// </summary>
        string? ActiveConversationId { get; }

        /// <summary>
        /// UTC time of the welcome.
        /// </summary>
        DateTime ConnectedUtc { get; }

        /// <summary>
        /// Granted capabilities (snapshot).
        /// </summary>
        IReadOnlyList<string> Capabilities { get; }

        /// <summary>
        /// Whether the connection holds a capability.
        /// </summary>
        /// <param name="capability">Capability.</param>
        /// <returns>True when held.</returns>
        bool HasCapability(string capability);
    }
}
