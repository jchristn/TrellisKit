namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// A lease that lets one server instance run a singleton background job.
    /// </summary>
    public class JobLease
    {
        #region Public-Members

        /// <summary>
        /// Job name.
        /// </summary>
        public string JobName { get; set; } = String.Empty;

        /// <summary>
        /// Instance holding the lease.
        /// </summary>
        public string Holder { get; set; } = String.Empty;

        /// <summary>
        /// When the lease lapses (UTC).
        /// </summary>
        public DateTime LeaseUntilUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
