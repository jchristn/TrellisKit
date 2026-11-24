namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// One bucket a usage computation reads: a key and the half-open UTC interval [StartUtc, EndUtc) its rows fall in.
    /// </summary>
    public sealed class UsageBucketRange
    {
        #region Public-Members

        /// <summary>
        /// Bucket key (an hour start in ISO 8601, or a local day yyyy-MM-dd, or a caller-chosen label).
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Inclusive start (UTC).
        /// </summary>
        public DateTime StartUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Exclusive end (UTC).
        /// </summary>
        public DateTime EndUtc { get; set; } = DateTime.MinValue;

        #endregion
    }
}
