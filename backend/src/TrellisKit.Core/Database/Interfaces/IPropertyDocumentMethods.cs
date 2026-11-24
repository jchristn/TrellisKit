namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// The property's document versions and members' acceptances (tables propertydocumentversions and
    /// propertydocumentacceptances; docs/TRELLISKIT.md section 14). The current version of a document is its highest
    /// sequence. Implementations are safe to call concurrently.
    /// </summary>
    public interface IPropertyDocumentMethods
    {
        /// <summary>
        /// Read the current version of every document, at most 100 (property.json allows 30).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Current versions, by key.</returns>
        Task<List<PropertyDocumentVersion>> ListCurrentAsync(CancellationToken token = default);

        /// <summary>
        /// The highest sequence per document whose version requires re-acceptance, at most 100 documents.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Sequence by document key.</returns>
        Task<Dictionary<string, int>> RequiredSequencesAsync(CancellationToken token = default);

        /// <summary>
        /// Read the current version of one document.
        /// </summary>
        /// <param name="documentKey">Document key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Version, or null when none is stored.</returns>
        Task<PropertyDocumentVersion?> ReadCurrentAsync(string documentKey, CancellationToken token = default);

        /// <summary>
        /// Read a version by id.
        /// </summary>
        /// <param name="id">Version id (dcv_).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Version, or null.</returns>
        Task<PropertyDocumentVersion?> ReadVersionAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Store a version.
        /// </summary>
        /// <param name="version">Version.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The version is null.</exception>
        /// <exception cref="DuplicateRecordException">The document already has this sequence.</exception>
        Task CreateVersionAsync(PropertyDocumentVersion version, CancellationToken token = default);

        /// <summary>
        /// Count the versions of one document.
        /// </summary>
        /// <param name="documentKey">Document key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountVersionsAsync(string documentKey, CancellationToken token = default);

        /// <summary>
        /// Enumerate one document's versions (content omitted; AcceptanceCount filled).
        /// </summary>
        /// <param name="request">Normalized request.</param>
        /// <param name="documentKey">Document key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<EnumerationResult<PropertyDocumentVersion>> EnumerateVersionsAsync(EnumerationRequest request, string documentKey, CancellationToken token = default);

        /// <summary>
        /// Store acceptances (one transaction). An acceptance of a version the member already accepted is skipped.
        /// </summary>
        /// <param name="acceptances">Acceptances (at most 100).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The list is null.</exception>
        Task CreateAcceptancesAsync(List<DocumentAcceptance> acceptances, CancellationToken token = default);

        /// <summary>
        /// The highest accepted sequence per document for one member, at most 100 documents.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Sequence by document key.</returns>
        Task<Dictionary<string, int>> AcceptedSequencesAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Distinct members who accepted a document, optionally only one sequence.
        /// </summary>
        /// <param name="documentKey">Document key.</param>
        /// <param name="sequence">Sequence, or null for any version.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountAcceptedMembersAsync(string documentKey, int? sequence, CancellationToken token = default);

        /// <summary>
        /// Enumerate acceptances.
        /// </summary>
        /// <param name="request">Normalized request.</param>
        /// <param name="filter">Filters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<EnumerationResult<DocumentAcceptance>> EnumerateAcceptancesAsync(EnumerationRequest request, DocumentAcceptanceFilter filter, CancellationToken token = default);

        /// <summary>
        /// One member's acceptances after a position, oldest first (account export pages).
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="afterId">Exclusive id position, or null to start.</param>
        /// <param name="limit">Page size (1 to 1000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<List<DocumentAcceptance>> ListByUserAsync(string userId, string? afterId, int limit, CancellationToken token = default);
    }
}
