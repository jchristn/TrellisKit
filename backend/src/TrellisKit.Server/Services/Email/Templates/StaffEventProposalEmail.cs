namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// staff_event_proposal (docs/PHASE4_CONTRACT.md section 9): A member proposed an event for review (event staff).
    /// </summary>
    public sealed class StaffEventProposalEmail : EmailTemplate
    {
        #region Public-Members

        /// <summary>
        /// Template key.
        /// </summary>
        public const string TemplateKey = "staff_event_proposal";

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
            get { return EmailRecipientEnum.Moderators; }
        }

        /// <inheritdoc />
        public override string Description
        {
            get { return "A member proposed an event for review (event staff)."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "New event proposal: {eventTitle}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "{submitterName} proposed an event."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "New event proposal"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "{submitterName} proposed {eventTitle} on {startLocal}. Review it in the event proposals queue."; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "Review"; }
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
            { StaffEmailDetails.Token, StaffEmailSamples.EventProposal },
            { "eventTitle", "Community picnic" },
            { "submitterName", "Alex Chen" },
            { "startLocal", "Sunday, June 7 at 11:00 AM" },
            { "adminLink", "https://example.test/admin/events/proposals" }
        };

        #endregion
    }
}
