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
    /// Feature flag, kill switch, and app content version data access. Every method is bound to one property; implementations are safe to call concurrently.
    /// </summary>
    public interface IAppConfigMethods
    {
        /// <summary>
        /// Read draft.
        /// </summary>
        /// <param name="objectId">Object id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<FeatureFlag?> ReadDraftAsync(string objectId, CancellationToken token = default);

        /// <summary>
        /// Read current.
        /// </summary>
        /// <param name="objectId">Object id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<FeatureFlag?> ReadCurrentAsync(string objectId, CancellationToken token = default);

        /// <summary>
        /// Read revision.
        /// </summary>
        /// <param name="revisionId">Revision id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<FeatureFlag?> ReadRevisionAsync(string revisionId, CancellationToken token = default);

        /// <summary>
        /// Key in use.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="excludeObjectId">Exclude object id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> KeyInUseAsync(string key, string? excludeObjectId, CancellationToken token = default);

        /// <summary>
        /// Save draft.
        /// </summary>
        /// <param name="flag">Flag.</param>
        /// <param name="token">Cancellation token.</param>
        Task SaveDraftAsync(FeatureFlag flag, CancellationToken token = default);

        /// <summary>
        /// Delete draft.
        /// </summary>
        /// <param name="objectId">Object id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> DeleteDraftAsync(string objectId, CancellationToken token = default);

        /// <summary>
        /// Has published revision.
        /// </summary>
        /// <param name="objectId">Object id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> HasPublishedRevisionAsync(string objectId, CancellationToken token = default);

        /// <summary>
        /// List current.
        /// </summary>
        /// <param name="killSwitches">Kill switches.</param>
        /// <param name="limit">Limit.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<FeatureFlag>> ListCurrentAsync(bool killSwitches, int limit, CancellationToken token = default);

        /// <summary>
        /// List at version.
        /// </summary>
        /// <param name="version">Version.</param>
        /// <param name="limit">Limit.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<FeatureFlag>> ListAtVersionAsync(long version, int limit, CancellationToken token = default);

        /// <summary>
        /// List drafts.
        /// </summary>
        /// <param name="limit">Limit.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<FeatureFlag>> ListDraftsAsync(int limit, CancellationToken token = default);

        /// <summary>Exact count of pending flag drafts, excluding kill switches.</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Draft count.</returns>
        Task<long> CountDraftsAsync(CancellationToken token = default);

        /// <summary>Count configured flags, excluding kill switches.</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Flag count.</returns>
        Task<long> CountFlagsAsync(CancellationToken token = default);

        /// <summary>
        /// Enumerate flags.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="filter">Filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<EnumerationResult<FeatureFlag>> EnumerateFlagsAsync(EnumerationRequest request, FeatureFlagFilter filter, CancellationToken token = default);

        /// <summary>
        /// Enumerate drafts.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<EnumerationResult<FeatureFlag>> EnumerateDraftsAsync(EnumerationRequest request, CancellationToken token = default);

        /// <summary>
        /// Enumerate history.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="objectId">Object id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<EnumerationResult<FeatureFlag>> EnumerateHistoryAsync(EnumerationRequest request, string objectId, CancellationToken token = default);

        /// <summary>
        /// Read state.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<AppContentState> ReadStateAsync(CancellationToken token = default);

        /// <summary>
        /// Publish.
        /// </summary>
        /// <param name="expectedVersion">Expected version.</param>
        /// <param name="record">Record.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> PublishAsync(long expectedVersion, ContentVersion record, CancellationToken token = default);

        /// <summary>
        /// Prune changes.
        /// </summary>
        /// <param name="belowVersion">Below version.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<int> PruneChangesAsync(long belowVersion, CancellationToken token = default);

        /// <summary>
        /// Enumerate versions.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<EnumerationResult<ContentVersion>> EnumerateVersionsAsync(EnumerationRequest request, CancellationToken token = default);

        /// <summary>
        /// Read version.
        /// </summary>
        /// <param name="version">Version.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<ContentVersion?> ReadVersionAsync(long version, CancellationToken token = default);

        /// <summary>
        /// Enumerate changes.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="version">Version.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<EnumerationResult<FeatureFlag>> EnumerateChangesAsync(EnumerationRequest request, long version, CancellationToken token = default);

        /// <summary>
        /// Ensure kill switches.
        /// </summary>
        /// <param name="seeds">Seeds.</param>
        /// <param name="token">Cancellation token.</param>
        Task EnsureKillSwitchesAsync(List<FeatureFlag> seeds, CancellationToken token = default);

        /// <summary>
        /// List kill switch states.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<KillSwitchState>> ListKillSwitchStatesAsync(CancellationToken token = default);

        /// <summary>
        /// Save kill switch.
        /// </summary>
        /// <param name="live">Live.</param>
        /// <param name="state">State.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<long> SaveKillSwitchAsync(FeatureFlag live, KillSwitchState state, CancellationToken token = default);

        /// <summary>
        /// Enumerate kill switches.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="killed">Killed.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<EnumerationResult<FeatureFlag>> EnumerateKillSwitchesAsync(EnumerationRequest request, bool? killed, CancellationToken token = default);
    }
}
