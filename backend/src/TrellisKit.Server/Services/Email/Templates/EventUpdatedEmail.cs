namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// event_updated (docs/PHASE4_CONTRACT.md section 9): An event you RSVPed to changed time, location, or format (Events email preference).
    /// </summary>
    public sealed class EventUpdatedEmail : EmailTemplate
    {
        #region Public-Members

        /// <summary>
        /// Template key.
        /// </summary>
        public const string TemplateKey = "event_updated";

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
            get { return "An event you RSVPed to changed time, location, or format (Events email preference)."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "{eventTitle} changed"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "{changeSummary}"; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "{eventTitle} changed"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "Hi {memberName}, {eventTitle} changed: {changeSummary}\n\nWhen: {startLocal}\nWhere: {locationText}\n\nUpdate your calendar: {calendarLink}\n\nTurn off Events emails: {unsubscribeLink}"; }
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
            { "changeSummary", "It now starts at 7:00 PM." },
            { "startLocal", "Saturday, December 5 at 7:00 PM" },
            { "locationText", "100 Example Street, Exampleville" },
            { "eventLink", "https://example.test/app/events/evt_1" },
            { "calendarLink", "https://api.example.test/api/v1/events/evt_1/ics" },
            { "unsubscribeLink", "https://example.test/api/v1/email/unsubscribe?token=sample" }
        };

        #endregion
    }
}
