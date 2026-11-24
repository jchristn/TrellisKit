namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Typed filters of the remote string enumeration.
    /// </summary>
    public class RemoteStringFilter
    {
        /// <summary>
        /// Client type (web, mobile), or null.
        /// </summary>
        public string? Client { get; set; } = null;

        /// <summary>
        /// Whether a published override exists, or null.
        /// </summary>
        public bool? Overridden { get; set; } = null;

        /// <summary>
        /// Whether a draft is pending, or null.
        /// </summary>
        public bool? HasDraftChanges { get; set; } = null;
    }
}
