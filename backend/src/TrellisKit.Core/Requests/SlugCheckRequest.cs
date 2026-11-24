namespace TrellisKit.Core.Requests
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Body of POST /api/v1/public-pages/slugs/check.
    /// </summary>
    public class SlugCheckRequest
    {
        #region Public-Members

        /// <summary>
        /// Kind of page. Default Profile.
        /// </summary>
        public PublicPageKindEnum Kind { get; set; } = PublicPageKindEnum.Profile;

        /// <summary>
        /// Slug to check.
        /// </summary>
        public string? Slug { get; set; } = null;

        /// <summary>
        /// The object being edited (venue or event identifier; for profiles the caller), whose own slugs count as available.
        /// </summary>
        public string? Id { get; set; } = null;

        #endregion
    }
}
