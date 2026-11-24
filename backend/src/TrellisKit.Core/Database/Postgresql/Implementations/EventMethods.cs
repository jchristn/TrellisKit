namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using System;
    using TrellisKit.Core.Database.Interfaces;

    /// <summary>
    /// PostgreSQL data access of events (occurrences and recurrence, RSVPs, reminders, proposals, calendar feeds).
    /// </summary>
    internal sealed class EventMethods : IEventMethods
    {
        public IEventRecordMethods Records { get; }

        public IEventRecurrenceMethods Recurrences { get; }

        public IEventCategoryMethods Categories { get; }

        public IEventRsvpMethods Rsvps { get; }

        public IEventReminderMethods Reminders { get; }

        public ICalendarFeedMethods Feeds { get; }

        internal EventMethods(DatabaseDriverBase driver, string? propertyId)
        {
            ArgumentNullException.ThrowIfNull(driver);
            Records = new EventRecordMethods(driver, propertyId);
            Recurrences = new EventRecurrenceMethods(driver, propertyId);
            Categories = new EventCategoryMethods(driver, propertyId);
            Rsvps = new EventRsvpMethods(driver, propertyId);
            Reminders = new EventReminderMethods(driver, propertyId);
            Feeds = new CalendarFeedMethods(driver, propertyId);
        }
    }
}
