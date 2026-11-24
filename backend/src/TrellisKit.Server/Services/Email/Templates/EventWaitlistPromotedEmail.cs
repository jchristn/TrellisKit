namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// event_waitlist_promoted (docs/PHASE4_CONTRACT.md section 9): A spot opened and you moved from the waitlist to going (sent regardless of the Events email preference).
    /// </summary>
    public sealed class EventWaitlistPromotedEmail : EmailTemplate
    {
        #region Public-Members

        /// <summary>
        /// Template key.
        /// </summary>
        public const string TemplateKey = "event_waitlist_promoted";

        /// <inheritdoc />
        public override string Key
        {
            get { return TemplateKey; }
        }

        /// <inheritdoc />
        public override EmailCategoryEnum Category
        {
            get { return EmailCategoryEnum.Essential; }
        }

        /// <inheritdoc />
        public override EmailRecipientEnum Recipient
        {
            get { return EmailRecipientEnum.Member; }
        }

        /// <inheritdoc />
        public override string Description
        {
            get { return "A spot opened and you moved from the waitlist to going (sent regardless of the Events email preference)."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "A spot opened: you are going to {eventTitle}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "You moved from the waitlist to going."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "You are going to {eventTitle}"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "Hi {memberName}, a spot opened and you are now going to {eventTitle} on {startLocal}.\n\nWhere: {locationText}\n\nAdd it to your calendar: {calendarLink}\n\nIf you cannot make it, cancel your RSVP so the next person can go."; }
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
            { "locationText", "100 Example Street, Exampleville" },
            { "eventLink", "https://example.test/app/events/evt_1" },
            { "calendarLink", "https://api.example.test/api/v1/events/evt_1/ics" }
        };

        #endregion
    }
}
