namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Why a Training hub section is not available to the caller (docs/TRELLISKIT.md, "Training hub"). Reported only
    /// when a section's <c>available</c> is false.
    /// </summary>
    public enum TrainingSectionReasonEnum
    {
        /// <summary>
        /// The property feature switch, or a kill switch, behind the section is off.
        /// </summary>
        FeatureOff,

        /// <summary>
        /// The caller's access tier does not reach the section.
        /// </summary>
        NoAccess,

        /// <summary>
        /// The section is reachable but has nothing to show the caller yet.
        /// </summary>
        NoData,

        /// <summary>
        /// The section needs a signed-in account.
        /// </summary>
        SignedOut
    }
}
