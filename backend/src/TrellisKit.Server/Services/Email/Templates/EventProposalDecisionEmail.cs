namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// event_proposal_decision (docs/PHASE4_CONTRACT.md section 9): Your proposed event was approved or declined by event staff.
    /// </summary>
    public sealed class EventProposalDecisionEmail : EmailTemplate
    {
        #region Public-Members

        /// <summary>
        /// Template key.
        /// </summary>
        public const string TemplateKey = "event_proposal_decision";

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
            get { return "Your proposed event was approved or declined by event staff."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "Your proposed event {eventTitle} was {decision}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "Event staff reviewed your proposal."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "Your proposal was {decision}"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "Hi {memberName}, event staff reviewed your proposed event {eventTitle}: it was {decision}.\n\n{reviewNote}"; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "Open"; }
        }

        /// <inheritdoc />
        public override string ButtonToken
        {
            get { return "link"; }
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
            { "eventTitle", "Community picnic" },
            { "decision", "approved" },
            { "reviewNote", "Thanks for organizing!" },
            { "link", "https://example.test/app/events/evt_1" }
        };

        #endregion
    }
}
