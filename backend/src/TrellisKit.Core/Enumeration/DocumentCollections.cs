namespace TrellisKit.Core.Enumeration
{
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The property document enumeration collections (docs/TRELLISKIT.md section 14).
    /// </summary>
    public static class DocumentCollections
    {
        #region Public-Members

        /// <summary>
        /// Orderings of document versions.
        /// </summary>
        public const string VersionOrderings = "SequenceDescending, CreatedDescending, CreatedAscending";

        /// <summary>
        /// Orderings of document acceptances.
        /// </summary>
        public const string AcceptanceOrderings = "AcceptedDescending, AcceptedAscending, CreatedDescending, CreatedAscending";

        /// <summary>
        /// Filters of the admin acceptance enumeration.
        /// </summary>
        public const string AcceptanceFilters = "documentKey, userId, version, method, client";

        /// <summary>
        /// Versions of one document (staff).
        /// </summary>
        public static readonly EnumerationCollection Versions = new EnumerationCollection("admin-document-versions", TargetTypeEnum.PropertyDocumentVersion, "v.id", "v.createdutc", true)
            .Order("SequenceDescending", "v.sequence", true, EnumerationKeyKindEnum.Number)
            .Search("v.version", "v.title", "v.changesummary")
            .DefaultTo("SequenceDescending");

        /// <summary>
        /// Acceptances across documents (staff). Search covers the member's display name and email.
        /// </summary>
        public static readonly EnumerationCollection Acceptances = new EnumerationCollection("admin-document-acceptances", TargetTypeEnum.DocumentAcceptance, "a.id", "a.createdutc", true)
            .Order("AcceptedDescending", "a.acceptedutc", true, EnumerationKeyKindEnum.Time)
            .Order("AcceptedAscending", "a.acceptedutc", false, EnumerationKeyKindEnum.Time)
            .Search("u.displayname", "u.email", "a.documentkey")
            .Filters("documentKey", "userId", "version", "method", "client")
            .DefaultTo("AcceptedDescending");

        /// <summary>
        /// The caller's own acceptances.
        /// </summary>
        public static readonly EnumerationCollection MyAcceptances = new EnumerationCollection("my-document-acceptances", TargetTypeEnum.DocumentAcceptance, "a.id", "a.createdutc", false)
            .Order("AcceptedDescending", "a.acceptedutc", true, EnumerationKeyKindEnum.Time)
            .Order("AcceptedAscending", "a.acceptedutc", false, EnumerationKeyKindEnum.Time)
            .Search("a.documentkey", "a.title")
            .Filters("documentKey")
            .DefaultTo("AcceptedDescending");

        #endregion
    }
}
