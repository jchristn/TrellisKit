namespace TrellisKit.Core.Models
{
    /// <summary>
    /// Item counts and cost of one run, by status.
    /// </summary>
    public class JobItemCounts
    {
        #region Public-Members

        /// <summary>
        /// Every item.
        /// </summary>
        public int Total { get; set; } = 0;

        /// <summary>
        /// Pending or running items.
        /// </summary>
        public int Open { get; set; } = 0;

        /// <summary>
        /// Succeeded items.
        /// </summary>
        public int Succeeded { get; set; } = 0;

        /// <summary>
        /// Failed items.
        /// </summary>
        public int Failed { get; set; } = 0;

        /// <summary>
        /// Dead-lettered items.
        /// </summary>
        public int Dead { get; set; } = 0;

        /// <summary>
        /// Skipped items.
        /// </summary>
        public int Skipped { get; set; } = 0;

        /// <summary>
        /// Cost in micro-dollars.
        /// </summary>
        public long CostMicros { get; set; } = 0;

        #endregion
    }
}
