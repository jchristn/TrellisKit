namespace TrellisKit.Core.PropertyConfig
{
    using System;

    /// <summary>
    /// Website build inputs. Not served by the API.
    /// </summary>
    public class PropertyWeb
    {
        #region Public-Members

        /// <summary>
        /// Theme stylesheet path.
        /// </summary>
        public string Theme { get; set; } = String.Empty;

        /// <summary>
        /// Marketing content directory.
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// Legal documents directory.
        /// </summary>
        public string Legal { get; set; } = String.Empty;

        /// <summary>
        /// Whether the website shows app store badges.
        /// </summary>
        public bool StoreBadges { get; set; } = false;

        /// <summary>
        /// Optional member navigation order and labels for the website. Null keeps the engine order and labels.
        /// </summary>
        public PropertyWebNavigation? Navigation { get; set; } = null;

        #endregion
    }
}
