namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Integrations;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Link previews (contract section 5.9): a cache keyed by the normalized URL hash, fetched through the
    /// SSRF-protected <see cref="TrellisKit.Core.Integrations.Interfaces.ILinkPreviewFetcher"/>, parsed, word
    /// filtered, with the og:image fetched under the same rules and stored through <see cref="ImageStore"/>.
    /// Successful rows are reused for linkPreviews.cacheHours and failures for linkPreviews.failureCacheMinutes. A
    /// refresh never modifies a row content may reference: it inserts a new row and renames the old row's urlhash
    /// to "old:" plus its id (Failed rows are deleted instead). Outbound fetches are limited to
    /// linkPreviews.perHostPerMinute per host across all members.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class LinkPreviewService
    {
        #region Public-Members

        /// <summary>
        /// Per-member rate limit bucket for POST /api/v1/link-previews.
        /// </summary>
        public const string RateBucket = "link-previews";

        /// <summary>
        /// Per-host outbound fetch bucket (shared by every member).
        /// </summary>
        public const string HostBucket = "link-preview-host";

        /// <summary>
        /// Stores preview images and builds their URLs. Set by the composition root (the media pipeline provides the
        /// real implementation; tests set a fake). Never null.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public ILinkPreviewImageStore ImageStore
        {
            get { return _ImageStore; }
            set { _ImageStore = value ?? throw new ArgumentNullException(nameof(ImageStore)); }
        }

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly AppSettings _Settings;
        private readonly IntegrationSet _Integrations;
        private readonly RateLimitService _Limits;
        private readonly WordFilterService _WordFilter;
        private readonly AppLogger _Logger;
        private ILinkPreviewImageStore _ImageStore;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Running settings (read live).</param>
        /// <param name="integrations">Integrations (LinkPreviewFetcher).</param>
        /// <param name="limits">Rate limits.</param>
        /// <param name="wordFilter">Word filter.</param>
        /// <param name="imageStore">Preview image store.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public LinkPreviewService(DatabaseDriverBase database, AppSettings settings, IntegrationSet integrations, RateLimitService limits, WordFilterService wordFilter,
            ILinkPreviewImageStore imageStore, AppLogger logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Integrations = integrations ?? throw new ArgumentNullException(nameof(integrations));
            _Limits = limits ?? throw new ArgumentNullException(nameof(limits));
            _WordFilter = wordFilter ?? throw new ArgumentNullException(nameof(wordFilter));
            _ImageStore = imageStore ?? throw new ArgumentNullException(nameof(imageStore));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Normalize a URL for the cache key: absolute http or https, lowercase scheme and host, default port removed,
        /// fragment removed (user info is kept so the fetcher refuses it).
        /// </summary>
        /// <param name="url">URL text, or null.</param>
        /// <returns>Normalized URL, or null when it is not an absolute http or https URL.</returns>
        public static string? NormalizeUrl(string? url)
        {
            if (String.IsNullOrWhiteSpace(url) || url.Length > 2048) return null;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri)) return null;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
            if (String.IsNullOrEmpty(uri.Host)) return null;
            return uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.UserInfo | UriComponents.PathAndQuery, UriFormat.UriEscaped);
        }

        /// <summary>
        /// SHA-256 (lowercase hex) of a normalized URL.
        /// </summary>
        /// <param name="normalizedUrl">Normalized URL.</param>
        /// <returns>Hash.</returns>
        /// <exception cref="ArgumentNullException">The URL is null.</exception>
        public static string HashUrl(string normalizedUrl)
        {
            ArgumentNullException.ThrowIfNull(normalizedUrl);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedUrl))).ToLowerInvariant();
        }

        /// <summary>
        /// POST /api/v1/link-previews: the preview for a URL, from the cache when fresh.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="url">URL.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Preview.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed (fieldErrors.url missing), 400 LinkPreviewUnavailable (disabled, not
        /// allowed, or could not be fetched), 429 RateLimited (member limit, or the per-host limit).</exception>
        public async Task<LinkPreviewView> PreviewAsync(ContentActor actor, string? url, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (String.IsNullOrWhiteSpace(url)) throw ApiErrorException.Field("url", "Provide an http or https URL.");
            if (!_Settings.LinkPreviews.Enabled) throw Unavailable();
            _Limits.EnforceMember(actor, RateBucket, _Settings.LinkPreviews.PerHour, RateLimitService.Hour);
            LinkPreview? preview = await GetOrFetchAsync(url, token).ConfigureAwait(false);
            if (preview == null || preview.Status != LinkPreviewStatusEnum.Ok) throw Unavailable();
            return ToView(preview);
        }

        /// <summary>
        /// Resolve the preview to store with a post or comment at write time: the first http or https URL of the body
        /// (<see cref="MarkdownNormalizer.FirstUrl"/>). Never throws for fetch failures, limits, or a disabled feature.
        /// </summary>
        /// <param name="body">Normalized body, or null.</param>
        /// <param name="showLinkPreview">The request's showLinkPreview flag.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Identifier of an Ok preview row to store in linkpreviewid, or null.</returns>
        /// <exception cref="OperationCanceledException">The caller's token was cancelled.</exception>
        public async Task<string?> ResolveForBodyAsync(string? body, bool showLinkPreview, CancellationToken token = default)
        {
            if (!showLinkPreview || !_Settings.LinkPreviews.Enabled) return null;
            string? url = MarkdownNormalizer.FirstUrl(body);
            if (url == null) return null;
            try
            {
                LinkPreview? preview = await GetOrFetchAsync(url, token).ConfigureAwait(false);
                return preview != null && preview.Status == LinkPreviewStatusEnum.Ok ? preview.Id : null;
            }
            catch (ApiErrorException)
            {
                return null;
            }
            catch (IntegrationNotConfiguredException)
            {
                return null;
            }
        }

        /// <summary>
        /// LinkPreview views for many stored preview ids (rows referenced by content), skipping unknown and Failed rows.
        /// Returns an empty map when link previews are disabled (contract: linkPreview is always null then).
        /// </summary>
        /// <param name="ids">Preview identifiers (nulls ignored; at most 500 distinct are read).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Views keyed by preview id.</returns>
        /// <exception cref="ArgumentNullException">The ids are null.</exception>
        public async Task<Dictionary<string, LinkPreviewView>> LoadViewsAsync(IEnumerable<string?> ids, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            Dictionary<string, LinkPreviewView> result = new Dictionary<string, LinkPreviewView>(StringComparer.Ordinal);
            if (!_Settings.LinkPreviews.Enabled) return result;
            List<string> list = ids.Where(i => !String.IsNullOrEmpty(i)).Select(i => i!).Distinct(StringComparer.Ordinal).ToList();
            if (list.Count == 0) return result;
            Dictionary<string, LinkPreview> rows = await _Database.LinkPreviews.ReadManyAsync(list, token).ConfigureAwait(false);
            foreach (LinkPreview row in rows.Values)
            {
                if (row.Status == LinkPreviewStatusEnum.Ok) result[row.Id] = ToView(row);
            }
            return result;
        }

        /// <summary>
        /// The cache row for a URL: fresh rows are returned as they are (cache_hit); otherwise the page is fetched and
        /// a new row stored (Ok or Failed).
        /// </summary>
        /// <param name="url">URL.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Row, or null when the URL is not an absolute http or https URL.</returns>
        /// <exception cref="ApiErrorException">429 RateLimited when the host's outbound limit is reached (nothing is cached).</exception>
        public async Task<LinkPreview?> GetOrFetchAsync(string url, CancellationToken token = default)
        {
            string? normalized = NormalizeUrl(url);
            if (normalized == null) return null;
            string hash = HashUrl(normalized);
            DateTime now = DateTime.UtcNow;
            LinkPreview? existing = await _Database.LinkPreviews.ReadByUrlHashAsync(hash, token).ConfigureAwait(false);
            if (existing != null && existing.ExpiresUtc > now)
            {
                Count("cache_hit");
                return existing;
            }
            Uri uri = new Uri(normalized);
            using (Activity? activity = TrellisKitTelemetry.StartSpan("link_preview.fetch"))
            {
                _Limits.Enforce(HostBucket + ":" + uri.IdnHost.ToLowerInvariant(), _Settings.LinkPreviews.PerHostPerMinute, RateLimitService.Minute);
                LinkFetchResult page = await _Integrations.LinkPreviewFetcher.FetchPageAsync(uri, token).ConfigureAwait(false);
                Count(OutcomeLabel(page.Outcome));
                activity?.SetTag("outcome", page.Outcome.ToString());
                LinkPreview row = page.Success
                    ? await BuildAsync(normalized, hash, page, token).ConfigureAwait(false)
                    : Failed(normalized, hash, page.Outcome.ToString());
                return await _Database.LinkPreviews.ReplaceAsync(row, existing?.Id, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The LinkPreview JSON shape of a row.
        /// </summary>
        /// <param name="preview">Row.</param>
        /// <returns>View.</returns>
        /// <exception cref="ArgumentNullException">The row is null.</exception>
        public LinkPreviewView ToView(LinkPreview preview)
        {
            ArgumentNullException.ThrowIfNull(preview);
            return new LinkPreviewView
            {
                Url = preview.Url,
                CanonicalUrl = preview.CanonicalUrl,
                SiteName = preview.SiteName,
                Title = preview.Title,
                Description = preview.Description,
                ImageUrl = String.IsNullOrEmpty(preview.ImageKey) ? null : _ImageStore.LinkPreviewImageUrl(preview.ImageKey),
                ImageWidth = String.IsNullOrEmpty(preview.ImageKey) ? null : preview.ImageWidth,
                ImageHeight = String.IsNullOrEmpty(preview.ImageKey) ? null : preview.ImageHeight,
                FetchedUtc = preview.FetchedUtc
            };
        }

        #endregion

        #region Private-Methods

        private async Task<LinkPreview> BuildAsync(string normalized, string hash, LinkFetchResult page, CancellationToken token)
        {
            Uri final = page.FinalUrl ?? new Uri(normalized);
            LinkPreviewMetadata metadata = LinkPreviewHtmlParser.Parse(LinkPreviewHtmlParser.Decode(page.Body, page.ContentType));
            if (metadata.Title == null && metadata.Description == null) return Failed(normalized, hash, "NoMetadata");
            if (await _WordFilter.IsBlockedAsync(metadata.Title, token).ConfigureAwait(false) ||
                await _WordFilter.IsBlockedAsync(metadata.Description, token).ConfigureAwait(false))
            {
                return Failed(normalized, hash, "Filtered");
            }
            DateTime now = DateTime.UtcNow;
            LinkPreview row = new LinkPreview
            {
                Id = IdGenerator.Generate(Constants.LinkPreviewIdPrefix),
                UrlHash = hash,
                Url = normalized,
                CanonicalUrl = Absolute(final, metadata.CanonicalUrl) ?? final.AbsoluteUri,
                SiteName = metadata.SiteName,
                Title = metadata.Title,
                Description = metadata.Description,
                Status = LinkPreviewStatusEnum.Ok,
                FetchedUtc = now,
                ExpiresUtc = now.AddHours(_Settings.LinkPreviews.CacheHours)
            };
            string? image = Absolute(final, metadata.ImageUrl);
            if (image != null)
            {
                StoredPreviewImage? stored = await FetchImageAsync(new Uri(image), hash, token).ConfigureAwait(false);
                if (stored != null && !String.IsNullOrEmpty(stored.Key))
                {
                    row.ImageKey = stored.Key;
                    row.ImageWidth = stored.Width;
                    row.ImageHeight = stored.Height;
                }
            }
            return row;
        }

        private async Task<StoredPreviewImage?> FetchImageAsync(Uri image, string hash, CancellationToken token)
        {
            try
            {
                _Limits.Enforce(HostBucket + ":" + image.IdnHost.ToLowerInvariant(), _Settings.LinkPreviews.PerHostPerMinute, RateLimitService.Minute);
            }
            catch (ApiErrorException)
            {
                return null;
            }
            LinkFetchResult result = await _Integrations.LinkPreviewFetcher.FetchImageAsync(image, token).ConfigureAwait(false);
            if (!result.Success) return null;
            try
            {
                return await _ImageStore.StoreLinkPreviewImageAsync(hash, result.Body, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Logger.Warn("[LinkPreviewService] could not store a preview image: " + e.GetType().Name);
                return null;
            }
        }

        private LinkPreview Failed(string normalized, string hash, string reason)
        {
            DateTime now = DateTime.UtcNow;
            return new LinkPreview
            {
                Id = IdGenerator.Generate(Constants.LinkPreviewIdPrefix),
                UrlHash = hash,
                Url = normalized,
                Status = LinkPreviewStatusEnum.Failed,
                FailureReason = reason,
                FetchedUtc = now,
                ExpiresUtc = now.AddMinutes(_Settings.LinkPreviews.FailureCacheMinutes)
            };
        }

        private static string? Absolute(Uri baseUri, string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            if (!Uri.TryCreate(baseUri, value.Trim(), out Uri? resolved)) return null;
            if (resolved.Scheme != Uri.UriSchemeHttp && resolved.Scheme != Uri.UriSchemeHttps) return null;
            return resolved.AbsoluteUri;
        }

        private static void Count(string outcome)
        {
            TrellisKitTelemetry.Add(TrellisKitTelemetry.LinkPreviews, 1, "outcome", outcome);
        }

        private static string OutcomeLabel(LinkFetchOutcomeEnum outcome)
        {
            switch (outcome)
            {
                case LinkFetchOutcomeEnum.Fetched: return "fetched";
                case LinkFetchOutcomeEnum.BlockedAddress:
                case LinkFetchOutcomeEnum.BlockedUrl: return "blocked_address";
                case LinkFetchOutcomeEnum.BlockedDomain: return "blocked_domain";
                case LinkFetchOutcomeEnum.Timeout: return "timeout";
                case LinkFetchOutcomeEnum.TooLarge: return "too_large";
                case LinkFetchOutcomeEnum.NotHtml:
                case LinkFetchOutcomeEnum.UnsupportedImage: return "not_html";
                default: return "error";
            }
        }

        private static ApiErrorException Unavailable()
        {
            return new ApiErrorException(400, "LinkPreviewUnavailable", "A preview is not available for this link.");
        }

        #endregion
    }
}
