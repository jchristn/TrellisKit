namespace TrellisKit.Core.Database
{
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Enumeration collections of the media slice. Column expressions use the alias m for mediaattachments.
    /// </summary>
    public static class MediaCollections
    {
        #region Public-Members

        /// <summary>
        /// Admin media library (POST /api/v1/admin/media/enumerate and GET /api/v1/admin/media). Orderings
        /// CreatedDescending (default), CreatedAscending, SizeDescending, SizeAscending; search over the original file
        /// name and alt text; filters kind, status, ownerUserId, attached; labels and tags of target type Media.
        /// </summary>
        public static readonly EnumerationCollection AdminLibrary = new EnumerationCollection("admin-media", TargetTypeEnum.Media, "m.id", "m.createdutc", true)
            .Order("SizeDescending", "m.sizebytes", true, EnumerationKeyKindEnum.Number)
            .Order("SizeAscending", "m.sizebytes", false, EnumerationKeyKindEnum.Number)
            .Search("m.originalfilename", "m.alttext")
            .Filters("kind", "status", "ownerUserId", "attached")
            .DefaultTo("CreatedDescending");

        #endregion
    }
}
