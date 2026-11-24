namespace TrellisKit.Server.Settings
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Settings registry entries of events (occurrences and recurrence, RSVPs, reminders, proposals, calendar feeds) (docs/PHASE4_CONTRACT.md section 12).
    /// </summary>
    public static class EventsSettingFields
    {
        #region Public-Methods

        /// <summary>
        /// The definitions.
        /// </summary>
        /// <returns>Definitions.</returns>
        public static List<SettingFieldDefinition> Build()
        {
            const SettingApplyModeEnum live = SettingApplyModeEnum.Live;
            return new List<SettingFieldDefinition>
            {
                SettingFieldDefinition.Integer("events.maxCategories", "Events", "Max categories",
                    "Most event categories.", live, null, 1, 200,
                    s => s.Events.MaxCategories, (s, v) => s.Events.MaxCategories = v),
                SettingFieldDefinition.Integer("events.maxDescriptionLength", "Events", "Max description length",
                    "Most characters in an event description.", live, null, 100, 50000,
                    s => s.Events.MaxDescriptionLength, (s, v) => s.Events.MaxDescriptionLength = v),
                SettingFieldDefinition.Integer("events.maxDurationDays", "Events", "Max duration (days)",
                    "Longest event.", live, null, 1, 60,
                    s => s.Events.MaxDurationDays, (s, v) => s.Events.MaxDurationDays = v),
                SettingFieldDefinition.Integer("events.maxHosts", "Events", "Max hosts",
                    "Most hosts per event.", live, null, 1, 20,
                    s => s.Events.MaxHosts, (s, v) => s.Events.MaxHosts = v),
                SettingFieldDefinition.Integer("events.defaultMaxGuestsPerRsvp", "Events", "Default guests per RSVP",
                    "Guests a Going RSVP may bring when the event sets none.", live, null, 0, 10,
                    s => s.Events.DefaultMaxGuestsPerRsvp, (s, v) => s.Events.DefaultMaxGuestsPerRsvp = v),
                SettingFieldDefinition.Text("events.defaultReminderOffsetsMinutes", "Events", "Default reminder offsets (minutes)",
                    "Comma-separated reminder offsets of new events (at most 3, each 5 to 20160).", live, null,
                    s => s.Events.DefaultReminderOffsetsMinutes, (s, v) => s.Events.DefaultReminderOffsetsMinutes = (v ?? String.Empty).Trim()),
                SettingFieldDefinition.Boolean("events.remindInterested", "Events", "Remind Interested members",
                    "Whether reminders also go to Interested members.", live, null,
                    s => s.Events.RemindInterested, (s, v) => s.Events.RemindInterested = v),
                SettingFieldDefinition.Integer("events.reminderIntervalSeconds", "Events", "Reminder interval (seconds)",
                    "How often due reminders are claimed.", live, null, 15, 600,
                    s => s.Events.ReminderIntervalSeconds, (s, v) => s.Events.ReminderIntervalSeconds = v),
                SettingFieldDefinition.Integer("events.reminderGraceMinutes", "Events", "Reminder grace (minutes)",
                    "A reminder later than this is skipped, not sent late.", live, null, 5, 240,
                    s => s.Events.ReminderGraceMinutes, (s, v) => s.Events.ReminderGraceMinutes = v),
                SettingFieldDefinition.Integer("events.waitlistCutoffMinutes", "Events", "Waitlist cutoff (minutes)",
                    "Waitlist promotion stops this long before the start; freed spots then stay open for anyone.", live, null, 0, 1440,
                    s => s.Events.WaitlistCutoffMinutes, (s, v) => s.Events.WaitlistCutoffMinutes = v),
                SettingFieldDefinition.Integer("events.joinUrlRevealMinutes", "Events", "Join link reveal (minutes)",
                    "The online join link is shown this long before the start.", live, null, 0, 1440,
                    s => s.Events.JoinUrlRevealMinutes, (s, v) => s.Events.JoinUrlRevealMinutes = v),
                SettingFieldDefinition.Integer("events.maxActiveRsvpsPerMember", "Events", "Max active RSVPs per member",
                    "RSVPs a member may hold on upcoming events.", live, null, 1, 1000,
                    s => s.Events.MaxActiveRsvpsPerMember, (s, v) => s.Events.MaxActiveRsvpsPerMember = v),
                SettingFieldDefinition.Boolean("events.calendarFeedEnabled", "Events", "Calendar feeds enabled",
                    "Personal calendar subscription feeds.", live, null,
                    s => s.Events.CalendarFeedEnabled, (s, v) => s.Events.CalendarFeedEnabled = v),
                SettingFieldDefinition.Integer("events.calendarFeedDays", "Events", "Calendar feed days ahead",
                    "How far ahead a calendar feed lists events.", live, null, 30, 730,
                    s => s.Events.CalendarFeedDays, (s, v) => s.Events.CalendarFeedDays = v),
                SettingFieldDefinition.Integer("events.calendarFeedMaxEvents", "Events", "Calendar feed max events",
                    "Most events in one calendar feed.", live, null, 50, 1000,
                    s => s.Events.CalendarFeedMaxEvents, (s, v) => s.Events.CalendarFeedMaxEvents = v),
                SettingFieldDefinition.Boolean("events.publicListingsEnabled", "Events", "Public listings",
                    "Anonymous /api/v1/public/events routes for Public events.", live, null,
                    s => s.Events.PublicListingsEnabled, (s, v) => s.Events.PublicListingsEnabled = v),
                SettingFieldDefinition.Integer("events.staffRsvpDigestHours", "Events", "Staff RSVP digest (hours)",
                    "At most one staff RSVP digest per event in this window.", live, null, 1, 168,
                    s => s.Events.StaffRsvpDigestHours, (s, v) => s.Events.StaffRsvpDigestHours = v),
                SettingFieldDefinition.Boolean("events.commentMedia", "Events", "Comment media",
                    "Whether comments on events may carry media.", live, null,
                    s => s.Events.CommentMedia, (s, v) => s.Events.CommentMedia = v),
                SettingFieldDefinition.Integer("events.rsvpsPerMinute", "Events", "RSVPs per minute",
                    "RSVP writes per member each minute.", live, null, 1, 10000,
                    s => s.Events.RsvpsPerMinute, (s, v) => s.Events.RsvpsPerMinute = v),
                SettingFieldDefinition.Integer("events.rsvpsPerDay", "Events", "RSVPs per day",
                    "RSVP writes per member each day.", live, null, 1, 100000,
                    s => s.Events.RsvpsPerDay, (s, v) => s.Events.RsvpsPerDay = v),
                SettingFieldDefinition.Integer("events.icsPerHour", "Events", "ICS per hour",
                    "ICS downloads and calendar feed fetches per member or token each hour.", live, null, 1, 10000,
                    s => s.Events.IcsPerHour, (s, v) => s.Events.IcsPerHour = v),
                SettingFieldDefinition.Integer("events.mapPerMinute", "Events", "Map queries per minute",
                    "Events map enumerations per member each minute.", live, null, 1, 10000,
                    s => s.Events.MapPerMinute, (s, v) => s.Events.MapPerMinute = v),
                SettingFieldDefinition.Integer("events.mapMaxFeatures", "Events", "Map features per query",
                    "Most features one events map enumeration returns (points, or grid clusters above it).", live, null, 50, 1000,
                    s => s.Events.MapMaxFeatures, (s, v) => s.Events.MapMaxFeatures = v),
                SettingFieldDefinition.Integer("events.recurrenceHorizonDays", "Events", "Recurrence horizon (days)",
                    "Occurrences of a recurring event are materialized this far ahead.", live, null, 30, 730,
                    s => s.Events.RecurrenceHorizonDays, (s, v) => s.Events.RecurrenceHorizonDays = v),
                SettingFieldDefinition.Integer("events.maxOccurrences", "Events", "Max occurrences",
                    "Most occurrences one recurrence materializes (and the largest count).", live, null, 1, 1000,
                    s => s.Events.MaxOccurrences, (s, v) => s.Events.MaxOccurrences = v),
                SettingFieldDefinition.Integer("events.recurrenceIntervalSeconds", "Events", "Recurrence interval (seconds)",
                    "How often the recurrence worker extends the horizon.", live, null, 60, 86400,
                    s => s.Events.RecurrenceIntervalSeconds, (s, v) => s.Events.RecurrenceIntervalSeconds = v),
                SettingFieldDefinition.Boolean("events.proposalsEnabled", "Events", "Member proposals",
                    "Whether members may propose events for review (also needs property.json events.proposals).", live, null,
                    s => s.Events.ProposalsEnabled, (s, v) => s.Events.ProposalsEnabled = v),
                SettingFieldDefinition.Integer("events.maxPendingProposalsPerMember", "Events", "Max pending proposals",
                    "Pending proposals a member may have.", live, null, 1, 50,
                    s => s.Events.MaxPendingProposalsPerMember, (s, v) => s.Events.MaxPendingProposalsPerMember = v),
                SettingFieldDefinition.Integer("events.proposalsPerDay", "Events", "Proposals per day",
                    "Proposals a member may submit each day.", live, null, 1, 100,
                    s => s.Events.ProposalsPerDay, (s, v) => s.Events.ProposalsPerDay = v)
            };
        }

        #endregion
    }
}
