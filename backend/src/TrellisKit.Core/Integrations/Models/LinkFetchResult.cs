namespace TrellisKit.Core.Integrations.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Result of a link preview fetch.
    /// </summary>
    public class LinkFetchResult
    {
        #region Public-Members

        /// <summary>
        /// Outcome.
        /// </summary>
        public LinkFetchOutcomeEnum Outcome { get; set; } = LinkFetchOutcomeEnum.Error;

        /// <summary>
        /// Whether the body was fetched.
        /// </summary>
        public bool Success
        {
            get { return Outcome == LinkFetchOutcomeEnum.Fetched; }
        }

        /// <summary>
        /// URL of the last hop (after redirects), or null when nothing was requested.
        /// </summary>
        public Uri? FinalUrl { get; set; } = null;

        /// <summary>
        /// HTTP status of the last response, or 0.
        /// </summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>
        /// Content-Type header of the last response, or null.
        /// </summary>
        public string? ContentType { get; set; } = null;

        /// <summary>
        /// Body bytes (empty unless fetched). Never null.
        /// </summary>
        public byte[] Body
        {
            get { return _Body; }
            set { _Body = value ?? Array.Empty<byte>(); }
        }

        /// <summary>
        /// Short diagnostic detail (never shown to members), or null.
        /// </summary>
        public string? Detail { get; set; } = null;

        #endregion

        #region Private-Members

        private byte[] _Body = Array.Empty<byte>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// A failed result.
        /// </summary>
        /// <param name="outcome">Outcome.</param>
        /// <param name="url">URL involved, or null.</param>
        /// <param name="detail">Detail, or null.</param>
        /// <returns>Result.</returns>
        public static LinkFetchResult Fail(LinkFetchOutcomeEnum outcome, Uri? url, string? detail)
        {
            return new LinkFetchResult { Outcome = outcome, FinalUrl = url, Detail = detail };
        }

        #endregion
    }
}
