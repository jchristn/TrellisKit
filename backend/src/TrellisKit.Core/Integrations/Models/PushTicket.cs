namespace TrellisKit.Core.Integrations.Models
{
    using System;

    /// <summary>
    /// The Expo push ticket for one message.
    /// </summary>
    public class PushTicket
    {
        /// <summary>
        /// "ok" or "error".
        /// </summary>
        public string Status { get; set; } = String.Empty;

        /// <summary>
        /// Ticket (receipt) identifier when ok, or null.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Error message, or null.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Error code from details.error (for example DeviceNotRegistered), or null.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Whether the ticket is ok.
        /// </summary>
        public bool IsOk
        {
            get { return String.Equals(Status, "ok", StringComparison.OrdinalIgnoreCase); }
        }
    }
}
