namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Kind of a feature flag (docs/LIVE_UPDATES_CONTRACT.md section 12.1).
    /// </summary>
    public enum FeatureFlagKindEnum
    {
        /// <summary>
        /// On or off.
        /// </summary>
        Boolean,

        /// <summary>
        /// One of a fixed list of string variants.
        /// </summary>
        Variant,

        /// <summary>
        /// A free value of the flag's value type.
        /// </summary>
        Value,

        /// <summary>
        /// An emergency switch for an engine feature key, changed live outside content versioning.
        /// </summary>
        KillSwitch
    }
}
