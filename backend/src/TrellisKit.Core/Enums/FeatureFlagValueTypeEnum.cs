namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Value type of a feature flag.
    /// </summary>
    public enum FeatureFlagValueTypeEnum
    {
        /// <summary>
        /// true or false.
        /// </summary>
        Boolean,

        /// <summary>
        /// Text up to 500 characters.
        /// </summary>
        String,

        /// <summary>
        /// A 64-bit whole number.
        /// </summary>
        Integer,

        /// <summary>
        /// A decimal number.
        /// </summary>
        Decimal
    }
}
