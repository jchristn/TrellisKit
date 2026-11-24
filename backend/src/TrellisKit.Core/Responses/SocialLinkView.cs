namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A social link (docs/PHASE4_CONTRACT.md section 2.2).
    /// </summary>
    public class SocialLinkView
    {
        #region Public-Members

        /// <summary>
        /// Kind.
        /// </summary>
        public SocialLinkKindEnum Kind { get; set; } = SocialLinkKindEnum.Other;

        /// <summary>
        /// Absolute https URL.
        /// </summary>
        public string Url { get; set; } = String.Empty;

        #endregion
    }
}
