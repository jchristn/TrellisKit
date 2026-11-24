namespace TrellisKit.Core.Database
{
    using System;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Enumeration collections of administrative email (docs/TRELLISKIT.md section 12.4). Table aliases: emailmessages m,
    /// emailsuppressions s.
    /// </summary>
    public static class EmailCollections
    {
        #region Public-Members

        /// <summary>
        /// POST /api/v1/admin/email/messages/enumerate: search over recipient and subject; filters status, template,
        /// category, recipient, userId.
        /// </summary>
        public static readonly EnumerationCollection Messages = new EnumerationCollection("admin-email-messages", TargetTypeEnum.EmailMessage, "m.id", "m.createdutc", true)
            .Order("UpdatedDescending", "m.updatedutc", true, EnumerationKeyKindEnum.Time)
            .Search("m.recipient", "m.subject")
            .Filters("status", "template", "category", "recipient", "userId");

        /// <summary>
        /// POST /api/v1/admin/email/suppressions/enumerate: search over email; filter reason.
        /// </summary>
        public static readonly EnumerationCollection Suppressions = new EnumerationCollection("admin-email-suppressions", TargetTypeEnum.EmailSuppression, "s.id", "s.createdutc", true)
            .Order("EmailAscending", "s.emailnormalized", false, EnumerationKeyKindEnum.Text)
            .Search("s.email", "s.note")
            .Filters("reason");

        #endregion
    }
}
