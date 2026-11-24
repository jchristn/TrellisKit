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
    /// Deployment-level property registry: properties, their domains and apps, and the reviewed deployment-wide lookups
    /// (docs/MULTIPROPERTY_CONTRACT.md section 4.3 rule 1). Callers bind to the returned property id before any
    /// further property-owned data access.
    /// </summary>
    public interface IPropertyRegistryMethods
    {
        /// <summary>
        /// Register a property with its domains and apps, in one transaction.
        /// </summary>
        /// <param name="record">Property.</param>
        /// <param name="domains">Domains (at most 20).</param>
        /// <param name="apps">Apps (at most 10).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="Exceptions.DuplicateRecordException">The id, slug, a host, or an app id is already registered.</exception>
        Task CreateAsync(PropertyRecord record, List<PropertyDomain> domains, List<PropertyApp> apps, CancellationToken token = default);

        /// <summary>
        /// Read a property by id (any status).
        /// </summary>
        /// <param name="id">Property id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Property, or null.</returns>
        Task<PropertyRecord?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read every property that is not Deleted, in id order (bounded by the hard cap of 500 rows; the deployment
        /// limit is deployment.maxProperties).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Properties.</returns>
        Task<List<PropertyRecord>> ReadLiveAsync(CancellationToken token = default);

        /// <summary>
        /// Enumerate properties (never Deleted tombstones unless filtered by status).
        /// </summary>
        /// <param name="request">Normalized enumeration request.</param>
        /// <param name="status">Optional status filter.</param>
        /// <param name="host">Optional exact domain filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<PropertyRecord>> EnumerateAsync(EnumerationRequest request, PropertyStatusEnum? status, string? host, CancellationToken token = default);

        /// <summary>
        /// Count properties that are not Deleted.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountLiveAsync(CancellationToken token = default);

        /// <summary>
        /// Update a property's mutable fields (name, slug, status, document, source directory, lifecycle timestamps).
        /// </summary>
        /// <param name="record">Property.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task UpdateAsync(PropertyRecord record, CancellationToken token = default);

        /// <summary>
        /// Read a property's domains (at most 20).
        /// </summary>
        /// <param name="propertyId">Property id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Domains.</returns>
        Task<List<PropertyDomain>> ReadDomainsAsync(string propertyId, CancellationToken token = default);

        /// <summary>
        /// Read a property's apps (at most 10).
        /// </summary>
        /// <param name="propertyId">Property id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Apps.</returns>
        Task<List<PropertyApp>> ReadAppsAsync(string propertyId, CancellationToken token = default);

        /// <summary>
        /// Replace a property's domains and apps in one transaction (null leaves that list unchanged) and touch the
        /// property's updatedutc.
        /// </summary>
        /// <param name="propertyId">Property id.</param>
        /// <param name="domains">New domains, or null.</param>
        /// <param name="apps">New apps, or null.</param>
        /// <param name="updatedUtc">Change time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="Exceptions.DuplicateRecordException">A host or app id is registered to another property.</exception>
        Task ReplaceDomainsAndAppsAsync(string propertyId, List<PropertyDomain>? domains, List<PropertyApp>? apps, DateTime updatedUtc, CancellationToken token = default);

        /// <summary>
        /// Latest updatedutc over every property (the registry cache reload check), or null when none.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Timestamp.</returns>
        Task<DateTime?> ReadLatestChangeAsync(CancellationToken token = default);

        /// <summary>
        /// Deployment-wide lookup for the MediaConvert webhook: the property that owns a processor job id.
        /// </summary>
        /// <param name="jobId">Processor job id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Property id, or null.</returns>
        Task<string?> FindMediaJobPropertyAsync(string jobId, CancellationToken token = default);

        /// <summary>
        /// Properties whose purge is due (status Deleting and deletescheduledutc at or before now), at most 25.
        /// </summary>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Properties.</returns>
        Task<List<PropertyRecord>> ReadPurgeDueAsync(DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Delete up to batchSize rows of one property-owned table for one property (property purge).
        /// </summary>
        /// <param name="table">Property-owned table name (from <see cref="PropertyTables.Owned"/>).</param>
        /// <param name="propertyId">Property id.</param>
        /// <param name="batchSize">Rows per batch. Minimum 1, maximum 10000.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        /// <exception cref="ArgumentException">The table is not a property-owned table.</exception>
        Task<int> PurgeBatchAsync(string table, string propertyId, int batchSize, CancellationToken token = default);

        /// <summary>
        /// Count the non-deleted member accounts of a property.
        /// </summary>
        /// <param name="propertyId">Property id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountMembersAsync(string propertyId, CancellationToken token = default);

        /// <summary>
        /// Record that a property's profile fields were seeded (the first time only) and the hash of the profile field
        /// section they were last merged from. Does not change updatedutc (no registry reload).
        /// </summary>
        /// <param name="propertyId">Property id.</param>
        /// <param name="hash">Hash of the seeding section, or null.</param>
        /// <param name="seededUtc">Seed time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task MarkProfileFieldsSeededAsync(string propertyId, string? hash, DateTime seededUtc, CancellationToken token = default);

        /// <summary>
        /// Write a domain's verification state (verifiedUtc, last check, error) and mark the registry changed so every
        /// instance reloads its routing.
        /// </summary>
        /// <param name="domain">Domain.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task UpdateDomainVerificationAsync(PropertyDomain domain, CancellationToken token = default);

        /// <summary>
        /// Whether another property (any status, tombstones included) uses a slug (case-insensitive).
        /// </summary>
        /// <param name="slug">Slug.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when in use.</returns>
        Task<bool> SlugInUseAsync(string slug, CancellationToken token = default);
    }
}
