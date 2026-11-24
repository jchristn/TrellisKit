namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Install outcome counts of one update group, used by the automatic halt.
    /// </summary>
    public class UpdateHealthCounts
    {
        /// <summary>
        /// Installs that reported or were seen failing to launch an update of the group.
        /// </summary>
        public long Failures { get; set; } = 0;

        /// <summary>
        /// Installs running an update of the group.
        /// </summary>
        public long Successes { get; set; } = 0;
    }
}
