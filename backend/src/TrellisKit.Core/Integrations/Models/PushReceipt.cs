namespace TrellisKit.Core.Integrations.Models
{
    using System;

    /// <summary>
    /// The Expo push receipt for one ticket.
    /// </summary>
    public class PushReceipt
    {
        /// <summary>
        /// "ok" or "error".
        /// </summary>
        public string Status { get; set; } = String.Empty;

        /// <summary>
        /// Error message, or null.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Error code from details.error (DeviceNotRegistered, MessageTooBig, MessageRateExceeded, ...), or null.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Whether the receipt is ok.
        /// </summary>
        public bool IsOk
        {
            get { return String.Equals(Status, "ok", StringComparison.OrdinalIgnoreCase); }
        }
    }
}
