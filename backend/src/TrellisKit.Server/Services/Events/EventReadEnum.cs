namespace TrellisKit.Server.Services
{
    /// <summary>
    /// How a caller may see an event.
    /// </summary>
    public enum EventReadEnum
    {
        /// <summary>
        /// Not visible (404 NotFound, ids do not leak).
        /// </summary>
        Hidden,

        /// <summary>
        /// A locked stub (PartiallyGated, a signed-in caller without the membership the audience needs).
        /// </summary>
        Locked,

        /// <summary>
        /// Readable.
        /// </summary>
        Open
    }
}
