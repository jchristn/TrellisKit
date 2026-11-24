namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Member search profile filter operator (docs/MULTIPROPERTY_CONTRACT.md section 9.12).
    /// </summary>
    public enum ProfileFilterOperatorEnum
    {
        /// <summary>
        /// Equals.
        /// </summary>
        Eq,

        /// <summary>
        /// Normalized text starts with.
        /// </summary>
        Prefix,

        /// <summary>
        /// At least (number or date).
        /// </summary>
        Gte,

        /// <summary>
        /// At most (number or date).
        /// </summary>
        Lte,

        /// <summary>
        /// One of the keys or codes.
        /// </summary>
        In,

        /// <summary>
        /// MultiSelect holds at least one key.
        /// </summary>
        Any,

        /// <summary>
        /// MultiSelect holds every key.
        /// </summary>
        All,

        /// <summary>
        /// Rank at or above an option.
        /// </summary>
        MinRank,

        /// <summary>
        /// Rank at or below an option.
        /// </summary>
        MaxRank,

        /// <summary>
        /// Rank level at least.
        /// </summary>
        MinLevel,

        /// <summary>
        /// Has (or has no) value.
        /// </summary>
        HasValue
    }
}
