namespace TrellisKit.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Shared;
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Metrics;
    using TrellisKit.Core.Responses;

    /// <summary>Database keyset pages for growing metric history collections; source rows are never loaded wholesale.</summary>
    public sealed class MetricEnumerationStore
    {
        private readonly Reader _Reader;
        /// <summary>Bind to exactly one property.</summary>
        public MetricEnumerationStore(DatabaseDriverBase database)
        {
            ArgumentNullException.ThrowIfNull(database);
            _Reader = new Reader(database, database.BoundPropertyId ?? throw new ArgumentException("Metrics need a property binding.", nameof(database)));
        }
        /// <summary>One worker-history page with exact totals.</summary>
        public Task<EnumerationResult<WorkerPassRecord>> WorkersAsync(EnumerationRequest request, CancellationToken token = default) => _Reader.WorkersAsync(request, token);
        /// <summary>One annotation page with exact totals.</summary>
        public Task<EnumerationResult<MetricAnnotation>> AnnotationsAsync(EnumerationRequest request, CancellationToken token = default) => _Reader.AnnotationsAsync(request, token);
        private sealed class Reader : MethodsBase
        {
            internal Reader(DatabaseDriverBase database, string propertyId) : base(database, PostgresqlDialect.Instance, propertyId) { }
            internal Task<EnumerationResult<WorkerPassRecord>> WorkersAsync(EnumerationRequest request, CancellationToken token)
            {
                List<string> conditions = Conditions(request, "w.startedutc");
                foreach (string key in new[] { "worker", "outcome" }) if (request.Query.FilterString(key) is string value) conditions.Add("w." + key + "=" + D.Str(value));
                if (request.Query.FilterString("outcome") is string outcome && outcome != "Ok" && outcome != "Failed" && outcome != "Cancelled") throw ApiErrorException.Validation(new Dictionary<string, string> { { "filters.outcome", "Use Ok, Failed, or Cancelled." } }, "Invalid worker outcome.");
                return EnumerateAsync(request, "w.*", "metricworkerruns w", conditions, r => new WorkerPassRecord { Id = RowReader.GetString(r, "id"), Worker = RowReader.GetString(r, "worker"), StartedUtc = RowReader.GetDateTime(r, "startedutc"), EndedUtc = RowReader.GetDateTime(r, "endedutc"), Outcome = RowReader.GetString(r, "outcome"), ItemsProcessed = RowReader.GetInt(r, "items"), Error = RowReader.GetNullableString(r, "error"), IntervalSeconds = RowReader.GetDouble(r, "intervalseconds") }, token);
            }
            internal Task<EnumerationResult<MetricAnnotation>> AnnotationsAsync(EnumerationRequest request, CancellationToken token)
            {
                List<string> conditions = Conditions(request, "a.occurredutc");
                if (request.Query.FilterString("kind") is string kind) conditions.Add("a.kind=" + D.Str(kind));
                return EnumerateAsync(request, "a.*", "metricannotations a", conditions, r => new MetricAnnotation { Id = RowReader.GetString(r, "id"), Kind = RowReader.GetString(r, "kind"), Label = RowReader.GetString(r, "label"), OccurredUtc = RowReader.GetDateTime(r, "occurredutc") }, token);
            }
            private List<string> Conditions(EnumerationRequest request, string timestamp)
            {
                Dictionary<string, string> errors = new Dictionary<string, string>();
                DateTime? from = request.Query.FilterUtc("from", errors), to = request.Query.FilterUtc("to", errors);
                if (from.HasValue && to.HasValue && to <= from) errors["filters.to"] = "Must be after from.";
                foreach (string key in request.Query.Filters.Keys) if (request.Query.FilterString(key) == null) errors["filters." + key] = "Must be a scalar value.";
                if (errors.Count > 0) throw ApiErrorException.Validation(errors, "Invalid metric history filter.");
                List<string> conditions = new List<string>();
                if (from.HasValue) conditions.Add(timestamp + ">=" + D.Ts(from.Value)); if (to.HasValue) conditions.Add(timestamp + "<" + D.Ts(to.Value));
                // Operational observations carry empty inline metadata, so filters match none rather than being ignored.
                if (request.Query.Labels.Count > 0 || request.Query.Tags.Count > 0) conditions.Add("FALSE");
                return conditions;
            }
        }
    }
}
