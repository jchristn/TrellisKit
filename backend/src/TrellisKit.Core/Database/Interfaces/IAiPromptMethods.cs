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
    /// Data access of administrator-editable AI prompt templates and their versions.
    /// </summary>
    public interface IAiPromptMethods
    {
        /// <summary>
        /// Every stored template of the property.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Templates keyed by catalog key.</returns>
        Task<Dictionary<string, AiPromptTemplate>> ReadAllAsync(CancellationToken token = default);

        /// <summary>
        /// Save a new text: store it as the current template with the next version number and record the version.
        /// </summary>
        /// <param name="key">Catalog key.</param>
        /// <param name="content">Text.</param>
        /// <param name="versionId">Identifier of the version row.</param>
        /// <param name="userId">Administrator, or null.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored template.</returns>
        /// <exception cref="ArgumentNullException">key or content is null or empty.</exception>
        Task<AiPromptTemplate> SaveAsync(string key, string content, string versionId, string? userId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Save a draft: record a version with the next version number that is not live until
        /// <see cref="PublishAsync"/>.
        /// </summary>
        /// <param name="key">Catalog key.</param>
        /// <param name="content">Text.</param>
        /// <param name="versionId">Identifier of the version row.</param>
        /// <param name="userId">Administrator, or null.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The draft version.</returns>
        /// <exception cref="ArgumentNullException">key, content, or versionId is null or empty.</exception>
        Task<AiPromptTemplateVersion> SaveDraftAsync(string key, string content, string versionId, string? userId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Publish a draft version: mark it published and make it the current template.
        /// </summary>
        /// <param name="key">Catalog key.</param>
        /// <param name="versionId">Draft version identifier.</param>
        /// <param name="userId">Administrator, or null.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The current template, or null when the version is not an unpublished draft of the key.</returns>
        /// <exception cref="ArgumentNullException">key or versionId is null or empty.</exception>
        Task<AiPromptTemplate?> PublishAsync(string key, string versionId, string? userId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// The newest version of every key (a draft when it is unpublished and not a reset).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Newest versions keyed by catalog key.</returns>
        Task<Dictionary<string, AiPromptTemplateVersion>> ReadNewestVersionsAsync(CancellationToken token = default);

        /// <summary>
        /// One version by identifier, or null.
        /// </summary>
        /// <param name="versionId">Version identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Version or null.</returns>
        /// <exception cref="ArgumentNullException">versionId is null or empty.</exception>
        Task<AiPromptTemplateVersion?> ReadVersionAsync(string versionId, CancellationToken token = default);

        /// <summary>
        /// Reset to the default: delete the current template and record a reset version with the default text.
        /// </summary>
        /// <param name="key">Catalog key.</param>
        /// <param name="defaultContent">The default text, recorded in the version history.</param>
        /// <param name="versionId">Identifier of the version row.</param>
        /// <param name="userId">Administrator, or null.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task ResetAsync(string key, string defaultContent, string versionId, string? userId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// A page of the versions of one key, newest first.
        /// </summary>
        /// <param name="request">Prepared enumeration.</param>
        /// <param name="key">Catalog key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<AiPromptTemplateVersion>> EnumerateVersionsAsync(EnumerationRequest request, string key, CancellationToken token = default);
    }
}
