namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Output of every list route (docs/ENUMERATION_AND_METADATA.md section 2.3).
    /// </summary>
    /// <typeparam name="T">Object type.</typeparam>
    public class EnumerationResult<T>
    {
        #region Public-Members

        /// <summary>
        /// Always true for a successful enumeration.
        /// </summary>
        public bool Success { get; set; } = true;

        /// <summary>
        /// Effective page size after clamping.
        /// </summary>
        public int MaxResults { get; set; } = 25;

        /// <summary>
        /// Records skipped (0 when a continuation token was used).
        /// </summary>
        public int Skip { get; set; } = 0;

        /// <summary>
        /// Token for the next page, or null when endOfResults is true.
        /// </summary>
        public string? ContinuationToken { get; set; } = null;

        /// <summary>
        /// Whether this is the last page.
        /// </summary>
        public bool EndOfResults { get; set; } = true;

        /// <summary>
        /// Total matching records, or null when not counted (member lists with enumeration.countMemberTotals off).
        /// </summary>
        public long? TotalRecords { get; set; } = null;

        /// <summary>
        /// Matching records after this page, or null when not counted.
        /// </summary>
        public long? RecordsRemaining { get; set; } = null;

        /// <summary>
        /// Server time of the enumeration (UTC).
        /// </summary>
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Objects on this page (at most maxResults). Never null.
        /// </summary>
        public List<T> Objects
        {
            get { return _Objects; }
            set { _Objects = value ?? new List<T>(); }
        }

        #endregion

        #region Private-Members

        private List<T> _Objects = new List<T>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// A result with the same paging fields and different objects (for example after mapping rows to views).
        /// </summary>
        /// <typeparam name="TOut">New object type.</typeparam>
        /// <param name="objects">Objects.</param>
        /// <returns>Result.</returns>
        public EnumerationResult<TOut> With<TOut>(List<TOut> objects)
        {
            return new EnumerationResult<TOut>
            {
                Success = Success,
                MaxResults = MaxResults,
                Skip = Skip,
                ContinuationToken = ContinuationToken,
                EndOfResults = EndOfResults,
                TotalRecords = TotalRecords,
                RecordsRemaining = RecordsRemaining,
                TimestampUtc = TimestampUtc,
                Objects = objects
            };
        }

        /// <summary>
        /// A single-page result over a fixed, already bounded list (for example at most 20 requested ids).
        /// </summary>
        /// <param name="objects">Objects.</param>
        /// <returns>Result.</returns>
        public static EnumerationResult<T> Of(List<T> objects)
        {
            List<T> list = objects ?? new List<T>();
            return new EnumerationResult<T> { MaxResults = Math.Max(1, list.Count), Objects = list, EndOfResults = true, TotalRecords = list.Count, RecordsRemaining = 0 };
        }

        #endregion
    }
}
