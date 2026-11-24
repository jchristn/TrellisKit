namespace TrellisKit.Server.Services.Usage
{
    /// <summary>
    /// Query of the usage summary and export routes (query string values as sent).
    /// </summary>
    public sealed class UsageQuery
    {
        #region Public-Members

        /// <summary>
        /// Period name (default LastMonth).
        /// </summary>
        public string? Period { get; set; } = null;

        /// <summary>
        /// Range start.
        /// </summary>
        public string? From { get; set; } = null;

        /// <summary>
        /// Range end.
        /// </summary>
        public string? To { get; set; } = null;

        /// <summary>
        /// Bucket override.
        /// </summary>
        public string? Bucket { get; set; } = null;

        /// <summary>
        /// Comma-separated meter ids and group names.
        /// </summary>
        public string? Meters { get; set; } = null;

        /// <summary>
        /// Export format: json (default) or csv.
        /// </summary>
        public string? Format { get; set; } = null;

        /// <summary>
        /// Export granularity: Day (default) or Hour.
        /// </summary>
        public string? Granularity { get; set; } = null;

        #endregion
    }
}
