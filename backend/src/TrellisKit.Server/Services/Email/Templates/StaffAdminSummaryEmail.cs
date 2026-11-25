namespace TrellisKit.Server.Services.Email.Templates
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using TrellisKit.Core.Enums;
    using TrellisKit.Server.Services.AdminSummary;
    using TrellisKit.Server.Services.Email.Blocks;

    /// <summary>
    /// Daily administrator summary (docs/ADMIN_SUMMARY.md), sent to the deployment's adminSummary.recipients. The body
    /// paragraphs are editable; the report sections come from the report token (JSON written by AdminSummaryService), so a
    /// queued message renders the same summary on every attempt. Template key staff_admin_summary.
    /// </summary>
    public sealed class StaffAdminSummaryEmail : EmailTemplate
    {
        #region Public-Members

        /// <summary>
        /// Template key.
        /// </summary>
        public const string TemplateKey = "staff_admin_summary";

        /// <summary>
        /// Token that carries the report sections.
        /// </summary>
        public const string ReportToken = "report";

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
            get { return "Daily summary of members, activity, items needing attention, and platform health (adminSummary.recipients)."; }
        }

        /// <inheritdoc />
        public override string DefaultSubject
        {
            get { return "{name} daily summary for {date}"; }
        }

        /// <inheritdoc />
        public override string DefaultPreheader
        {
            get { return "{newMembers} new sign-ups, {activeMembers} active members, {totalMembers} members in total."; }
        }

        /// <inheritdoc />
        public override string DefaultHeading
        {
            get { return "Daily summary for {date}"; }
        }

        /// <inheritdoc />
        public override string DefaultBody
        {
            get { return "Here is what happened in {name} on {date} ({timeZone}). Each line compares the day with the day before and with the average of the seven days before it. Platform lines use UTC days."; }
        }

        /// <inheritdoc />
        public override string? ButtonLabel
        {
            get { return "Open metrics"; }
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
            { "date", "Monday, January 5, 2026" },
            { "timeZone", "America/Los_Angeles" },
            { "newMembers", "12" },
            { "activeMembers", "148" },
            { "totalMembers", "2,310" },
            { "link", "https://example.test/admin/metrics/growth" },
            { ReportToken, JsonSerializer.Serialize(new List<AdminSummaryEmailSection>
                {
                    new AdminSummaryEmailSection { Title = "Members", Lines = new List<EmailField>
                    {
                        new EmailField("Total members", "2,310 (day before 2,298, +1%; 7-day average 2,280)"),
                        new EmailField("New sign-ups", "12 (day before 9, +33%; 7-day average 10.4)"),
                        new EmailField("Daily active members", "148 (day before 139, +6%; 7-day average 142)")
                    } },
                    new AdminSummaryEmailSection { Title = "Activity", Lines = new List<EmailField>
                    {
                        new EmailField("Activity", "420 (day before 388, +8%; 7-day average 401)"),
                        new EmailField("Activity: reactions", "260 (day before 240, +8%; 7-day average 251)"),
                        new EmailField("Activity: comments", "96 (day before 90, +7%; 7-day average 88)")
                    } }
                }) }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Body paragraphs, one titled table per report section, the button, then a notice explaining why the recipient
        /// gets the summary.
        /// </summary>
        /// <param name="email">Composer.</param>
        public override void Compose(EmailComposer email)
        {
            ArgumentNullException.ThrowIfNull(email);
            email.BodyParagraphs();
            List<AdminSummaryEmailSection> sections = Sections(email.Token(ReportToken));
            if (sections.Count == 0) email.Paragraph("No metrics were available for this day.");
            foreach (AdminSummaryEmailSection section in sections)
            {
                // Report text is data, not template text: add blocks directly so braces are never treated as tokens.
                email.Add(new EmailSubheadingBlock(section.Title));
                email.Add(new EmailFieldsBlock(section.Lines.Where(l => l != null)));
            }
            email.DefaultButton();
            email.Notice("You receive this summary because your address is listed in the server's adminSummary.recipients setting.");
        }

        #endregion

        #region Private-Methods

        private static List<AdminSummaryEmailSection> Sections(string json)
        {
            if (String.IsNullOrWhiteSpace(json)) return new List<AdminSummaryEmailSection>();
            try
            {
                return JsonSerializer.Deserialize<List<AdminSummaryEmailSection>>(json) ?? new List<AdminSummaryEmailSection>();
            }
            catch (JsonException)
            {
                return new List<AdminSummaryEmailSection>();
            }
        }

        #endregion
    }
}
