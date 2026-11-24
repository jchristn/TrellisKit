namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// staff_event_rsvp_digest (docs/PHASE4_CONTRACT.md section 9): RSVP changes for an event you host or manage (at most one per event per events.staffRsvpDigestHours).
    /// </summary>
    public sealed class StaffEventRsvpDigestEmail : EmailTemplate
    {
        #region Public-Members

        /// <summary>
        /// Template key.
        /// </summary>
        public const string TemplateKey = "staff_event_rsvp_digest";

        /// <inheritdoc />
        public override string Key
        {
            get { return TemplateKey; }
        }

        /// <inheritdoc />
        public override EmailCategoryEnum Category
        {
            get { return EmailCategoryEnum.Staff; }
        }

        /// <inheritdoc />
        public override EmailRecipientEnum Recipient
        {
            get { return EmailRecipientEnum.Staff; }
        }

        /// <inheritdoc />
        public override string Description
        {
            get { return "RSVP changes for an event you host or manage (at most one per event per events.staffRsvpDigestHours)."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "RSVPs for {eventTitle}: {going} going"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "{changes}"; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "RSVP update for {eventTitle}"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "{eventTitle} now has {going} going and {waitlisted} on the waitlist.\n\nChanges: {changes}"; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "Open RSVPs"; }
        }

        /// <inheritdoc />
        public override string ButtonToken
        {
            get { return "adminLink"; }
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
            { "going", "37" },
            { "waitlisted", "2" },
            { "changes", "5 new, 1 canceled" },
            { "adminLink", "https://example.test/admin/events/evt_1" }
        };

        #endregion
    }
}
