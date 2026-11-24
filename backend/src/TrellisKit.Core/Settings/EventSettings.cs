namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Settings of events (occurrences and recurrence, RSVPs, reminders, proposals, calendar feeds) (docs/PHASE4_CONTRACT.md section 12, resolved decisions 10 and 11).
    /// </summary>
    public class EventSettings
    {
        #region Public-Members

        /// <summary>
        /// Most event categories. Default 50. Minimum 1, maximum 200.
        /// </summary>
        public int MaxCategories
        {
            get { return _MaxCategories; }
            set { _MaxCategories = Math.Clamp(value, 1, 200); }
        }

        /// <summary>
        /// Most characters in an event description. Default 10000. Minimum 100, maximum 50000.
        /// </summary>
        public int MaxDescriptionLength
        {
            get { return _MaxDescriptionLength; }
            set { _MaxDescriptionLength = Math.Clamp(value, 100, 50000); }
        }

        /// <summary>
        /// Longest event. Default 14. Minimum 1, maximum 60.
        /// </summary>
        public int MaxDurationDays
        {
            get { return _MaxDurationDays; }
            set { _MaxDurationDays = Math.Clamp(value, 1, 60); }
        }

        /// <summary>
        /// Most hosts per event. Default 10. Minimum 1, maximum 20.
        /// </summary>
        public int MaxHosts
        {
            get { return _MaxHosts; }
            set { _MaxHosts = Math.Clamp(value, 1, 20); }
        }

        /// <summary>
        /// Guests a Going RSVP may bring when the event sets none. Default 0. Minimum 0, maximum 10.
        /// </summary>
        public int DefaultMaxGuestsPerRsvp
        {
            get { return _DefaultMaxGuestsPerRsvp; }
            set { _DefaultMaxGuestsPerRsvp = Math.Clamp(value, 0, 10); }
        }

        /// <summary>
        /// Comma-separated reminder offsets of new events (at most 3, each 5 to 20160). Default "1440,120".
        /// </summary>
        public string DefaultReminderOffsetsMinutes
        {
            get { return _DefaultReminderOffsetsMinutes; }
            set { _DefaultReminderOffsetsMinutes = value ?? String.Empty; }
        }

        /// <summary>
        /// Whether reminders also go to Interested members. Default false.
        /// </summary>
        public bool RemindInterested { get; set; } = false;

        /// <summary>
        /// How often due reminders are claimed. Default 60. Minimum 15, maximum 600.
        /// </summary>
        public int ReminderIntervalSeconds
        {
            get { return _ReminderIntervalSeconds; }
            set { _ReminderIntervalSeconds = Math.Clamp(value, 15, 600); }
        }

        /// <summary>
        /// A reminder later than this is skipped, not sent late. Default 30. Minimum 5, maximum 240.
        /// </summary>
        public int ReminderGraceMinutes
        {
            get { return _ReminderGraceMinutes; }
            set { _ReminderGraceMinutes = Math.Clamp(value, 5, 240); }
        }

        /// <summary>
        /// Waitlist promotion stops this long before the start; freed spots then stay open for anyone. Default 60. Minimum 0, maximum 1440.
        /// </summary>
        public int WaitlistCutoffMinutes
        {
            get { return _WaitlistCutoffMinutes; }
            set { _WaitlistCutoffMinutes = Math.Clamp(value, 0, 1440); }
        }

        /// <summary>
        /// The online join link is shown this long before the start. Default 15. Minimum 0, maximum 1440.
        /// </summary>
        public int JoinUrlRevealMinutes
        {
            get { return _JoinUrlRevealMinutes; }
            set { _JoinUrlRevealMinutes = Math.Clamp(value, 0, 1440); }
        }

        /// <summary>
        /// RSVPs a member may hold on upcoming events. Default 100. Minimum 1, maximum 1000.
        /// </summary>
        public int MaxActiveRsvpsPerMember
        {
            get { return _MaxActiveRsvpsPerMember; }
            set { _MaxActiveRsvpsPerMember = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Personal calendar subscription feeds. Default true.
        /// </summary>
        public bool CalendarFeedEnabled { get; set; } = true;

        /// <summary>
        /// How far ahead a calendar feed lists events. Default 365. Minimum 30, maximum 730.
        /// </summary>
        public int CalendarFeedDays
        {
            get { return _CalendarFeedDays; }
            set { _CalendarFeedDays = Math.Clamp(value, 30, 730); }
        }

        /// <summary>
        /// Most events in one calendar feed. Default 500. Minimum 50, maximum 1000.
        /// </summary>
        public int CalendarFeedMaxEvents
        {
            get { return _CalendarFeedMaxEvents; }
            set { _CalendarFeedMaxEvents = Math.Clamp(value, 50, 1000); }
        }

        /// <summary>
        /// Anonymous /api/v1/public/events routes for Public events. Default false.
        /// </summary>
        public bool PublicListingsEnabled { get; set; } = false;

        /// <summary>
        /// At most one staff RSVP digest per event in this window. Default 24. Minimum 1, maximum 168.
        /// </summary>
        public int StaffRsvpDigestHours
        {
            get { return _StaffRsvpDigestHours; }
            set { _StaffRsvpDigestHours = Math.Clamp(value, 1, 168); }
        }

        /// <summary>
        /// Whether comments on events may carry media. Default true.
        /// </summary>
        public bool CommentMedia { get; set; } = true;

        /// <summary>
        /// RSVP writes per member each minute. Default 30. Minimum 1, maximum 10000.
        /// </summary>
        public int RsvpsPerMinute
        {
            get { return _RsvpsPerMinute; }
            set { _RsvpsPerMinute = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// RSVP writes per member each day. Default 200. Minimum 1, maximum 100000.
        /// </summary>
        public int RsvpsPerDay
        {
            get { return _RsvpsPerDay; }
            set { _RsvpsPerDay = Math.Clamp(value, 1, 100000); }
        }

        /// <summary>
        /// ICS downloads and calendar feed fetches per member or token each hour. Default 60. Minimum 1, maximum 10000.
        /// </summary>
        public int IcsPerHour
        {
            get { return _IcsPerHour; }
            set { _IcsPerHour = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// Events map enumerations per member each minute. Default 120. Minimum 1, maximum 10000.
        /// </summary>
        public int MapPerMinute
        {
            get { return _MapPerMinute; }
            set { _MapPerMinute = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// Most features one events map enumeration returns (the cap of maxResults). Default 500. Minimum 50, maximum 1000.
        /// </summary>
        public int MapMaxFeatures
        {
            get { return _MapMaxFeatures; }
            set { _MapMaxFeatures = Math.Clamp(value, 50, 1000); }
        }

        /// <summary>
        /// Occurrences of a recurring event are materialized this far ahead. Default 180. Minimum 30, maximum 730.
        /// </summary>
        public int RecurrenceHorizonDays
        {
            get { return _RecurrenceHorizonDays; }
            set { _RecurrenceHorizonDays = Math.Clamp(value, 30, 730); }
        }

        /// <summary>
        /// Most occurrences one recurrence materializes (and the largest count). Default 200. Minimum 1, maximum 1000.
        /// </summary>
        public int MaxOccurrences
        {
            get { return _MaxOccurrences; }
            set { _MaxOccurrences = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// How often the recurrence worker extends the horizon. Default 3600. Minimum 60, maximum 86400.
        /// </summary>
        public int RecurrenceIntervalSeconds
        {
            get { return _RecurrenceIntervalSeconds; }
            set { _RecurrenceIntervalSeconds = Math.Clamp(value, 60, 86400); }
        }

        /// <summary>
        /// Whether members may propose events for review (also needs property.json events.proposals). Default true.
        /// </summary>
        public bool ProposalsEnabled { get; set; } = true;

        /// <summary>
        /// Pending proposals a member may have. Default 3. Minimum 1, maximum 50.
        /// </summary>
        public int MaxPendingProposalsPerMember
        {
            get { return _MaxPendingProposalsPerMember; }
            set { _MaxPendingProposalsPerMember = Math.Clamp(value, 1, 50); }
        }

        /// <summary>
        /// Proposals a member may submit each day. Default 5. Minimum 1, maximum 100.
        /// </summary>
        public int ProposalsPerDay
        {
            get { return _ProposalsPerDay; }
            set { _ProposalsPerDay = Math.Clamp(value, 1, 100); }
        }

        #endregion

        #region Private-Members

        private int _MaxCategories = 50;
        private int _MaxDescriptionLength = 10000;
        private int _MaxDurationDays = 14;
        private int _MaxHosts = 10;
        private int _DefaultMaxGuestsPerRsvp = 0;
        private string _DefaultReminderOffsetsMinutes = "1440,120";
        private int _ReminderIntervalSeconds = 60;
        private int _ReminderGraceMinutes = 30;
        private int _WaitlistCutoffMinutes = 60;
        private int _JoinUrlRevealMinutes = 15;
        private int _MaxActiveRsvpsPerMember = 100;
        private int _CalendarFeedDays = 365;
        private int _CalendarFeedMaxEvents = 500;
        private int _StaffRsvpDigestHours = 24;
        private int _RsvpsPerMinute = 30;
        private int _RsvpsPerDay = 200;
        private int _IcsPerHour = 60;
        private int _MapPerMinute = 120;
        private int _MapMaxFeatures = 500;
        private int _RecurrenceHorizonDays = 180;
        private int _MaxOccurrences = 200;
        private int _RecurrenceIntervalSeconds = 3600;
        private int _MaxPendingProposalsPerMember = 3;
        private int _ProposalsPerDay = 5;

        #endregion
    }
}
