namespace TrellisKit.Core.Integrations.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Fetches pages and images for link previews with SSRF protection (contract section 5.9): http and https on
    /// ports 80 and 443 only, no credentials, the host resolved once and every address checked, a connection pinned
    /// to the checked address, manual redirects re-checked on every hop, blocked domains, timeouts, type and size
    /// limits, no cookies, and a fixed User-Agent. Thread safety: implementations are safe to call concurrently.
    /// </summary>
    public interface ILinkPreviewFetcher
    {
        /// <summary>
        /// Fetch an HTML page (text/html only, at most linkPreviews.maxHtmlBytes).
        /// </summary>
        /// <param name="url">Absolute URL.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result; failures are reported in the outcome, never thrown.</returns>
        /// <exception cref="ArgumentNullException">The URL is null.</exception>
        /// <exception cref="OperationCanceledException">The caller's token was cancelled.</exception>
        Task<LinkFetchResult> FetchPageAsync(Uri url, CancellationToken token = default);

        /// <summary>
        /// Fetch an image (JPEG, PNG, or WebP by magic bytes, at most linkPreviews.maxImageBytes).
        /// </summary>
        /// <param name="url">Absolute URL.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result; failures are reported in the outcome, never thrown.</returns>
        /// <exception cref="ArgumentNullException">The URL is null.</exception>
        /// <exception cref="OperationCanceledException">The caller's token was cancelled.</exception>
        Task<LinkFetchResult> FetchImageAsync(Uri url, CancellationToken token = default);
    }
}
