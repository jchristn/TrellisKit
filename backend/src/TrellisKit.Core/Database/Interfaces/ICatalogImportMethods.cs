namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>Property-scoped reviewed catalog source registry and immutable per-key identity mappings.</summary>
    public interface ICatalogImportMethods
    {
        /// <summary>Read one source by identifier; null when absent.</summary>
        Task<CatalogImportSource?> ReadAsync(string id, CancellationToken token = default);
        /// <summary>Enumerate a validated bounded source page including metadata filters.</summary>
        Task<EnumerationResult<CatalogImportSource>> EnumerateAsync(EnumerationRequest request, CancellationToken token = default);
        /// <summary>Insert or atomically replace a source and its metadata. False on revision conflict.</summary>
        Task<bool> SaveAsync(CatalogImportSource source, long? expectedRevision, CancellationToken token = default);
        /// <summary>Select up to 20 reviewed enabled sources, oldest attempted first, or an explicit set of at most 20 identifiers.</summary>
        Task<List<CatalogImportSource>> PlanAsync(IReadOnlyList<string> ids, CancellationToken token = default);
        /// <summary>Record a real fetch attempt without changing configuration revision.</summary>
        Task TouchAsync(string id, DateTime now, CancellationToken token = default);
        /// <summary>Read the stable mapping for one external key, including mappings to deleted entries.</summary>
        Task<CatalogImportItem?> ItemAsync(string sourceId, string key, CancellationToken token = default);
    }
}
