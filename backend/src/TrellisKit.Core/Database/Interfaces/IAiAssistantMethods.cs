namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Assistant data access (aiassistants and its child tables, aiknowledgeindexes, aiknowledgeitems). Implementations
    /// are bound to one property and safe to call concurrently.
    /// </summary>
    public interface IAiAssistantMethods
    {
        /// <summary>
        /// Create an assistant with its models, starter prompts, embed origins, and knowledge sources.
        /// </summary>
        /// <param name="assistant">Assistant.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored assistant.</returns>
        /// <exception cref="ArgumentNullException">The assistant is null.</exception>
        /// <exception cref="DuplicateRecordException">The name is taken.</exception>
        Task<Assistant> CreateAsync(Assistant assistant, CancellationToken token = default);

        /// <summary>
        /// Read a live assistant with every child list.
        /// </summary>
        /// <param name="id">Assistant id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Assistant, or null.</returns>
        Task<Assistant?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read an assistant including soft-deleted ones (for conversations of deleted assistants).
        /// </summary>
        /// <param name="id">Assistant id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Assistant, or null.</returns>
        Task<Assistant?> ReadAnyAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read assistants by id (at most 100), including deleted ones, without child lists except models.
        /// </summary>
        /// <param name="ids">Ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Assistants found.</returns>
        Task<List<Assistant>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default);

        /// <summary>
        /// Update every column and replace every child list.
        /// </summary>
        /// <param name="assistant">Assistant.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The assistant.</returns>
        /// <exception cref="DuplicateRecordException">The name is taken.</exception>
        Task<Assistant> UpdateAsync(Assistant assistant, CancellationToken token = default);

        /// <summary>
        /// Soft-delete an assistant and remove its child rows.
        /// </summary>
        /// <param name="id">Assistant id.</param>
        /// <param name="nowUtc">Deletion time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when deleted.</returns>
        Task<bool> DeleteAsync(string id, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Enumerate live assistants (AiCollections.AdminAssistants or AiCollections.Assistants), with every child list.
        /// </summary>
        /// <param name="request">Prepared request.</param>
        /// <param name="filter">Filters.</param>
        /// <param name="audiences">When not null, only assistants whose audience is listed, enabled only.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<Assistant>> EnumerateAsync(EnumerationRequest request, AiEnumerationFilter filter, IReadOnlyCollection<string>? audiences, CancellationToken token = default);

        /// <summary>
        /// Live assistants with knowledge enabled and an embedding model, in id order after a cursor (at most limit).
        /// </summary>
        /// <param name="limit">Most rows. Minimum 1, maximum 200.</param>
        /// <param name="afterId">Cursor, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Assistants with every child list.</returns>
        Task<List<Assistant>> ListForIndexingAsync(int limit, string? afterId, CancellationToken token = default);

        /// <summary>
        /// Assistant ids (live and deleted) in id order after a cursor, for retention passes.
        /// </summary>
        /// <param name="limit">Most rows.</param>
        /// <param name="afterId">Cursor, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Assistants without child lists.</returns>
        Task<List<Assistant>> ListPageAsync(int limit, string? afterId, CancellationToken token = default);

        /// <summary>
        /// Read the knowledge index state of an assistant.
        /// </summary>
        /// <param name="assistantId">Assistant id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>State, or null.</returns>
        Task<AiKnowledgeIndex?> ReadIndexAsync(string assistantId, CancellationToken token = default);

        /// <summary>
        /// Insert or replace the knowledge index state of an assistant.
        /// </summary>
        /// <param name="index">State.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task UpsertIndexAsync(AiKnowledgeIndex index, CancellationToken token = default);

        /// <summary>
        /// Read the knowledge items of an assistant (at most limit), keyed by target type and id.
        /// </summary>
        /// <param name="assistantId">Assistant id.</param>
        /// <param name="limit">Most rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Items.</returns>
        Task<List<AiKnowledgeItem>> ListItemsAsync(string assistantId, int limit, CancellationToken token = default);

        /// <summary>
        /// Insert or replace one knowledge item.
        /// </summary>
        /// <param name="item">Item.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task UpsertItemAsync(AiKnowledgeItem item, CancellationToken token = default);

        /// <summary>
        /// Delete every knowledge item and the index state of an assistant.
        /// </summary>
        /// <param name="assistantId">Assistant id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task DeleteKnowledgeAsync(string assistantId, CancellationToken token = default);

        /// <summary>
        /// Enumerate the knowledge items of an assistant (collection AiCollections.KnowledgeItems).
        /// </summary>
        /// <param name="request">Prepared request.</param>
        /// <param name="assistantId">Assistant id.</param>
        /// <param name="filter">Filters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<AiKnowledgeItem>> EnumerateItemsAsync(EnumerationRequest request, string assistantId, AiEnumerationFilter filter, CancellationToken token = default);

        /// <summary>
        /// Every tool kill switch row of the property (bounded by the engine tool registry).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Switches.</returns>
        Task<List<AssistantToolSwitch>> ListToolSwitchesAsync(CancellationToken token = default);

        /// <summary>
        /// Create or replace the kill switch of one tool.
        /// </summary>
        /// <param name="toolSwitch">Switch.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task UpsertToolSwitchAsync(AssistantToolSwitch toolSwitch, CancellationToken token = default);
    }
}
