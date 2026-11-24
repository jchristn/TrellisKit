namespace Test.Shared.Suites
{
    using System;
    using System.Text.Json;

    /// <summary>
    /// One parsed server-sent event.
    /// </summary>
    internal sealed class SseEvent
    {
        /// <summary>
        /// Event name.
        /// </summary>
        internal string Name { get; set; } = String.Empty;

        /// <summary>
        /// Event data.
        /// </summary>
        internal JsonElement Data { get; set; }
    }
}
