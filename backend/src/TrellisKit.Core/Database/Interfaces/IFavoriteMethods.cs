namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Property-scoped saves and private collections (REST_API.md, Saves). Every statement carries the bound property
    /// predicate, and collection memberships only ever link a save and a collection of the same member and property.
    /// Labels and tags live in the shared metadata tables; deleting a save or a collection deletes them too.
    /// </summary>
    public interface IFavoriteMethods
    {
        /// <summary>
        /// Create a save, or update the stored note of an existing save of the same member and target.
        /// </summary>
        /// <param name="favorite">Save (Id used only when created).</param>
        /// <param name="setNote">Whether to replace the note of an existing save.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored save and whether it was created.</returns>
        /// <exception cref="ArgumentNullException">The save is null or misses its member or target.</exception>
        Task<FavoriteUpsertResult> UpsertAsync(Favorite favorite, bool setNote, CancellationToken token = default);

        /// <summary>
        /// Read a save by identifier (any member), or null.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Save or null.</returns>
        Task<Favorite?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a member's save of a target, or null.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Save (with its collection identifiers) or null.</returns>
        Task<Favorite?> ReadByTargetAsync(string userId, FavoriteTargetTypeEnum targetType, string targetId, CancellationToken token = default);

        /// <summary>
        /// Delete a member's save of a target (idempotent).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a save was deleted.</returns>
        Task<bool> DeleteAsync(string userId, FavoriteTargetTypeEnum targetType, string targetId, CancellationToken token = default);

        /// <summary>
        /// Enumerate a member's saves (orderings CreatedDescending, CreatedAscending, UpdatedDescending), each with its
        /// collection identifiers.
        /// </summary>
        /// <param name="request">Prepared request.</param>
        /// <param name="filter">Filter (UserId required).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or the member is missing.</exception>
        Task<EnumerationResult<Favorite>> EnumerateAsync(EnumerationRequest request, FavoriteFilter filter, CancellationToken token = default);

        /// <summary>
        /// The member's saves among the given targets (at most 100 targets per call).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="targets">Targets.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The saves found (no collection identifiers).</returns>
        /// <exception cref="ArgumentException">More than 100 targets.</exception>
        Task<List<Favorite>> StateAsync(string userId, IReadOnlyList<FavoriteTargetKey> targets, CancellationToken token = default);

        /// <summary>
        /// Count a member's saves.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountForUserAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Replace the collections that hold a save. Each collection must belong to the save's member; otherwise
        /// nothing changes and false is returned (never a cross-member link). New memberships go last in their
        /// collection; kept memberships keep their position.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="favoriteId">Save.</param>
        /// <param name="collectionIds">Collections (distinct, possibly empty).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when applied.</returns>
        Task<bool> SetCollectionsAsync(string userId, string favoriteId, IReadOnlyList<string> collectionIds, CancellationToken token = default);

        /// <summary>
        /// Create a collection at the end of the member's order.
        /// </summary>
        /// <param name="collection">Collection.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored collection.</returns>
        /// <exception cref="TrellisKit.Core.Exceptions.DuplicateRecordException">The member has a collection with this name.</exception>
        Task<FavoriteCollection> CreateCollectionAsync(FavoriteCollection collection, CancellationToken token = default);

        /// <summary>
        /// Read a member's collection, or null.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="id">Collection.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Collection or null.</returns>
        Task<FavoriteCollection?> ReadCollectionAsync(string userId, string id, CancellationToken token = default);

        /// <summary>
        /// Read a member's collections by identifier (at most 500), ignoring other members' collections.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="ids">Collections.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Collections found.</returns>
        Task<List<FavoriteCollection>> ReadCollectionsAsync(string userId, IEnumerable<string> ids, CancellationToken token = default);

        /// <summary>
        /// The identifiers of every collection of a member in order (at most limit).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="limit">Most identifiers (1 to 1000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Identifiers.</returns>
        Task<List<string>> CollectionIdsAsync(string userId, int limit, CancellationToken token = default);

        /// <summary>
        /// Rename a member's collection.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="id">Collection.</param>
        /// <param name="name">New name.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when updated.</returns>
        /// <exception cref="TrellisKit.Core.Exceptions.DuplicateRecordException">The member has a collection with this name.</exception>
        Task<bool> RenameCollectionAsync(string userId, string id, string name, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Delete a member's collection, its memberships, labels, and tags. The saves stay.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="id">Collection.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when deleted.</returns>
        Task<bool> DeleteCollectionAsync(string userId, string id, CancellationToken token = default);

        /// <summary>
        /// Set the order of a member's collections (ids is the full set, validated by the caller).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="ids">Collections in the new order.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task ReorderCollectionsAsync(string userId, IReadOnlyList<string> ids, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Count a member's collections.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountCollectionsAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Enumerate a member's collections with their item counts (orderings PositionAscending, NameAscending,
        /// CreatedDescending, CreatedAscending).
        /// </summary>
        /// <param name="request">Prepared request.</param>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<FavoriteCollection>> EnumerateCollectionsAsync(EnumerationRequest request, string userId, CancellationToken token = default);

        /// <summary>
        /// A target was deleted: delete every member's saves of it (with their memberships, labels, and tags).
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetIds">Target identifiers (at most 1000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Saves deleted.</returns>
        Task<int> DeleteForTargetAsync(FavoriteTargetTypeEnum targetType, IEnumerable<string> targetIds, CancellationToken token = default);

        /// <summary>
        /// Account deletion: delete every save and collection of a member, with memberships, labels, and tags.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Saves deleted.</returns>
        Task<int> DeleteForUserAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Account export page of a member's saves in identifier order, with collection identifiers.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="afterId">Last identifier of the previous page, or null.</param>
        /// <param name="limit">Page size (1 to 1000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Saves.</returns>
        Task<List<Favorite>> ReadForUserAsync(string userId, string? afterId, int limit, CancellationToken token = default);

        /// <summary>
        /// Account export page of a member's collections in identifier order, with item counts.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="afterId">Last identifier of the previous page, or null.</param>
        /// <param name="limit">Page size (1 to 1000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Collections.</returns>
        Task<List<FavoriteCollection>> ReadCollectionsForUserAsync(string userId, string? afterId, int limit, CancellationToken token = default);
    }
}
