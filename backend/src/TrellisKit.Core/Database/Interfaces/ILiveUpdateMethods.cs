namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Live update data access: groups, updates, assets, rollout plans, channels, deploy and signing keys, installs, and events. Every method is bound to one property; implementations are safe to call concurrently.
    /// </summary>
    public interface ILiveUpdateMethods
    {
        /// <summary>
        /// Create group.
        /// </summary>
        /// <param name="group">Group.</param>
        /// <param name="token">Cancellation token.</param>
        Task CreateGroupAsync(UpdateGroup group, CancellationToken token = default);

        /// <summary>
        /// Read group.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<UpdateGroup?> ReadGroupAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read group by client id.
        /// </summary>
        /// <param name="clientGroupId">Client group id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<UpdateGroup?> ReadGroupByClientIdAsync(string clientGroupId, CancellationToken token = default);

        /// <summary>
        /// Find joinable group.
        /// </summary>
        /// <param name="channel">Channel.</param>
        /// <param name="gitCommit">Git commit.</param>
        /// <param name="message">Message.</param>
        /// <param name="platform">Platform.</param>
        /// <param name="sinceUtc">Since utc.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<UpdateGroup?> FindJoinableGroupAsync(string channel, string? gitCommit, string message, string platform, DateTime sinceUtc, CancellationToken token = default);

        /// <summary>
        /// Save group.
        /// </summary>
        /// <param name="group">Group.</param>
        /// <param name="syncRollout">Sync rollout.</param>
        /// <param name="token">Cancellation token.</param>
        Task SaveGroupAsync(UpdateGroup group, bool syncRollout, CancellationToken token = default);

        /// <summary>
        /// Set update rollout.
        /// </summary>
        /// <param name="updateId">Update id.</param>
        /// <param name="basisPoints">Basis points.</param>
        /// <param name="nowUtc">Now utc.</param>
        /// <param name="token">Cancellation token.</param>
        Task SetUpdateRolloutAsync(string updateId, int basisPoints, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Delete draft group.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> DeleteDraftGroupAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate groups.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="filter">Filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<EnumerationResult<UpdateGroup>> EnumerateGroupsAsync(EnumerationRequest request, UpdateGroupFilter filter, CancellationToken token = default);

        /// <summary>
        /// Add update.
        /// </summary>
        /// <param name="update">Update.</param>
        /// <param name="expoClient">Expo client.</param>
        /// <param name="assets">Assets.</param>
        /// <param name="sourceMaps">Source maps.</param>
        /// <param name="token">Cancellation token.</param>
        Task AddUpdateAsync(UpdateRecord update, string expoClient, List<UpdateAssetRef> assets, List<UpdateSourceMap> sourceMaps, CancellationToken token = default);

        /// <summary>
        /// Copy update.
        /// </summary>
        /// <param name="sourceUpdateId">Source update id.</param>
        /// <param name="target">Target.</param>
        /// <param name="token">Cancellation token.</param>
        Task CopyUpdateAsync(string sourceUpdateId, UpdateRecord target, CancellationToken token = default);

        /// <summary>
        /// Add directive update.
        /// </summary>
        /// <param name="update">Update.</param>
        /// <param name="token">Cancellation token.</param>
        Task AddDirectiveUpdateAsync(UpdateRecord update, CancellationToken token = default);

        /// <summary>
        /// List group updates.
        /// </summary>
        /// <param name="groupId">Group id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<UpdateRecord>> ListGroupUpdatesAsync(string groupId, CancellationToken token = default);

        /// <summary>
        /// Read update.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<UpdateRecord?> ReadUpdateAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read update by uuid.
        /// </summary>
        /// <param name="uuid">Uuid.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<UpdateRecord?> ReadUpdateByUuidAsync(string uuid, CancellationToken token = default);

        /// <summary>
        /// List candidates.
        /// </summary>
        /// <param name="platform">Platform.</param>
        /// <param name="runtimeVersion">Runtime version.</param>
        /// <param name="channel">Channel.</param>
        /// <param name="limit">Limit.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<UpdateRecord>> ListCandidatesAsync(string platform, string runtimeVersion, string channel, int limit, CancellationToken token = default);

        /// <summary>
        /// Max created at.
        /// </summary>
        /// <param name="platform">Platform.</param>
        /// <param name="runtimeVersion">Runtime version.</param>
        /// <param name="channel">Channel.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<DateTime?> MaxCreatedAtAsync(string platform, string runtimeVersion, string channel, CancellationToken token = default);

        /// <summary>
        /// List published on target.
        /// </summary>
        /// <param name="platform">Platform.</param>
        /// <param name="runtimeVersion">Runtime version.</param>
        /// <param name="channel">Channel.</param>
        /// <param name="excludeGroupId">Exclude group id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<UpdateRecord>> ListPublishedOnTargetAsync(string platform, string runtimeVersion, string channel, string excludeGroupId, CancellationToken token = default);

        /// <summary>
        /// Read latest.
        /// </summary>
        /// <param name="channel">Channel.</param>
        /// <param name="platform">Platform.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<UpdateRecord?> ReadLatestAsync(string channel, string platform, CancellationToken token = default);

        /// <summary>
        /// Read expo client.
        /// </summary>
        /// <param name="updateId">Update id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<string?> ReadExpoClientAsync(string updateId, CancellationToken token = default);

        /// <summary>
        /// List asset refs.
        /// </summary>
        /// <param name="updateId">Update id.</param>
        /// <param name="limit">Limit.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<UpdateAssetRef>> ListAssetRefsAsync(string updateId, int limit, CancellationToken token = default);

        /// <summary>
        /// Enumerate asset refs.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="updateId">Update id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<EnumerationResult<UpdateAssetRef>> EnumerateAssetRefsAsync(EnumerationRequest request, string updateId, CancellationToken token = default);

        /// <summary>
        /// Count source maps.
        /// </summary>
        /// <param name="updateId">Update id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<long> CountSourceMapsAsync(string updateId, CancellationToken token = default);

        /// <summary>
        /// Read asset.
        /// </summary>
        /// <param name="sha256Hex">Sha256 hex.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<UpdateAsset?> ReadAssetAsync(string sha256Hex, CancellationToken token = default);

        /// <summary>
        /// Stored assets.
        /// </summary>
        /// <param name="sha256Hexes">Sha256 hexes.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<HashSet<string>> StoredAssetsAsync(IEnumerable<string> sha256Hexes, CancellationToken token = default);

        /// <summary>
        /// Upsert asset.
        /// </summary>
        /// <param name="asset">Asset.</param>
        /// <param name="token">Cancellation token.</param>
        Task UpsertAssetAsync(UpdateAsset asset, CancellationToken token = default);

        /// <summary>
        /// Replace stages.
        /// </summary>
        /// <param name="groupId">Group id.</param>
        /// <param name="stages">Stages.</param>
        /// <param name="token">Cancellation token.</param>
        Task ReplaceStagesAsync(string groupId, List<UpdateRolloutStage> stages, CancellationToken token = default);

        /// <summary>
        /// List stages.
        /// </summary>
        /// <param name="groupId">Group id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<UpdateRolloutStage>> ListStagesAsync(string groupId, CancellationToken token = default);

        /// <summary>
        /// List due stages.
        /// </summary>
        /// <param name="nowUtc">Now utc.</param>
        /// <param name="limit">Limit.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<UpdateDueStage>> ListDueStagesAsync(DateTime nowUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Mark stage applied.
        /// </summary>
        /// <param name="groupId">Group id.</param>
        /// <param name="position">Position.</param>
        /// <param name="nowUtc">Now utc.</param>
        /// <param name="token">Cancellation token.</param>
        Task MarkStageAppliedAsync(string groupId, int position, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// List auto halt candidates.
        /// </summary>
        /// <param name="limit">Limit.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<UpdateGroup>> ListAutoHaltCandidatesAsync(int limit, CancellationToken token = default);

        /// <summary>
        /// Count health.
        /// </summary>
        /// <param name="groupId">Group id.</param>
        /// <param name="activeSinceUtc">Active since utc.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<UpdateHealthCounts> CountHealthAsync(string groupId, DateTime activeSinceUtc, CancellationToken token = default);

        /// <summary>
        /// Ensure channels.
        /// </summary>
        /// <param name="builtIn">Built in.</param>
        /// <param name="nowUtc">Now utc.</param>
        /// <param name="token">Cancellation token.</param>
        Task EnsureChannelsAsync(IEnumerable<string> builtIn, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// List channels.
        /// </summary>
        /// <param name="limit">Limit.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<UpdateChannel>> ListChannelsAsync(int limit, CancellationToken token = default);

        /// <summary>
        /// Read channel.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<UpdateChannel?> ReadChannelAsync(string name, CancellationToken token = default);

        /// <summary>
        /// Enumerate channels.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="builtIn">Built in.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<EnumerationResult<UpdateChannel>> EnumerateChannelsAsync(EnumerationRequest request, bool? builtIn, CancellationToken token = default);

        /// <summary>
        /// Create channel.
        /// </summary>
        /// <param name="channel">Channel.</param>
        /// <param name="token">Cancellation token.</param>
        Task CreateChannelAsync(UpdateChannel channel, CancellationToken token = default);

        /// <summary>
        /// Delete channel.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> DeleteChannelAsync(string name, CancellationToken token = default);

        /// <summary>
        /// Count live groups in channel.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<long> CountLiveGroupsInChannelAsync(string name, CancellationToken token = default);

        /// <summary>
        /// Create deploy key.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="token">Cancellation token.</param>
        Task CreateDeployKeyAsync(UpdateDeployKey key, CancellationToken token = default);

        /// <summary>
        /// Read deploy key.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<UpdateDeployKey?> ReadDeployKeyAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read deploy key by secret.
        /// </summary>
        /// <param name="secretHash">Secret hash.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<UpdateDeployKey?> ReadDeployKeyBySecretAsync(string secretHash, CancellationToken token = default);

        /// <summary>
        /// Enumerate deploy keys.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="active">Active.</param>
        /// <param name="nowUtc">Now utc.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<EnumerationResult<UpdateDeployKey>> EnumerateDeployKeysAsync(EnumerationRequest request, bool? active, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Revoke deploy key.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="nowUtc">Now utc.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> RevokeDeployKeyAsync(string id, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Touch deploy key.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="nowUtc">Now utc.</param>
        /// <param name="token">Cancellation token.</param>
        Task TouchDeployKeyAsync(string id, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// List signing keys.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<UpdateSigningKey>> ListSigningKeysAsync(CancellationToken token = default);

        /// <summary>
        /// Read signing key.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<UpdateSigningKey?> ReadSigningKeyAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Create signing key.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="token">Cancellation token.</param>
        Task CreateSigningKeyAsync(UpdateSigningKey key, CancellationToken token = default);

        /// <summary>
        /// Set signing key status.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="status">Status.</param>
        /// <param name="nowUtc">Now utc.</param>
        /// <param name="token">Cancellation token.</param>
        Task SetSigningKeyStatusAsync(string id, UpdateSigningKeyStatusEnum status, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Touch signing key.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="nowUtc">Now utc.</param>
        /// <param name="token">Cancellation token.</param>
        Task TouchSigningKeyAsync(string id, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Enumerate signing keys.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="status">Status.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<EnumerationResult<UpdateSigningKey>> EnumerateSigningKeysAsync(EnumerationRequest request, UpdateSigningKeyStatusEnum? status, CancellationToken token = default);

        /// <summary>
        /// Upsert install.
        /// </summary>
        /// <param name="install">Install.</param>
        /// <param name="token">Cancellation token.</param>
        Task UpsertInstallAsync(UpdateInstall install, CancellationToken token = default);

        /// <summary>
        /// Add install failures.
        /// </summary>
        /// <param name="installIdHash">Install id hash.</param>
        /// <param name="updateUuids">Update uuids.</param>
        /// <param name="nowUtc">Now utc.</param>
        /// <param name="token">Cancellation token.</param>
        Task AddInstallFailuresAsync(string installIdHash, List<string> updateUuids, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// List install failures.
        /// </summary>
        /// <param name="installIdHash">Install id hash.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<HashSet<string>> ListInstallFailuresAsync(string installIdHash, CancellationToken token = default);

        /// <summary>
        /// Count installs.
        /// </summary>
        /// <param name="platform">Platform.</param>
        /// <param name="channel">Channel.</param>
        /// <param name="currentUpdateUuid">Current update uuid.</param>
        /// <param name="activeSinceUtc">Active since utc.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<long> CountInstallsAsync(string? platform, string? channel, string? currentUpdateUuid, DateTime activeSinceUtc, CancellationToken token = default);

        /// <summary>
        /// Enumerate runtime versions.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="platform">Platform.</param>
        /// <param name="channel">Channel.</param>
        /// <param name="activeSinceUtc">Active since utc.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<EnumerationResult<UpdateRuntimeVersionRow>> EnumerateRuntimeVersionsAsync(EnumerationRequest request, string? platform, string? channel, DateTime activeSinceUtc, CancellationToken token = default);

        /// <summary>
        /// Count installs by.
        /// </summary>
        /// <param name="column">Column.</param>
        /// <param name="platform">Platform.</param>
        /// <param name="runtimeVersion">Runtime version.</param>
        /// <param name="currentUpdateUuid">Current update uuid.</param>
        /// <param name="activeSinceUtc">Active since utc.</param>
        /// <param name="limit">Limit.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<UpdateCountRow>> CountInstallsByAsync(string column, string? platform, string? runtimeVersion, string? currentUpdateUuid, DateTime activeSinceUtc, int limit, CancellationToken token = default);

        /// <summary>Page grouped active install counts in SQL. Creation time is the earliest install first-seen time in the group.</summary>
        /// <param name="request">Normalized enumeration request using alias ic.</param>
        /// <param name="column">Allowlisted grouping column: keyid or appversion.</param>
        /// <param name="updateIds">Optional current update IDs; empty means no matches.</param>
        /// <param name="activeSinceUtc">Active install cutoff.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A bounded grouped page.</returns>
        Task<EnumerationResult<UpdateCountRow>> EnumerateInstallCountsAsync(EnumerationRequest request, string column, IReadOnlyCollection<string>? updateIds, DateTime activeSinceUtc, CancellationToken token = default);

        /// <summary>
        /// Count distinct install values.
        /// </summary>
        /// <param name="column">Column.</param>
        /// <param name="platform">Platform.</param>
        /// <param name="runtimeVersion">Runtime version.</param>
        /// <param name="currentUpdateUuid">Current update uuid.</param>
        /// <param name="activeSinceUtc">Active since utc.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<long> CountDistinctInstallValuesAsync(string column, string? platform, string? runtimeVersion, string? currentUpdateUuid, DateTime activeSinceUtc, CancellationToken token = default);

        /// <summary>
        /// Insert events.
        /// </summary>
        /// <param name="events">Events.</param>
        /// <param name="token">Cancellation token.</param>
        Task InsertEventsAsync(List<UpdateEvent> events, CancellationToken token = default);

        /// <summary>
        /// Increment count.
        /// </summary>
        /// <param name="updateUuid">Update uuid.</param>
        /// <param name="day">Day.</param>
        /// <param name="type">Type.</param>
        /// <param name="token">Cancellation token.</param>
        Task IncrementCountAsync(string updateUuid, string day, string type, CancellationToken token = default);

        /// <summary>
        /// Event counts.
        /// </summary>
        /// <param name="updateUuids">Update uuids.</param>
        /// <param name="sinceDay">Since day.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<UpdateCountRow>> EventCountsAsync(List<string> updateUuids, string sinceDay, CancellationToken token = default);

        /// <summary>
        /// Prune installs.
        /// </summary>
        /// <param name="cutoffUtc">Cutoff utc.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<int> PruneInstallsAsync(DateTime cutoffUtc, CancellationToken token = default);

        /// <summary>
        /// Prune events.
        /// </summary>
        /// <param name="cutoffUtc">Cutoff utc.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<int> PruneEventsAsync(DateTime cutoffUtc, CancellationToken token = default);

        /// <summary>
        /// Enumerate the failure details (message and stack trace) reported for one update, newest first.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="updateUuid">Update UUID.</param>
        /// <param name="type">Event type, or null for every failure type.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<EnumerationResult<UpdateEventDetail>> EnumerateEventDetailsAsync(EnumerationRequest request, string updateUuid, UpdateEventTypeEnum? type, CancellationToken token = default);

        /// <summary>
        /// Read one failure detail.
        /// </summary>
        /// <param name="eventId">Event id (uev_).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The detail, or null.</returns>
        Task<UpdateEventDetail?> ReadEventDetailAsync(string eventId, CancellationToken token = default);

        /// <summary>
        /// List the private source maps of an update.
        /// </summary>
        /// <param name="updateId">Update id (upd_).</param>
        /// <param name="limit">Most rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Source maps in upload order.</returns>
        Task<List<UpdateSourceMap>> ListSourceMapsAsync(string updateId, int limit, CancellationToken token = default);

        /// <summary>
        /// Read an install row by its install id hash.
        /// </summary>
        /// <param name="installIdHash">HMAC of the install id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The install, or null.</returns>
        Task<UpdateInstall?> ReadInstallAsync(string installIdHash, CancellationToken token = default);

        /// <summary>
        /// Delete, in one statement, up to <paramref name="limit"/> asset rows that no update references and that were
        /// last referenced before the cutoff (UpdateAssetGcWorker). The caller deletes the stored objects afterwards.
        /// </summary>
        /// <param name="cutoffUtc">Retention cutoff.</param>
        /// <param name="limit">Batch size.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The deleted asset rows.</returns>
        Task<List<UpdateAsset>> DeleteUnreferencedAssetsAsync(DateTime cutoffUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Queue a stored object for garbage collection (insert, or restart its wait when it is queued already).
        /// </summary>
        /// <param name="item">Object (kind, hash, storage key, size).</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task QueueGcObjectAsync(UpdateGcObject item, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// One bounded garbage collection batch of a kind (UpdateAssetGcWorker): queued objects past the cutoff that are
        /// referenced again (a source map row or a remote asset revision uses the hash) restart their wait; up to
        /// <paramref name="limit"/> unreferenced ones are removed from the queue in a statement that re-checks the
        /// references and returned so the caller deletes the stored objects.
        /// </summary>
        /// <param name="kind">UpdateGcObject.SourceMapKind or UpdateGcObject.ContentAssetKind.</param>
        /// <param name="cutoffUtc">Objects queued before this are due.</param>
        /// <param name="nowUtc">Now (the new wait start of referenced objects).</param>
        /// <param name="limit">Batch size.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The removed queue rows.</returns>
        /// <exception cref="ArgumentException">The kind is unknown.</exception>
        Task<List<UpdateGcObject>> DeleteUnreferencedGcObjectsAsync(string kind, DateTime cutoffUtc, DateTime nowUtc, int limit, CancellationToken token = default);
    }
}
