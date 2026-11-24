namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A live update channel (table updatechannels).
    /// </summary>
    public class UpdateChannel
    {
        /// <summary>
        /// Channel name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Whether it is production, preview, or development.
        /// </summary>
        public bool BuiltIn { get; set; } = false;

        /// <summary>
        /// Created time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }
}
