namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A channel with its latest updates and active installs.
    /// </summary>
    public class UpdateChannelView
    {
        /// <summary>
        /// Name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Built-in channel.
        /// </summary>
        public bool BuiltIn { get; set; } = false;

        /// <summary>
        /// Newest published update per platform (ios, android), or null.
        /// </summary>
        public Dictionary<string, UpdateView?> LatestByPlatform { get; set; } = new Dictionary<string, UpdateView?>(StringComparer.Ordinal);

        /// <summary>
        /// Installs on the channel that checked in the last 30 days.
        /// </summary>
        public long ActiveInstalls30d { get; set; } = 0;

        /// <summary>
        /// Whether updates on this channel may be promoted to another channel (never development).
        /// </summary>
        public bool Promotable { get; set; } = false;

        /// <summary>
        /// Labels (always empty).
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags (always empty).
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Created time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }
}
