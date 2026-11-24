namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// event_reminder (docs/PHASE4_CONTRACT.md section 9): Event reminder (the largest reminder offset; Events email preference).
    /// </summary>
    public sealed class EventReminderEmail : EmailTemplate
    {
        #region Public-Members

        /// <summary>
        /// Template key.
        /// </summary>
        public const string TemplateKey = "event_reminder";

        /// <inheritdoc />
        public override string Key
        {
            get { return TemplateKey; }
        }

        /// <inheritdoc />
        public override EmailCategoryEnum Category
        {
            get { return EmailCategoryEnum.Optional; }
        }

        /// <inheritdoc />
        public override EmailRecipientEnum Recipient
        {
            get { return EmailRecipientEnum.Member; }
        }

        /// <inheritdoc />
        public override string Description
        {
            get { return "Event reminder (the largest reminder offset; Events email preference)."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "Reminder: {eventTitle} starts {startLocal}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "{eventTitle} is coming up."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "{eventTitle} is coming up"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "Hi {memberName}, a reminder that {eventTitle} starts {startLocal} ({timeZone}).\n\nWhere: {locationText}\n{joinNote}\n\nAdd it to your calendar: {calendarLink}\n\nYou get event reminders because Events emails are on in your notification settings. Turn them off: {unsubscribeLink}"; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "Open event"; }
        }

        /// <inheritdoc />
        public override string ButtonToken
        {
            get { return "eventLink"; }
        }

        /// <inheritdoc />
        public override IReadOnlyDictionary<string, string> Samples
        {
            get { return _Samples; }
        }

        #endregion

        #region Private-Members

        private static readonly Dictionary<string, string> _Samples = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "eventTitle", "Autumn meetup" },
            { "startLocal", "Saturday, December 5 at 6:00 PM" },
            { "timeZone", "America/Los_Angeles" },
            { "locationText", "100 Example Street, Exampleville" },
            { "joinNote", "The join link is available from 5:45 PM." },
            { "eventLink", "https://example.test/app/events/evt_1" },
            { "calendarLink", "https://api.example.test/api/v1/events/evt_1/ics" },
            { "unsubscribeLink", "https://example.test/api/v1/email/unsubscribe?token=sample" }
        };

        #endregion
    }
}
