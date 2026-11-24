namespace TrellisKit.Core.Database.Interfaces
{
    /// <summary>
    /// Data access of events (occurrences and recurrence, RSVPs, reminders, proposals, calendar feeds) (docs/PHASE4_CONTRACT.md section 14): a container of method sets, each bound to
    /// one property through the property-bound driver.
    /// </summary>
    public interface IEventMethods
    {
        /// <summary>
        /// Event rows, hosts, instructors, reminder offsets, and the directory reads events need.
        /// </summary>
        IEventRecordMethods Records { get; }

        /// <summary>
        /// Recurrence rules and exceptions.
        /// </summary>
        IEventRecurrenceMethods Recurrences { get; }

        /// <summary>
        /// Event categories.
        /// </summary>
        IEventCategoryMethods Categories { get; }

        /// <summary>
        /// RSVPs.
        /// </summary>
        IEventRsvpMethods Rsvps { get; }

        /// <summary>
        /// Reminders.
        /// </summary>
        IEventReminderMethods Reminders { get; }

        /// <summary>
        /// Personal calendar feeds.
        /// </summary>
        ICalendarFeedMethods Feeds { get; }
    }
}
