namespace TrellisKit.Core.Enumeration
{
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Brand palette version collections (docs/TRELLISKIT.md section 2.1). Versions are bounded by
    /// property.maxPaletteVersions (at most 200 retained) and carry no labels or tags of their own (the collection has no
    /// target type), so the enumeration normalizer refuses label and tag filters with 400 ValidationFailed.
    /// </summary>
    public static class BrandingCollections
    {
        #region Public-Members

        /// <summary>
        /// GET /api/v1/admin/property/branding/versions and the platform equivalent: newest first by default.
        /// </summary>
        public static readonly EnumerationCollection Versions = new EnumerationCollection("branding-versions", null, "p.vid", "p.createdutc", true)
            .Order("VersionDescending", "p.version", true, EnumerationKeyKindEnum.Number)
            .Order("VersionAscending", "p.version", false, EnumerationKeyKindEnum.Number)
            .Search("p.note", "p.source")
            .Filters("source")
            .DefaultTo("VersionDescending");

        #endregion
    }
}
