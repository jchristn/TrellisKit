namespace TrellisKit.Server.Services.Messaging
{
    using System;
    using System.Diagnostics.Metrics;
    using TrellisKit.Core.Observability;

    /// <summary>
    /// Direct messaging instruments (docs/PHASE5_CONTRACT.md section 15). Labels are low cardinality; never ids, names, or
    /// text. The property label is added by <see cref="TrellisKitTelemetry"/>.
    /// Thread safety: instruments are thread safe.
    /// </summary>
    public static class MessagingTelemetry
    {
        #region Public-Members

        /// <summary>
        /// trelliskit.messaging.messages: labels action (send, edit, delete, react), outcome, has_media.
        /// </summary>
        public static readonly Counter<long> Messages = TrellisKitTelemetry.Meter.CreateCounter<long>(
            "trelliskit.messaging.messages", "{message}", "Direct message actions, labeled by action, outcome, and has_media.");

        /// <summary>
        /// trelliskit.messaging.conversations: labels action (create, accept, decline, delete), outcome.
        /// </summary>
        public static readonly Counter<long> Conversations = TrellisKitTelemetry.Meter.CreateCounter<long>(
            "trelliskit.messaging.conversations", "{conversation}", "Conversation actions, labeled by action and outcome.");

        /// <summary>
        /// trelliskit.messaging.denied: label reason (policy, block_or_policy, restricted, unavailable, request_limit).
        /// </summary>
        public static readonly Counter<long> Denied = TrellisKitTelemetry.Meter.CreateCounter<long>(
            "trelliskit.messaging.denied", "{denial}", "Refused direct message sends, labeled by reason.");

        /// <summary>
        /// trelliskit.messaging.send.duration (seconds).
        /// </summary>
        public static readonly Histogram<double> SendDuration = TrellisKitTelemetry.Meter.CreateHistogram<double>(
            "trelliskit.messaging.send.duration", "s", "Time to send a direct message (validation through commit).");

        /// <summary>
        /// trelliskit.messaging.delivery.latency (seconds): label path (socket, push), commit to frame written or push queued.
        /// </summary>
        public static readonly Histogram<double> DeliveryLatency = TrellisKitTelemetry.Meter.CreateHistogram<double>(
            "trelliskit.messaging.delivery.latency", "s", "Commit to delivery of a direct message, labeled by path.");

        #endregion

        #region Public-Methods

        /// <summary>
        /// Count one message action. Never throws.
        /// </summary>
        /// <param name="action">send, edit, delete, or react.</param>
        /// <param name="outcome">Outcome, for example ok or idempotent.</param>
        /// <param name="hasMedia">Whether the message has media.</param>
        public static void Message(string action, string outcome, bool hasMedia)
        {
            TrellisKitTelemetry.Add(Messages, 1, "action", action ?? String.Empty, "outcome", outcome ?? String.Empty, "has_media", hasMedia ? "true" : "false");
        }

        /// <summary>
        /// Count one conversation action. Never throws.
        /// </summary>
        /// <param name="action">create, accept, decline, or delete.</param>
        /// <param name="outcome">Outcome.</param>
        public static void Conversation(string action, string outcome)
        {
            TrellisKitTelemetry.Add(Conversations, 1, "action", action ?? String.Empty, "outcome", outcome ?? String.Empty);
        }

        /// <summary>
        /// Count one refusal. Never throws.
        /// </summary>
        /// <param name="reason">Reason label.</param>
        public static void Deny(string reason)
        {
            TrellisKitTelemetry.Add(Denied, 1, "reason", reason ?? String.Empty);
        }

        #endregion
    }
}
