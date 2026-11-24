namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of POST /api/v1/admin/members/{id}/comp.
    /// </summary>
    public class CompGrantRequest
    {
        #region Public-Members

        /// <summary>
        /// DurationMonths, FixedEndDate, or Lifetime.
        /// </summary>
        public string? GrantType { get; set; } = null;

        /// <summary>
        /// Months for DurationMonths, 1 to 120.
        /// </summary>
        public int? Months { get; set; } = null;

        /// <summary>
        /// End for FixedEndDate (future).
        /// </summary>
        public DateTime? EndUtc { get; set; } = null;

        /// <summary>
        /// Reason (required).
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// Labels (replace the stored list when present; omitted means unchanged). At most 50.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags (replace the stored dictionary when present; omitted means unchanged). At most 50 keys.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
