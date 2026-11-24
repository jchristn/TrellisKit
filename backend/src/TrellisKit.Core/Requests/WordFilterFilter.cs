namespace TrellisKit.Core.Requests
{
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Typed filters of the admin word filter enumeration (parsed from EnumerationQuery.Filters).
    /// </summary>
    public class WordFilterFilter
    {
        #region Public-Members

        /// <summary>
        /// Only terms with this action, or null.
        /// </summary>
        public WordFilterActionEnum? Action { get; set; } = null;

        /// <summary>
        /// Only terms with this match type, or null.
        /// </summary>
        public WordFilterMatchTypeEnum? MatchType { get; set; } = null;

        #endregion
    }
}
