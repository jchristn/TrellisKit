namespace TrellisKit.Core.Integrations.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One Expo push message (Phase 2 contract 8.8).
    /// </summary>
    public class PushMessage
    {
        /// <summary>
        /// Expo push token.
        /// </summary>
        public string To { get; set; } = String.Empty;

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Body, or null.
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Data delivered to the app (notificationId, type, path). Never null.
        /// </summary>
        public Dictionary<string, string> Data
        {
            get { return _Data; }
            set { _Data = value ?? new Dictionary<string, string>(); }
        }

        /// <summary>
        /// Sound. Default "default".
        /// </summary>
        public string Sound { get; set; } = "default";

        /// <summary>
        /// App icon badge, or null to leave it unchanged.
        /// </summary>
        public int? Badge { get; set; } = null;

        /// <summary>
        /// Android channel. Default "default".
        /// </summary>
        public string ChannelId { get; set; } = "default";

        /// <summary>
        /// Priority. Default "high".
        /// </summary>
        public string Priority { get; set; } = "high";

        private Dictionary<string, string> _Data = new Dictionary<string, string>();
    }
}
