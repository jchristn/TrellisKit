namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Lifecycle status of a registered property.
    /// </summary>
    public enum PropertyStatusEnum
    {
        /// <summary>
        /// Registered but not serving (new properties start here). Requests resolve to 404 PropertyNotFound.
        /// </summary>
        Disabled,

        /// <summary>
        /// Serving requests.
        /// </summary>
        Enabled,

        /// <summary>
        /// Deleted and disabled; restorable until the purge starts (platform.propertyPurgeDelayHours, default 30 days).
        /// </summary>
        Deleting,

        /// <summary>
        /// Purged tombstone. The id stays reserved forever.
        /// </summary>
        Deleted
    }
}
