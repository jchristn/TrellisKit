namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Event categories and the events configuration row (docs/PHASE4_CONTRACT.md section 0.2). Every statement is
    /// property scoped.
    /// </summary>
    public interface IEventCategoryMethods
    {
        /// <summary>
        /// Insert a category (key unique, case-insensitive).
        /// </summary>
        /// <param name="category">Category.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="TrellisKit.Core.Exceptions.DuplicateRecordException">The key is taken.</exception>
        Task CreateAsync(EventCategory category, CancellationToken token = default);

        /// <summary>
        /// Read a category, or null.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Category or null.</returns>
        Task<EventCategory?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Every category (bounded by events.maxCategories), by sort order.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Categories.</returns>
        Task<List<EventCategory>> ListAllAsync(CancellationToken token = default);

        /// <summary>
        /// One page of categories.
        /// </summary>
        /// <param name="includeRetired">Include retired categories.</param>
        /// <param name="request">Normalized enumeration request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<EventCategory>> EnumerateAsync(bool includeRetired, EnumerationRequest request, CancellationToken token = default);

        /// <summary>
        /// Write a category's label, color, sort order, and retired flag.
        /// </summary>
        /// <param name="category">Category.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task UpdateAsync(EventCategory category, CancellationToken token = default);

        /// <summary>
        /// Delete a category row.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when deleted.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Whether any undeleted event uses a category key.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when in use.</returns>
        Task<bool> InUseAsync(string key, CancellationToken token = default);

        /// <summary>
        /// Set sort orders 0, 1, 2, ... in the given order.
        /// </summary>
        /// <param name="ids">Category ids.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows updated.</returns>
        Task<int> SetOrderAsync(List<string> ids, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// The source hash of the last seed from property.json, or null when never seeded.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Hash or null.</returns>
        Task<string?> ReadSourceHashAsync(CancellationToken token = default);

        /// <summary>
        /// Seed categories from property.json (insert missing keys, update labels and colors of seeded keys) and store
        /// the source hash, in one transaction.
        /// </summary>
        /// <param name="categories">Categories from the property document.</param>
        /// <param name="sourceHash">Hash of the section.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task SeedAsync(List<EventCategory> categories, string sourceHash, DateTime nowUtc, CancellationToken token = default);
    }
}
