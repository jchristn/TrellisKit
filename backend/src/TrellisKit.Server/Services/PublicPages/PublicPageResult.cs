namespace TrellisKit.Server.Services.PublicPages
{
    using System;

    /// <summary>
    /// The response to a public page, sitemap, or robots.txt request.
    /// </summary>
    public class PublicPageResult
    {
        #region Public-Members

        /// <summary>
        /// Header that asks the website proxy (nginx) to serve its own file instead (an internal redirect).
        /// </summary>
        public const string AccelRedirectHeader = "X-Accel-Redirect";

        /// <summary>
        /// HTTP status. Default 200.
        /// </summary>
        public int StatusCode { get; set; } = 200;

        /// <summary>
        /// Content type.
        /// </summary>
        public string ContentType { get; set; } = "text/html; charset=utf-8";

        /// <summary>
        /// Body.
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// Location of a redirect, or null.
        /// </summary>
        public string? Location { get; set; } = null;

        /// <summary>
        /// Cache-Control value.
        /// </summary>
        public string CacheControl { get; set; } = "public, max-age=300";

        /// <summary>
        /// The website file the proxy should serve instead (X-Accel-Redirect), or null.
        /// </summary>
        public string? AccelRedirect { get; set; } = null;

        #endregion
    }
}
