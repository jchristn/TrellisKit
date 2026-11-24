namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Publication state of a feature flag.
    /// </summary>
    public enum FeatureFlagStateEnum
    {
        /// <summary>
        /// Never published.
        /// </summary>
        Draft,

        /// <summary>
        /// Live in manifests.
        /// </summary>
        Published,

        /// <summary>
        /// Removed from manifests at the next publish; the key stays reserved.
        /// </summary>
        Archived
    }
}
