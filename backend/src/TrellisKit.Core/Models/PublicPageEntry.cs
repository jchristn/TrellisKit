namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// One sitemap row of a public landing page: the object's current slug and when it last changed.
    /// </summary>
    public class PublicPageEntry
    {
        #region Public-Members

        /// <summary>
        /// Current slug.
        /// </summary>
        public string Slug { get; set; } = String.Empty;

        /// <summary>
        /// Last modification (UTC).
        /// </summary>
        public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
