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
    /// Labels and tags of every object type (docs/ENUMERATION_AND_METADATA.md section 1), stored in the shared labels
    /// and tags tables. Implementations are safe to call concurrently.
    /// </summary>
    public interface IMetadataMethods
    {
        /// <summary>
        /// Read one object's labels and tags (empty when none).
        /// </summary>
        /// <param name="targetType">Object type.</param>
        /// <param name="targetId">Object identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Metadata; never null.</returns>
        /// <exception cref="ArgumentNullException">The identifier is null or empty.</exception>
        Task<ObjectMetadata> ReadAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default);

        /// <summary>
        /// Read the labels and tags of many objects of one type in two queries.
        /// </summary>
        /// <param name="targetType">Object type.</param>
        /// <param name="targetIds">Identifiers (duplicates ignored).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Metadata keyed by every distinct requested identifier.</returns>
        /// <exception cref="ArgumentNullException">The identifiers are null.</exception>
        Task<Dictionary<string, ObjectMetadata>> ReadManyAsync(TargetTypeEnum targetType, IEnumerable<string> targetIds, CancellationToken token = default);

        /// <summary>
        /// Replace an object's labels and tags with already validated metadata, in one transaction.
        /// </summary>
        /// <param name="targetType">Object type.</param>
        /// <param name="targetId">Object identifier.</param>
        /// <param name="metadata">Metadata to store.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task WriteAsync(TargetTypeEnum targetType, string targetId, ObjectMetadata metadata, CancellationToken token = default);

        /// <summary>
        /// Replace the labels and tags of many objects with the same already validated metadata, in one transaction
        /// (for example every code of a sign-up code batch).
        /// </summary>
        /// <param name="targetType">Object type.</param>
        /// <param name="targetIds">Object identifiers.</param>
        /// <param name="metadata">Metadata to store on each.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task WriteManyAsync(TargetTypeEnum targetType, IEnumerable<string> targetIds, ObjectMetadata metadata, CancellationToken token = default);

        /// <summary>
        /// Delete an object's public labels and tags and keep its staff: entries, in one transaction (account deletion).
        /// </summary>
        /// <param name="targetType">Object type.</param>
        /// <param name="targetId">Object identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The identifier is null or empty.</exception>
        Task DeletePublicAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default);

        /// <summary>
        /// Statements that delete an object's labels and tags, to include in the transaction that deletes the object.
        /// </summary>
        /// <param name="targetType">Object type.</param>
        /// <param name="targetId">Object identifier.</param>
        /// <returns>Statements.</returns>
        /// <exception cref="ArgumentNullException">The identifier is null or empty.</exception>
        List<string> DeleteStatements(TargetTypeEnum targetType, string targetId);

        /// <summary>
        /// Statements that delete the labels and tags of every object whose id a subquery returns.
        /// </summary>
        /// <param name="targetType">Object type.</param>
        /// <param name="idSubquery">SQL subquery returning ids, for example "SELECT id FROM comments WHERE authoruserid = 'usr_x'".</param>
        /// <returns>Statements.</returns>
        /// <exception cref="ArgumentNullException">The subquery is null or empty.</exception>
        List<string> DeleteWhereStatements(TargetTypeEnum targetType, string idSubquery);

        /// <summary>
        /// Delete an object's labels and tags.
        /// </summary>
        /// <param name="targetType">Object type.</param>
        /// <param name="targetId">Object identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The identifier is null or empty.</exception>
        Task DeleteAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default);

        /// <summary>
        /// Resolve an object for the generic metadata routes: whether it exists (and is not deleted) and its owner.
        /// </summary>
        /// <param name="targetType">Object type.</param>
        /// <param name="targetId">Object identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Target, or null when it does not exist or the type cannot carry metadata.</returns>
        /// <exception cref="ArgumentNullException">The identifier is null or empty.</exception>
        Task<MetadataTarget?> ReadTargetAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default);

        /// <summary>
        /// Distinct labels with counts, most used first.
        /// </summary>
        /// <param name="request">Normalized request (collection "labels"; skip and the token position are offsets).</param>
        /// <param name="targetType">Only this object type, or null for every type the caller can see.</param>
        /// <param name="prefix">Only labels starting with this text (case-insensitive), or null.</param>
        /// <param name="viewerUserId">Caller, for owner-only types; null for the admin token.</param>
        /// <param name="isStaff">Whether staff labels and every object count.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentNullException">The request is null.</exception>
        Task<EnumerationResult<LabelCount>> EnumerateLabelsAsync(EnumerationRequest request, TargetTypeEnum? targetType, string? prefix, string? viewerUserId, bool isStaff, CancellationToken token = default);

        /// <summary>
        /// Distinct tag keys with counts, most used first.
        /// </summary>
        /// <param name="request">Normalized request.</param>
        /// <param name="targetType">Only this object type, or null.</param>
        /// <param name="prefix">Only keys starting with this text, or null.</param>
        /// <param name="viewerUserId">Caller.</param>
        /// <param name="isStaff">Whether staff keys and every object count.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentNullException">The request is null.</exception>
        Task<EnumerationResult<TagKeyCount>> EnumerateTagKeysAsync(EnumerationRequest request, TargetTypeEnum? targetType, string? prefix, string? viewerUserId, bool isStaff, CancellationToken token = default);

        /// <summary>
        /// Distinct values of one tag key with counts, most used first.
        /// </summary>
        /// <param name="request">Normalized request.</param>
        /// <param name="targetType">Only this object type, or null.</param>
        /// <param name="key">Tag key (case-insensitive).</param>
        /// <param name="prefix">Only values starting with this text, or null.</param>
        /// <param name="viewerUserId">Caller.</param>
        /// <param name="isStaff">Whether staff keys and every object count.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentNullException">The request or key is null.</exception>
        Task<EnumerationResult<TagValueCount>> EnumerateTagValuesAsync(EnumerationRequest request, TargetTypeEnum? targetType, string key, string? prefix, string? viewerUserId, bool isStaff, CancellationToken token = default);
    }
}
