namespace TrellisKit.Core.Enumeration
{
    using System;
    using TrellisKit.Core.Requests;

    /// <summary>
    /// A validated, clamped enumeration ready for the database helper: built by <see cref="EnumerationNormalizer"/>.
    /// </summary>
    public class EnumerationRequest
    {
        #region Public-Members

        /// <summary>
        /// Collection.
        /// </summary>
        public EnumerationCollection Collection { get; set; } = null!;

        /// <summary>
        /// The original query (filters, search, labels, tags, window).
        /// </summary>
        public EnumerationQuery Query { get; set; } = new EnumerationQuery();

        /// <summary>
        /// Effective page size.
        /// </summary>
        public int MaxResults { get; set; } = 25;

        /// <summary>
        /// Records to skip (0 when a continuation token is used).
        /// </summary>
        public int Skip { get; set; } = 0;

        /// <summary>
        /// Effective ordering.
        /// </summary>
        public EnumerationOrdering Ordering { get; set; } = new EnumerationOrdering();

        /// <summary>
        /// Sort key of the last item of the previous page (token), or null for the first page.
        /// </summary>
        public string? AfterKey { get; set; } = null;

        /// <summary>
        /// Id of the last item of the previous page (token), or null for the first page.
        /// </summary>
        public string? AfterId { get; set; } = null;

        /// <summary>
        /// Whether a continuation token positioned this page.
        /// </summary>
        public bool HasPosition
        {
            get { return AfterId != null; }
        }

        /// <summary>
        /// Fingerprint of the collection scope and filters, carried in continuation tokens.
        /// </summary>
        public string Fingerprint { get; set; } = String.Empty;

        /// <summary>
        /// Whether to count totalRecords and recordsRemaining.
        /// </summary>
        public bool CountTotals { get; set; } = true;

        /// <summary>
        /// Whether the caller may see staff: labels and tags (and filter on them).
        /// </summary>
        public bool IncludeStaffMetadata { get; set; } = false;

        #endregion
    }
}
