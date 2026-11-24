namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Remote string, banner, remote asset, and string catalog data access (docs/LIVE_UPDATES_CONTRACT.md sections 14 and
    /// 16.3). Every method is bound to one property; implementations are safe to call concurrently.
    /// </summary>
    public interface IAppContentMethods
    {
        /// <summary>
        /// Sync catalog.
        /// </summary>
        /// <param name="sha256">Sha256.</param>
        /// <param name="entries">Entries.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> SyncCatalogAsync(string sha256, List<StringCatalogEntry> entries, CancellationToken token = default);

        /// <summary>
        /// Read catalog entry.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<StringCatalogEntry?> ReadCatalogEntryAsync(string key, CancellationToken token = default);

        /// <summary>
        /// Enumerate catalog.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="filter">Filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<EnumerationResult<StringCatalogEntry>> EnumerateCatalogAsync(EnumerationRequest request, RemoteStringFilter filter, CancellationToken token = default);

        /// <summary>
        /// Read string draft.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<RemoteStringOverride?> ReadStringDraftAsync(string key, CancellationToken token = default);

        /// <summary>
        /// Read string current.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<RemoteStringOverride?> ReadStringCurrentAsync(string key, CancellationToken token = default);

        /// <summary>
        /// List string heads.
        /// </summary>
        /// <param name="keys">Keys.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<RemoteStringOverride>> ListStringHeadsAsync(IEnumerable<string> keys, CancellationToken token = default);

        /// <summary>
        /// Save string draft.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="token">Cancellation token.</param>
        Task SaveStringDraftAsync(RemoteStringOverride value, CancellationToken token = default);

        /// <summary>
        /// Delete string draft.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> DeleteStringDraftAsync(string key, CancellationToken token = default);

        /// <summary>
        /// List current strings.
        /// </summary>
        /// <param name="limit">Limit.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<RemoteStringOverride>> ListCurrentStringsAsync(int limit, CancellationToken token = default);

        /// <summary>
        /// List strings at version.
        /// </summary>
        /// <param name="version">Version.</param>
        /// <param name="limit">Limit.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<RemoteStringOverride>> ListStringsAtVersionAsync(long version, int limit, CancellationToken token = default);

        /// <summary>
        /// List string drafts.
        /// </summary>
        /// <param name="limit">Limit.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<RemoteStringOverride>> ListStringDraftsAsync(int limit, CancellationToken token = default);

        /// <summary>
        /// List strings by keys.
        /// </summary>
        /// <param name="keys">Keys.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<RemoteStringOverride>> ListStringsByKeysAsync(IEnumerable<string> keys, CancellationToken token = default);

        /// <summary>
        /// Count string overrides.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<long> CountStringOverridesAsync(CancellationToken token = default);

        /// <summary>
        /// Enumerate string history.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="key">Key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<EnumerationResult<RemoteStringOverride>> EnumerateStringHistoryAsync(EnumerationRequest request, string key, CancellationToken token = default);

        /// <summary>
        /// Enumerate published strings.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="client">Client.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<EnumerationResult<RemoteStringOverride>> EnumeratePublishedStringsAsync(EnumerationRequest request, string client, CancellationToken token = default);

        /// <summary>
        /// Read banner draft.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<AppBanner?> ReadBannerDraftAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read banner current.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<AppBanner?> ReadBannerCurrentAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Save banner draft.
        /// </summary>
        /// <param name="banner">Banner.</param>
        /// <param name="token">Cancellation token.</param>
        Task SaveBannerDraftAsync(AppBanner banner, CancellationToken token = default);

        /// <summary>
        /// Delete banner draft.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="removeMetadata">Remove metadata.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> DeleteBannerDraftAsync(string id, bool removeMetadata, CancellationToken token = default);

        /// <summary>
        /// Has published banner.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> HasPublishedBannerAsync(string id, CancellationToken token = default);

        /// <summary>
        /// List current banners.
        /// </summary>
        /// <param name="limit">Limit.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<AppBanner>> ListCurrentBannersAsync(int limit, CancellationToken token = default);

        /// <summary>
        /// List banners at version.
        /// </summary>
        /// <param name="version">Version.</param>
        /// <param name="limit">Limit.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<AppBanner>> ListBannersAtVersionAsync(long version, int limit, CancellationToken token = default);

        /// <summary>
        /// List banner drafts.
        /// </summary>
        /// <param name="limit">Limit.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<AppBanner>> ListBannerDraftsAsync(int limit, CancellationToken token = default);

        /// <summary>
        /// Count banners.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<long> CountBannersAsync(CancellationToken token = default);

        /// <summary>
        /// Enumerate banners.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="filter">Filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<EnumerationResult<AppBanner>> EnumerateBannersAsync(EnumerationRequest request, BannerFilter filter, CancellationToken token = default);

        /// <summary>
        /// Enumerate banner history.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="id">Id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<EnumerationResult<AppBanner>> EnumerateBannerHistoryAsync(EnumerationRequest request, string id, CancellationToken token = default);

        /// <summary>
        /// Read asset draft.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<RemoteAsset?> ReadAssetDraftAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read asset current.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<RemoteAsset?> ReadAssetCurrentAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Asset key in use.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="excludeId">Exclude id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> AssetKeyInUseAsync(string key, string? excludeId, CancellationToken token = default);

        /// <summary>
        /// Save asset draft.
        /// </summary>
        /// <param name="asset">Asset.</param>
        /// <param name="token">Cancellation token.</param>
        Task SaveAssetDraftAsync(RemoteAsset asset, CancellationToken token = default);

        /// <summary>
        /// Delete asset draft.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="removeMetadata">Remove metadata.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> DeleteAssetDraftAsync(string id, bool removeMetadata, CancellationToken token = default);

        /// <summary>
        /// Has published asset.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> HasPublishedAssetAsync(string id, CancellationToken token = default);

        /// <summary>
        /// List current assets.
        /// </summary>
        /// <param name="limit">Limit.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<RemoteAsset>> ListCurrentAssetsAsync(int limit, CancellationToken token = default);

        /// <summary>
        /// List assets at version.
        /// </summary>
        /// <param name="version">Version.</param>
        /// <param name="limit">Limit.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<RemoteAsset>> ListAssetsAtVersionAsync(long version, int limit, CancellationToken token = default);

        /// <summary>
        /// List asset drafts.
        /// </summary>
        /// <param name="limit">Limit.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<RemoteAsset>> ListAssetDraftsAsync(int limit, CancellationToken token = default);

        /// <summary>
        /// Count assets.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<long> CountAssetsAsync(CancellationToken token = default);

        /// <summary>
        /// Is content hash referenced.
        /// </summary>
        /// <param name="sha256Hex">Sha256 hex.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> IsContentHashReferencedAsync(string sha256Hex, CancellationToken token = default);

        /// <summary>
        /// Enumerate assets.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="filter">Filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<EnumerationResult<RemoteAsset>> EnumerateAssetsAsync(EnumerationRequest request, RemoteAssetFilter filter, CancellationToken token = default);

        /// <summary>
        /// Enumerate asset history.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="id">Id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<EnumerationResult<RemoteAsset>> EnumerateAssetHistoryAsync(EnumerationRequest request, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate published assets.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<EnumerationResult<RemoteAsset>> EnumeratePublishedAssetsAsync(EnumerationRequest request, CancellationToken token = default);

        /// <summary>
        /// Count drafts.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<ContentDraftCounts> CountDraftsAsync(CancellationToken token = default);

        /// <summary>
        /// List changes since.
        /// </summary>
        /// <param name="since">Since.</param>
        /// <param name="limit">Limit.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<ContentChange>> ListChangesSinceAsync(long since, int limit, CancellationToken token = default);

        /// <summary>
        /// Oldest change version.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<long?> OldestChangeVersionAsync(CancellationToken token = default);
    }
}
