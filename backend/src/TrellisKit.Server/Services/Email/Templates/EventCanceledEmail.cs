namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// event_canceled (docs/PHASE4_CONTRACT.md section 9): An event you RSVPed to was canceled (sent regardless of the Events email preference).
    /// </summary>
    public sealed class EventCanceledEmail : EmailTemplate
    {
        #region Public-Members

        /// <summary>
        /// Template key.
        /// </summary>
        public const string TemplateKey = "event_canceled";

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
            get { return "An event you RSVPed to was canceled (sent regardless of the Events email preference)."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "{eventTitle} was canceled"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "The event on {startLocal} will not take place."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "{eventTitle} was canceled"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "Hi {memberName}, {eventTitle} on {startLocal} was canceled.\n\nReason: {reason}"; }
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
            { "reason", "The venue is closed for repairs." },
            { "eventLink", "https://example.test/app/events/evt_1" }
        };

        #endregion
    }
}
