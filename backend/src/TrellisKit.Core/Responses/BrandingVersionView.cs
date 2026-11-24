namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Models;

    /// <summary>
    /// One retained brand palette version (GET /api/v1/admin/property/branding/versions). Versions carry no labels or
    /// tags of their own; the lists are always empty.
    /// </summary>
    public class BrandingVersionView
    {
        #region Public-Members

        /// <summary>
        /// Version number.
        /// </summary>
        public int Version { get; set; } = 1;

        /// <summary>
        /// Source: Seed, Edit, RevertPrevious, RevertSeed, RevertVersion, or DirectorySync. Never null.
        /// </summary>
        public string Source
        {
            get { return _Source; }
            set { _Source = value ?? String.Empty; }
        }

        /// <summary>
        /// Note stored with the version, or null.
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// Light scheme colors. Never null.
        /// </summary>
        public BrandColorSet Light
        {
            get { return _Light; }
            set { _Light = value ?? new BrandColorSet(); }
        }

        /// <summary>
        /// Dark scheme colors. Never null.
        /// </summary>
        public BrandColorSet Dark
        {
            get { return _Dark; }
            set { _Dark = value ?? new BrandColorSet(); }
        }

        /// <summary>
        /// Contrast pairs of this version that fail WCAG 2.1 AA (with or without an override).
        /// </summary>
        public int FailingPairs { get; set; } = 0;

        /// <summary>
        /// Contrast overrides stored with this version.
        /// </summary>
        public int OverrideCount { get; set; } = 0;

        /// <summary>
        /// True for the current (newest) version, which cannot be restored.
        /// </summary>
        public bool IsCurrent { get; set; } = false;

        /// <summary>
        /// Member or platform administrator who created the version, or null (seed and folder sync).
        /// </summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Labels (always empty). Never null.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags (always empty). Never null.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion

        #region Private-Members

        private string _Source = String.Empty;
        private BrandColorSet _Light = new BrandColorSet();
        private BrandColorSet _Dark = new BrandColorSet();

        #endregion
    }
}
