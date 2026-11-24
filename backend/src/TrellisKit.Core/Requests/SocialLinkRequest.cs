namespace TrellisKit.Core.Requests
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A social link in a request.
    /// </summary>
    public class SocialLinkRequest
    {
        #region Public-Members

        /// <summary>
        /// Kind.
        /// </summary>
        public SocialLinkKindEnum Kind { get; set; } = SocialLinkKindEnum.Other;

        /// <summary>
        /// Absolute https URL.
        /// </summary>
        public string? Url { get; set; } = null;

        #endregion
    }
}
