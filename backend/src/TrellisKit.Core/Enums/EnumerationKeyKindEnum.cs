namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Type of an enumeration ordering key, used to encode and compare continuation tokens.
    /// </summary>
    public enum EnumerationKeyKindEnum
    {
        /// <summary>
        /// A text value.
        /// </summary>
        Text,

        /// <summary>
        /// A UTC timestamp.
        /// </summary>
        Time,

        /// <summary>
        /// A whole number.
        /// </summary>
        Number
    }
}
