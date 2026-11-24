namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Kind of a recurrence exception (resolved decision 11).
    /// </summary>
    public enum EventRecurrenceExceptionKindEnum
    {
        /// <summary>
        /// The occurrence was edited on its own (an override).
        /// </summary>
        Edited,

        /// <summary>
        /// The occurrence was canceled on its own.
        /// </summary>
        Canceled,

        /// <summary>
        /// A draft occurrence was deleted; it is never materialized again.
        /// </summary>
        Deleted
    }
}
