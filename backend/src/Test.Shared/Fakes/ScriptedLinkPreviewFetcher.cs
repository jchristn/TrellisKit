namespace Test.Shared.Fakes
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Link preview fetcher returning scripted results by absolute URL; unknown URLs fail with Error. Records calls.
    /// </summary>
    public sealed class ScriptedLinkPreviewFetcher : ILinkPreviewFetcher
    {
        private readonly ConcurrentDictionary<string, LinkFetchResult> _Pages = new ConcurrentDictionary<string, LinkFetchResult>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, LinkFetchResult> _Images = new ConcurrentDictionary<string, LinkFetchResult>(StringComparer.Ordinal);
        private readonly ConcurrentQueue<string> _Calls = new ConcurrentQueue<string>();

        /// <summary>
        /// Requested URLs in order, prefixed "page " or "image ".
        /// </summary>
        public List<string> Calls
        {
            get { return new List<string>(_Calls); }
        }

        /// <summary>
        /// Script an HTML page.
        /// </summary>
        /// <param name="url">Absolute URL.</param>
        /// <param name="html">HTML.</param>
        /// <param name="finalUrl">Final URL after redirects, or null for the URL.</param>
        public void Page(string url, string html, string? finalUrl = null)
        {
            _Pages[url] = new LinkFetchResult
            {
                Outcome = LinkFetchOutcomeEnum.Fetched,
                FinalUrl = new Uri(finalUrl ?? url),
                StatusCode = 200,
                ContentType = "text/html; charset=utf-8",
                Body = Encoding.UTF8.GetBytes(html)
            };
        }

        /// <summary>
        /// Script a failed page fetch.
        /// </summary>
        /// <param name="url">Absolute URL.</param>
        /// <param name="outcome">Outcome.</param>
        public void PageFailure(string url, LinkFetchOutcomeEnum outcome)
        {
            _Pages[url] = LinkFetchResult.Fail(outcome, new Uri(url), "scripted");
        }

        /// <summary>
        /// Script an image.
        /// </summary>
        /// <param name="url">Absolute URL.</param>
        /// <param name="bytes">Image bytes.</param>
        public void Image(string url, byte[] bytes)
        {
            _Images[url] = new LinkFetchResult { Outcome = LinkFetchOutcomeEnum.Fetched, FinalUrl = new Uri(url), StatusCode = 200, ContentType = "image/png", Body = bytes };
        }

        /// <inheritdoc />
        public Task<LinkFetchResult> FetchPageAsync(Uri url, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(url);
            _Calls.Enqueue("page " + url.AbsoluteUri);
            return Task.FromResult(_Pages.TryGetValue(url.AbsoluteUri, out LinkFetchResult? result) ? result : LinkFetchResult.Fail(LinkFetchOutcomeEnum.Error, url, "unscripted"));
        }

        /// <inheritdoc />
        public Task<LinkFetchResult> FetchImageAsync(Uri url, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(url);
            _Calls.Enqueue("image " + url.AbsoluteUri);
            return Task.FromResult(_Images.TryGetValue(url.AbsoluteUri, out LinkFetchResult? result) ? result : LinkFetchResult.Fail(LinkFetchOutcomeEnum.Error, url, "unscripted"));
        }
    }
}
