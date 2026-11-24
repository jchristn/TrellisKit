namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Adoption summary of a group.
    /// </summary>
    public class UpdateAdoptionSummaryView
    {
        /// <summary>
        /// Installs on the group's channel and platforms that checked in the last 30 days.
        /// </summary>
        public long ActiveInstalls { get; set; } = 0;

        /// <summary>
        /// Installs running an update of the group.
        /// </summary>
        public long OnThisUpdate { get; set; } = 0;
    }
}
