namespace TrellisKit.Core.Enumeration
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One allowed ordering of a collection: a SQL sort expression (never null for any row; wrap nullable columns in
    /// COALESCE), a direction, and the key type used in continuation tokens. Ties break on the collection id column.
    /// </summary>
    public class EnumerationOrdering
    {
        #region Public-Members

        /// <summary>
        /// Ordering name sent by clients, for example CreatedDescending.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// SQL expression sorted on, for example "createdutc" or "COALESCE(publishedutc, createdutc)".
        /// </summary>
        public string Expression { get; set; } = String.Empty;

        /// <summary>
        /// Whether the ordering is descending.
        /// </summary>
        public bool Descending { get; set; } = true;

        /// <summary>
        /// Type of the expression value.
        /// </summary>
        public EnumerationKeyKindEnum KeyKind { get; set; } = EnumerationKeyKindEnum.Time;

        #endregion
    }
}
