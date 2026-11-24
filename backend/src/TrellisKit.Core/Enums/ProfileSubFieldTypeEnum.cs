namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Type of a History sub-field (docs/MULTIPROPERTY_CONTRACT.md section 9.2).
    /// </summary>
    public enum ProfileSubFieldTypeEnum
    {
        /// <summary>
        /// Text.
        /// </summary>
        Text,

        /// <summary>
        /// Number.
        /// </summary>
        Number,

        /// <summary>
        /// Calendar date (YYYY-MM-DD).
        /// </summary>
        Date,

        /// <summary>
        /// True or false.
        /// </summary>
        Boolean,

        /// <summary>
        /// Absolute http or https URL.
        /// </summary>
        Url,

        /// <summary>
        /// One option key.
        /// </summary>
        SingleSelect,

        /// <summary>
        /// An option (and optional level) of a Rank field of the property.
        /// </summary>
        RankValue,

        /// <summary>
        /// A name with an optional member id of the same property.
        /// </summary>
        Member
    }
}
