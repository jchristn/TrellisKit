namespace TrellisKit.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>Property-scoped, transactional exercise and technique catalog persistence.</summary>
    public interface IExerciseCatalogMethods
    {
        /// <summary>Read an undeleted entry by identifier or slug.</summary>
        /// <param name="key">Identifier or slug.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entry or null.</returns>
        Task<CatalogEntry?> ReadAsync(string key, CancellationToken token = default);
        /// <summary>Find one existing undeleted entry matching any of at most 31 exact normalized names or aliases; import deduplication only.</summary>
        Task<string?> DuplicateNameAsync(IReadOnlyList<string> names, CancellationToken token = default);
        /// <summary>Read a bounded catalog page.</summary>
        /// <param name="request">Prepared enumeration.</param>
        /// <param name="publishedOnly">Only approved public entries.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<CatalogEntry>> EnumerateAsync(EnumerationRequest request, bool publishedOnly, CancellationToken token = default);
        /// <summary>Atomically insert or compare-and-swap an entry with content and optional outgoing relationships.</summary>
        /// <param name="entry">Validated entry.</param>
        /// <param name="expectedRevision">Null for insert, current revision for update.</param>
        /// <param name="replaceContent">Replace structured child values and metadata.</param>
        /// <param name="relationships">Null keeps the outgoing graph; otherwise bounded replacement.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>False on a stale update.</returns>
        Task<bool> SaveAsync(CatalogEntry entry, long? expectedRevision, bool replaceContent, List<CatalogRelationship>? relationships, CancellationToken token = default);
        /// <summary>Atomically apply an imported draft and provenance while locking the reviewed enabled source revision. Existing entries additionally require an untouched Draft with no moderation hold.</summary>
        Task<bool> SaveImportedAsync(CatalogEntry entry, long? expectedRevision, CatalogImportItem import, CancellationToken token = default);
        /// <summary>Enumerate directed relationships, hiding unpublished targets from public reads.</summary>
        /// <param name="request">Prepared query.</param>
        /// <param name="sourceId">Source entry.</param>
        /// <param name="publishedOnly">Public target visibility.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<CatalogRelationship>> RelationshipsAsync(EnumerationRequest request, string sourceId, bool publishedOnly, CancellationToken token = default);
    }
}
