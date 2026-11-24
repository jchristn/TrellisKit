namespace TrellisKit.Core.Requests
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Typed filters of the remote asset enumeration.
    /// </summary>
    public class RemoteAssetFilter
    {
        /// <summary>
        /// State, or null.
        /// </summary>
        public FeatureFlagStateEnum? State { get; set; } = null;

        /// <summary>
        /// Whether a draft is pending, or null.
        /// </summary>
        public bool? HasDraftChanges { get; set; } = null;
    }
}
