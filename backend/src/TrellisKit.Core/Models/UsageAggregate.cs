namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// The aggregate of one meter's stored rows inside one bucket: sum, latest value, and largest value.
    /// </summary>
    public sealed class UsageAggregate
    {
        #region Public-Members

        /// <summary>
        /// Bucket key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Meter id.
        /// </summary>
        public string Meter { get; set; } = String.Empty;

        /// <summary>
        /// Sum of the values.
        /// </summary>
        public long Sum { get; set; } = 0;

        /// <summary>
        /// Value of the latest row.
        /// </summary>
        public long Last { get; set; } = 0;

        /// <summary>
        /// Largest value.
        /// </summary>
        public long Max { get; set; } = 0;

        /// <summary>
        /// Rows aggregated.
        /// </summary>
        public long Rows { get; set; } = 0;

        #endregion
    }
}
