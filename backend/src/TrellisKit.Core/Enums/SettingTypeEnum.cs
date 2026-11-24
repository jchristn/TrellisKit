namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Data type of a runtime-editable setting, used to render the admin settings form.
    /// </summary>
    public enum SettingTypeEnum
    {
        /// <summary>
        /// True or false.
        /// </summary>
        Boolean,

        /// <summary>
        /// Whole number, optionally bounded by a minimum and maximum.
        /// </summary>
        Integer,

        /// <summary>
        /// Free text.
        /// </summary>
        String,

        /// <summary>
        /// Secret text. Never returned to clients; only its set or not set state is reported.
        /// </summary>
        Secret,

        /// <summary>
        /// One of a fixed list of options.
        /// </summary>
        Enum,

        /// <summary>
        /// A decimal number (double, invariant culture) with a minimum and maximum.
        /// </summary>
        Decimal
    }
}
