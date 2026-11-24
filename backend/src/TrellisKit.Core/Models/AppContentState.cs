namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Per-property content and kill switch revisions (table appcontentstate).
    /// </summary>
    public class AppContentState
    {
        /// <summary>
        /// Current content version (0 before the first publish).
        /// </summary>
        public long ContentVersion { get; set; } = 0;

        /// <summary>
        /// Current kill switch revision.
        /// </summary>
        public long KillSwitchRevision { get; set; } = 0;

        /// <summary>
        /// Last change.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}
