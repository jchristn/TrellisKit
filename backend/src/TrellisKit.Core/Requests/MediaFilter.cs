namespace TrellisKit.Core.Requests
{
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Typed filters of the admin media library enumeration (filters kind, status, ownerUserId, attached).
    /// </summary>
    public class MediaFilter
    {
        #region Public-Members

        /// <summary>
        /// Only this kind, or null for all.
        /// </summary>
        public MediaKindEnum? Kind { get; set; } = null;

        /// <summary>
        /// Only this status, or null for all.
        /// </summary>
        public MediaStatusEnum? Status { get; set; } = null;

        /// <summary>
        /// Only media of this owner, or null for all.
        /// </summary>
        public string? OwnerUserId { get; set; } = null;

        /// <summary>
        /// True for attached media only, false for unattached only, null for both.
        /// </summary>
        public bool? Attached { get; set; } = null;

        #endregion
    }
}
