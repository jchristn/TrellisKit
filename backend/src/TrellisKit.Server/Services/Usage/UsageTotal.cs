namespace TrellisKit.Server.Services.Usage
{
    /// <summary>
    /// A period total of one meter.
    /// </summary>
    public sealed class UsageTotal
    {
        #region Public-Members

        /// <summary>
        /// Value.
        /// </summary>
        public long Value { get; set; } = 0;

        /// <summary>
        /// False when the value is a lower bound.
        /// </summary>
        public bool Exact { get; set; } = true;

        #endregion
    }
}
