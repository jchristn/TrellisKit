namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Live state of one kill switch (table killswitchstate).
    /// </summary>
    public class KillSwitchState
    {
        /// <summary>
        /// Engine feature key.
        /// </summary>
        public string FeatureKey { get; set; } = String.Empty;

        /// <summary>
        /// Kill switch flag object id.
        /// </summary>
        public string FlagObjectId { get; set; } = String.Empty;

        /// <summary>
        /// Kill switch revision when it last changed.
        /// </summary>
        public long Revision { get; set; } = 0;

        /// <summary>
        /// Last editor, or null.
        /// </summary>
        public string? LastChangedByUserId { get; set; } = null;

        /// <summary>
        /// Last editor display name, or null.
        /// </summary>
        public string? LastChangedByName { get; set; } = null;

        /// <summary>
        /// Reason of the last change, or null.
        /// </summary>
        public string? LastChangedReason { get; set; } = null;

        /// <summary>
        /// Last change, or null when never changed.
        /// </summary>
        public DateTime? LastChangedUtc { get; set; } = null;
    }
}
