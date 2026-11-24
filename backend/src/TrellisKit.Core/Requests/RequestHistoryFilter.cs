namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Filter for enumerating, summarizing, and bulk deleting request history.
    /// </summary>
    public class RequestHistoryFilter
    {
        #region Public-Members

        /// <summary>
        /// Only entries for this user. Null for any.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Only entries with this HTTP method (case-insensitive). Null for any.
        /// </summary>
        public string? Method
        {
            get { return _Method; }
            set { _Method = String.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant(); }
        }

        /// <summary>
        /// Only entries with this status code. Null for any.
        /// </summary>
        public int? StatusCode { get; set; } = null;

        /// <summary>
        /// Only entries whose path contains this text (case-sensitive substring). Null for any.
        /// </summary>
        public string? PathContains
        {
            get { return _PathContains; }
            set { _PathContains = String.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
        }

        /// <summary>Exact retained metric dimension; null for any.</summary>
        public string? RouteTemplate { get; set; } = null;

        /// <summary>Exact retained metric dimension; null for any.</summary>
        public string? StatusClass { get; set; } = null;

        /// <summary>Exact retained metric dimension; null for any.</summary>
        public string? ErrorCode { get; set; } = null;

        /// <summary>Exact retained metric dimension; null for any.</summary>
        public string? ClientApp { get; set; } = null;

        /// <summary>Exact retained metric dimension; null for any.</summary>
        public string? ClientPlatform { get; set; } = null;

        /// <summary>Exact retained metric dimension; null for any.</summary>
        public string? ClientVersion { get; set; } = null;

        /// <summary>
        /// Inclusive lower bound on creation time (UTC). Null for unbounded.
        /// </summary>
        public DateTime? FromUtc { get; set; } = null;

        /// <summary>
        /// Exclusive upper bound on creation time (UTC). Null for unbounded.
        /// </summary>
        public DateTime? ToUtc { get; set; } = null;

        /// <summary>
        /// Summary bucket width in minutes. Default 15. Minimum 1, maximum 1440.
        /// </summary>
        public int BucketMinutes
        {
            get { return _BucketMinutes; }
            set { _BucketMinutes = Math.Clamp(value, 1, 1440); }
        }

        #endregion

        #region Private-Members

        private string? _Method = null;
        private string? _PathContains = null;
        private int _BucketMinutes = 15;

        #endregion
    }
}
