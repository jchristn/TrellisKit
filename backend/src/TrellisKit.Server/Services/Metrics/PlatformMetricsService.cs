namespace TrellisKit.Server.Services.Metrics
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Metrics;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Settings;

    /// <summary>Read-only platform comparisons over at most 500 registered properties. Every read uses a property-bound metrics service.</summary>
    public sealed class PlatformMetricsService
    {
        /// <summary>Property metadata filters and computed orderings. Metric filters are inside Query.</summary>
        public static readonly EnumerationCollection Collection = new EnumerationCollection("platform-metric-comparisons", TargetTypeEnum.Property, "p.id", "p.createdutc", true)
            .Order("ValueDescending", "p.id", true, EnumerationKeyKindEnum.Text)
            .Order("ValueAscending", "p.id", false, EnumerationKeyKindEnum.Text)
            .Search("p.id", "p.slug", "p.name").Filters("status");
        private readonly PlatformService _Platform;
        private readonly EnumerationService _Enumeration;

        /// <summary>Create a platform metrics reader.</summary>
        /// <param name="platform">Authenticated platform service.</param>
        /// <param name="settings">Enumeration limits.</param>
        public PlatformMetricsService(PlatformService platform, AppSettings settings)
        {
            _Platform = platform ?? throw new ArgumentNullException(nameof(platform));
            _Enumeration = new EnumerationService(settings) { PropertyId = PropertyIds.Platform };
        }

        /// <summary>Compare any registered metric. Computed rankings use skip paging and return at most 20 rows.</summary>
        /// <param name="input">Property filters and metric query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Ranked property metrics.</returns>
        public async Task<EnumerationResult<PropertyMetricComparison>> EnumerateAsync(PlatformMetricQuery input, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (input.Query == null || String.IsNullOrWhiteSpace(input.Query.Metric)) throw new ApiErrorException(400, "ValidationFailed", "A registered metric query is required.");
            if (input.Query.From >= input.Query.To || input.Query.To - input.Query.From > TimeSpan.FromDays(366)) throw new ApiErrorException(400, "ValidationFailed", "Choose a range up to 366 days.");
            if (!String.IsNullOrEmpty(input.Query.GroupBy)) throw new ApiErrorException(400, "ValidationFailed", "Property comparisons use properties as groups; groupBy must be empty.");
            input.Ordering ??= "ValueDescending";
            EnumerationRequest request = _Enumeration.Prepare(input, Collection, true, JsonSerializer.Serialize(input.Query), 20);
            Dictionary<string,string> errors = new Dictionary<string,string>();
            PropertyStatusEnum? status = input.FilterEnum<PropertyStatusEnum>("status", errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "Invalid property filters.");
            EnumerationRequest source = new EnumerationRequest
            {
                Collection = Collection, Query = input, MaxResults = 500, Skip = 0,
                Ordering = new EnumerationOrdering { Name = "CreatedDescending", Expression = "p.createdutc", Descending = true, KeyKind = EnumerationKeyKindEnum.Time },
                Fingerprint = request.Fingerprint, CountTotals = true, IncludeStaffMetadata = true
            };
            EnumerationResult<PropertyRecord> properties = await _Platform.Directory.Database.Registry.EnumerateAsync(source, status, null, token).ConfigureAwait(false);
            if (!properties.EndOfResults) throw new ApiErrorException(400, "ValidationFailed", "Narrow the property filters to at most 500 properties.");
            Dictionary<string,ObjectMetadata> metadata = await _Platform.RealmMetadata.LoadAsync(TargetTypeEnum.Property, properties.Objects.Select(p => p.Id), true, token).ConfigureAwait(false);
            List<PropertyMetricComparison> rows = new List<PropertyMetricComparison>();
            foreach (PropertyRecord property in properties.Objects.Where(p => p.Status != PropertyStatusEnum.Deleted && p.Status != PropertyStatusEnum.Deleting))
            {
                token.ThrowIfCancellationRequested();
                PropertyMetricComparison row = await ReadAsync(property, input.Query, false, token).ConfigureAwait(false);
                if (metadata.TryGetValue(property.Id, out ObjectMetadata? m)) { row.Labels = m.Labels; row.Tags = m.Tags; }
                rows.Add(row);
            }
            string Key(PropertyMetricComparison row)
            {
                if(!row.Value.HasValue)return request.Ordering.Descending?"0":"1";
                ulong bits=BitConverter.DoubleToUInt64Bits(row.Value.Value);
                ulong sortable=(bits&0x8000000000000000UL)!=0?~bits:bits^0x8000000000000000UL;
                return (request.Ordering.Descending?"1":"0")+sortable.ToString("X16",CultureInfo.InvariantCulture);
            }
            EnumerationResult<PropertyMetricComparison> page=CatalogEnumeration.Page(request,rows,r=>r.PropertyId,
                r=>properties.Objects.First(p=>p.Id==r.PropertyId).CreatedUtc,
                r=>r.Name+" "+properties.Objects.First(p=>p.Id==r.PropertyId).Slug,
                r=>r.Labels,r=>r.Tags,Key);
            if(input.IncludeSeries)
                foreach(PropertyMetricComparison row in page.Objects.Where(r=>r.UnavailableReason==null))
                    row.Data=(await ReadAsync(properties.Objects.First(p=>p.Id==row.PropertyId),input.Query,true,token).ConfigureAwait(false)).Data;
            return page;
        }

        /// <summary>Query one registered property's metric, including a disabled property's retained history.</summary>
        /// <param name="propertyId">Property identifier.</param>
        /// <param name="query">Metric query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Property-scoped result.</returns>
        public async Task<MetricEnumerationResult> QueryAsync(string propertyId, MetricEnumerationQuery query, CancellationToken token = default)
        {
            PropertyRecord? record = PropertyIds.IsValid(propertyId) ? await _Platform.Directory.Database.Registry.ReadAsync(propertyId, token).ConfigureAwait(false) : null;
            if (record == null || record.Status == PropertyStatusEnum.Deleted || record.Status == PropertyStatusEnum.Deleting) throw ApiErrorException.NotFound("Property");
            PropertyRuntime? runtime=_Platform.Directory.Find(record.Id);bool temporary=runtime==null;
            if(runtime==null)
            {
                PropertyConfiguration configuration=PropertyLoader.Parse(record.DocumentJson,"registry property "+record.Id);
                configuration.Directory=record.SourceDirectory??String.Empty;runtime=_Platform.Directory.Build(record,configuration);
            }
            try { return await new MetricEnumerationService(runtime.Services).QueryAsync(new ContentActor{IsSystemAdmin=true},query,token).ConfigureAwait(false); }
            finally { if(temporary)await runtime.DisposeAsync().ConfigureAwait(false); }
        }

        private async Task<PropertyMetricComparison> ReadAsync(PropertyRecord record, MetricQuery query, bool includeData, CancellationToken token)
        {
            PropertyRuntime? runtime = _Platform.Directory.Find(record.Id);
            bool temporary = runtime == null;
            if (runtime == null)
            {
                PropertyConfiguration configuration = PropertyLoader.Parse(record.DocumentJson, "registry property " + record.Id);
                configuration.Directory = record.SourceDirectory ?? String.Empty;
                runtime = _Platform.Directory.Build(record, configuration);
            }
            try
            {
                MetricsService metrics = runtime.Services.Metrics;
                MetricDefinition? definition = metrics.Registry.Find(query.Metric);
                PropertyMetricComparison row = new PropertyMetricComparison { PropertyId = record.Id, Name = record.Name, Metric = query.Metric };
                if (definition == null) { row.UnavailableReason = "MetricNotRegistered"; return row; }
                MetricQuery scoped = new MetricQuery { Metric = query.Metric, From = query.From, To = query.To, Bucket = query.Bucket, GroupBy = query.GroupBy, Filters = query.Filters, Percentile = query.Percentile, Compare = includeData && query.Compare };
                MetricResult result = await metrics.QueryAsync(new ContentActor { IsSystemAdmin = true }, scoped, token).ConfigureAwait(false);
                List<MetricPoint> points = result.Series.SelectMany(s => s.Points).OrderBy(p => p.StartUtc).ToList();
                List<double> values = points.Where(p => p.Value.HasValue).Select(p => p.Value!.Value).ToList();
                row.Unit = definition.Unit;
                row.Aggregation = definition.Kind == "Distribution" ? "WorstBucket" : definition.Kind == "Count" ? "Sum" : "Last";
                row.Value = values.Count == 0 || points.Any(p => p.Suppressed) ? null : row.Aggregation == "Sum" ? values.Sum() : row.Aggregation == "WorstBucket" ? values.Max() : points.LastOrDefault()?.Value;
                row.Complete = points.Count > 0 && points.All(p => p.Complete);
                row.CompleteThroughUtc = result.CompleteThroughUtc;
                row.Data = includeData ? await new MetricEnumerationService(runtime.Services).QueryAsync(new ContentActor{IsSystemAdmin=true},new MetricEnumerationQuery{Query=scoped},token).ConfigureAwait(false) : null;
                return row;
            }
            finally { if (temporary) await runtime.DisposeAsync().ConfigureAwait(false); }
        }
    }
}
