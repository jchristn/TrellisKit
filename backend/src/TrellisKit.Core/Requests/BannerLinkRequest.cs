namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A banner link in a request.
    /// </summary>
    public class BannerLinkRequest
    {
        /// <summary>
        /// Label.
        /// </summary>
        public string? Label { get; set; } = null;

        /// <summary>
        /// App route.
        /// </summary>
        public string? Path { get; set; } = null;

        /// <summary>
        /// HTTPS URL.
        /// </summary>
        public string? Url { get; set; } = null;
    }
}
