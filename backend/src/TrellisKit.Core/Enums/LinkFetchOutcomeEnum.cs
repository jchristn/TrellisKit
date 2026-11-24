namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Outcome of one link preview fetch (a page or an image), including every redirect hop.
    /// </summary>
    public enum LinkFetchOutcomeEnum
    {
        /// <summary>
        /// The body was read within the limits.
        /// </summary>
        Fetched,

        /// <summary>
        /// The host resolved to (or is) an address that is not on the public internet.
        /// </summary>
        BlockedAddress,

        /// <summary>
        /// The host is under a suffix in linkPreviews.blockedDomains.
        /// </summary>
        BlockedDomain,

        /// <summary>
        /// The URL itself is not allowed: not http or https, a port other than 80 or 443, or credentials in the URL.
        /// </summary>
        BlockedUrl,

        /// <summary>
        /// The connect or total timeout (linkPreviews.timeoutSeconds) elapsed.
        /// </summary>
        Timeout,

        /// <summary>
        /// The body is larger than the limit.
        /// </summary>
        TooLarge,

        /// <summary>
        /// A page response that is not text/html.
        /// </summary>
        NotHtml,

        /// <summary>
        /// An image response that is not JPEG, PNG, or WebP by its magic bytes.
        /// </summary>
        UnsupportedImage,

        /// <summary>
        /// More redirects than linkPreviews.maxRedirects, or a redirect without a usable Location.
        /// </summary>
        TooManyRedirects,

        /// <summary>
        /// A non-success HTTP status.
        /// </summary>
        HttpError,

        /// <summary>
        /// DNS failure, connection failure, or another transport error.
        /// </summary>
        Error
    }
}
