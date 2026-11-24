namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A banner link: an app route or an https URL.
    /// </summary>
    public class BannerLinkView
    {
        /// <summary>
        /// Label.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// App route, or null.
        /// </summary>
        public string? Path { get; set; } = null;

        /// <summary>
        /// HTTPS URL, or null.
        /// </summary>
        public string? Url { get; set; } = null;
    }
}
