namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Profile field type (docs/MULTIPROPERTY_CONTRACT.md section 9.1).
    /// </summary>
    public enum ProfileFieldTypeEnum
    {
        /// <summary>
        /// Single-line text.
        /// </summary>
        Text,

        /// <summary>
        /// Multi-line plain text.
        /// </summary>
        LongText,

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
        /// Email address.
        /// </summary>
        Email,

        /// <summary>
        /// E.164 phone number.
        /// </summary>
        Phone,

        /// <summary>
        /// ISO 3166-1 alpha-2 country code.
        /// </summary>
        Country,

        /// <summary>
        /// One option, optionally with free text.
        /// </summary>
        SingleSelect,

        /// <summary>
        /// Several options, optionally with free text.
        /// </summary>
        MultiSelect,

        /// <summary>
        /// Ordered options with colors plus a level.
        /// </summary>
        Rank,

        /// <summary>
        /// Repeating entries with configurable sub-fields.
        /// </summary>
        History,

        /// <summary>
        /// Built-in avatar only.
        /// </summary>
        Image
    }
}
