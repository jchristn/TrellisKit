namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A social link of a venue or instructor (docs/PHASE4_CONTRACT.md section 2.2).
    /// </summary>
    public class DirectorySocialLink
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Kind.
        /// </summary>
        public SocialLinkKindEnum Kind { get; set; } = SocialLinkKindEnum.Other;

        /// <summary>
        /// Absolute https URL.
        /// </summary>
        public string Url { get; set; } = String.Empty;

        /// <summary>
        /// Display position.
        /// </summary>
        public int Position { get; set; } = 0;

        #endregion
    }
}
