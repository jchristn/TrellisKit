namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A registered push token as returned by the API (Phase 2 contract 8.7). The token value is never returned.
    /// </summary>
    public class PushTokenView
    {
        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Platform.
        /// </summary>
        public PushPlatformEnum Platform { get; set; } = PushPlatformEnum.Ios;

        /// <summary>
        /// Device name, or null.
        /// </summary>
        public string? DeviceName { get; set; } = null;

        /// <summary>
        /// App version, or null.
        /// </summary>
        public string? AppVersion { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last registration (UTC).
        /// </summary>
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Labels (enumeration and metadata specification). Never null.
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags (enumeration and metadata specification). Never null.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(); }
        }

        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();
    }
}
