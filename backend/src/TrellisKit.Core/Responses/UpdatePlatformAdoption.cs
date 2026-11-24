namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Adoption of a group on one platform.
    /// </summary>
    public class UpdatePlatformAdoption
    {
        /// <summary>
        /// Active installs on the platform and channel.
        /// </summary>
        public long ActiveInstalls { get; set; } = 0;

        /// <summary>
        /// Installs running this update.
        /// </summary>
        public long OnThisUpdate { get; set; } = 0;

        /// <summary>
        /// Share running it.
        /// </summary>
        public decimal PercentOnThisUpdate { get; set; } = 0m;

        /// <summary>
        /// Downloaded events.
        /// </summary>
        public long Downloaded { get; set; } = 0;

        /// <summary>
        /// Applied events.
        /// </summary>
        public long Applied { get; set; } = 0;

        /// <summary>
        /// DownloadFailed and CheckFailed events.
        /// </summary>
        public long Failed { get; set; } = 0;

        /// <summary>
        /// Installs that failed to launch it.
        /// </summary>
        public long LaunchFailed { get; set; } = 0;
    }
}
