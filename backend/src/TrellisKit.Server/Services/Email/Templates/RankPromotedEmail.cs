namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// rank_promoted (docs/TRELLISKIT.md, Ranks, curricula, and attendance): a rank awarder verified a promotion of the member.
    /// </summary>
    public sealed class RankPromotedEmail : EmailTemplate
    {
        #region Public-Members

        /// <summary>
        /// Template key.
        /// </summary>
        public const string TemplateKey = "rank_promoted";

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
            get { return "A rank awarder verified your promotion."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "You were promoted to {rank}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "Congratulations from {awarderName}."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "You were promoted to {rank}"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "Hi {memberName}, {awarderName} promoted you to {rank}. Your profile now shows it as verified.\n\n{note}"; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "Open your profile"; }
        }

        /// <inheritdoc />
        public override string ButtonToken
        {
            get { return "profileLink"; }
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
            { "rank", "Intermediate, 2 Stars" },
            { "awarderName", "Alex Chen" },
            { "note", "Great progress this season." },
            { "profileLink", "https://example.test/app/profile" }
        };

        #endregion
    }
}
