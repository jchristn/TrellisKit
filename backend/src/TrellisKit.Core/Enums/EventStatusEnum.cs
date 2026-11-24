namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Lifecycle state of an event row (docs/PHASE4_CONTRACT.md section 2.5, resolved decisions 10 and 11).
    /// </summary>
    public enum EventStatusEnum
    {
        /// <summary>
        /// Not published; event staff and hosts only.
        /// </summary>
        Draft,

        /// <summary>
        /// A member proposal waiting for event staff review (resolved decision 10).
        /// </summary>
        Proposed,

        /// <summary>
        /// Visible to its audience.
        /// </summary>
        Published,

        /// <summary>
        /// Canceled; RSVPs are kept for history.
        /// </summary>
        Canceled,

        /// <summary>
        /// A proposal event staff rejected.
        /// </summary>
        Rejected,

        /// <summary>
        /// A proposal its proposer withdrew.
        /// </summary>
        Withdrawn,

        /// <summary>
        /// The hidden series template of a recurrence: the fields new occurrences are copied from. Never listed or readable.
        /// </summary>
        Template
    }
}
