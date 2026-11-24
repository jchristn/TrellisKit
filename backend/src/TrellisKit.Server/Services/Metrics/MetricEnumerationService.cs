namespace TrellisKit.Server.Services.Metrics
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Metrics;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>Standard enumeration over definitions, declarations, persisted history, and actual metric buckets.</summary>
    public sealed class MetricEnumerationService
    {
        private readonly ApplicationServices _Services;
        private readonly EnumerationService _Enumeration;
        private readonly MetricEnumerationStore _Store;
        /// <summary>Create a property-scoped reader.</summary>
        public MetricEnumerationService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            _Enumeration = new EnumerationService(services.Settings) { PropertyId = services.PropertyId };
            _Store = new MetricEnumerationStore(services.Database);
        }
        /// <summary>One registry page with real filters, ordering, totals and continuation.</summary>
        public EnumerationResult<MetricDefinition> Definitions(EnumerationQuery query)
        {
            EnumerationRequest request = _Enumeration.Prepare(query, MetricCollections.Definitions, true);
            ValidateScalarFilters(query);
            if (query.FilterString("kind") is string kind && !new[] { "Count", "Gauge", "Distinct", "Distribution" }.Contains(kind)) throw ApiErrorException.Validation(new Dictionary<string, string> { { "filters.kind", "Use Count, Gauge, Distinct, or Distribution." } }, "Invalid metric kind.");
            List<MetricDefinition> source = _Services.Metrics.Registry.Definitions.Where(d => (query.FilterString("kind") == null || d.Kind == query.FilterString("kind")) && (query.FilterString("source") == null || d.Source == query.FilterString("source")) && (query.FilterString("feature") == null || d.Feature == query.FilterString("feature"))).ToList();
            return CatalogEnumeration.Page(request, source, d => d.Key, search: d => d.Key + " " + d.Title, labels: d => d.Labels, tags: d => d.Tags, orderKey: d => d.Key);
        }
        /// <summary>One dashboard declaration page.</summary>
        public EnumerationResult<MetricPageDeclaration> Pages(EnumerationQuery query)
        {
            EnumerationRequest request = _Enumeration.Prepare(query, MetricCollections.Pages, true);
            return CatalogEnumeration.Page(request, _Services.Metrics.Registry.Pages.ToList(), p => p.Id, search: p => p.Id + " " + p.Title + " " + p.Question, labels: p => p.Labels, tags: p => p.Tags);
        }
        /// <summary>Database-paged worker passes, including exact filtered totals.</summary>
        public Task<EnumerationResult<WorkerPassRecord>> WorkersAsync(EnumerationQuery query, CancellationToken token = default) => _Store.WorkersAsync(_Enumeration.Prepare(query, MetricCollections.Workers, true), token);
        /// <summary>Database-paged annotations, independently of chart point paging.</summary>
        public Task<EnumerationResult<MetricAnnotation>> AnnotationsAsync(EnumerationQuery query, CancellationToken token = default) => _Store.AnnotationsAsync(_Enumeration.Prepare(query, MetricCollections.Annotations, true), token);
        /// <summary>Page actual current/comparison observations, never a one-item wrapper around an unpaged chart.</summary>
        public async Task<MetricEnumerationResult> QueryAsync(ContentActor actor, MetricEnumerationQuery input, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (input.Query == null) throw ApiErrorException.Validation(new Dictionary<string, string> { { "query", "A metric query is required." } }, "Invalid metric query.");
            string scope = JsonSerializer.Serialize(input.Query);
            EnumerationRequest request = _Enumeration.Prepare(input, MetricCollections.Observations, true, scope);
            ValidateScalarFilters(input);
            if (input.FilterString("period") is string period && period != "Current" && period != "Previous") throw ApiErrorException.Validation(new Dictionary<string, string> { { "filters.period", "Use Current or Previous." } }, "Invalid metric period.");
            MetricResult source = await _Services.Metrics.QueryAsync(actor, input.Query, token).ConfigureAwait(false);
            List<MetricObservationRow> rows = new List<MetricObservationRow>();
            void Add(IEnumerable<MetricSeries> series, string period)
            {
                if (input.FilterString("period") is string selected && selected != period) return;
                foreach (MetricSeries group in series)
                {
                    if (input.FilterString("seriesKey") is string key && key != group.Key) continue;
                    foreach (MetricPoint point in group.Points) rows.Add(new MetricObservationRow { Id = period + ":" + group.Key + ":" + point.StartUtc.ToString("O", CultureInfo.InvariantCulture), Period = period, SeriesKey = group.Key, StartUtc = point.StartUtc, EndUtc = point.EndUtc, Value = point.Value, Complete = point.Complete, Suppressed = point.Suppressed, Samples = point.Samples });
                }
            }
            Add(source.Series, "Current"); Add(source.Previous, "Previous");
            EnumerationResult<MetricObservationRow> page = CatalogEnumeration.Page(request, rows, p => p.Id, p => p.StartUtc, p => p.SeriesKey + " " + p.Period, labels: p => p.Labels, tags: p => p.Tags);
            return MetricEnumerationResult.From(page, source);
        }
        private static void ValidateScalarFilters(EnumerationQuery query)
        {
            foreach (string key in query.Filters.Keys) if (query.FilterString(key) == null || query.FilterString(key)!.Length > 100) throw ApiErrorException.Validation(new Dictionary<string, string> { { "filters." + key, "Use a scalar value of at most 100 characters." } }, "Invalid filter value.");
        }
    }
}
