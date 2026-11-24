namespace TrellisKit.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Postgresql;
    /// <summary>Atomic property-scoped milestone aggregation with minimal account-erasable deduplication state.</summary>
    public sealed class ProductMetricEventStore
    {
        private readonly DatabaseDriverBase _Database;
        private readonly string _Property;
        /// <summary>Require a property-bound database.</summary>
        public ProductMetricEventStore(DatabaseDriverBase database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Property = database.BoundPropertyId ?? throw new ArgumentException("Product events require a bound property.", nameof(database));
        }
        /// <summary>Remove member identifiers while retaining anonymous counts.</summary>
        public Task EraseAsync(string memberId, CancellationToken token = default)
        {
            return _Database.ExecuteQueryAsync("DELETE FROM metricfirstactions WHERE propertyid=" + Sanitizer.Str(_Property) + " AND userid=" + Sanitizer.Str(memberId) + ";", false, token);
        }
        /// <summary>Claim a milestone and increment its aggregate in the same statement; racing claims count only once.</summary>
        public Task RecordOnceAsync(string name, string memberId, Dictionary<string,string> dimensions, CancellationToken token = default)
        {
            string pid = Sanitizer.Str(_Property), now = Sanitizer.Ts(DateTime.UtcNow);
            string sql = "WITH claimed AS (INSERT INTO metricfirstactions(propertyid,userid,action,occurredutc) VALUES(" + pid + "," + Sanitizer.Str(memberId) + "," + Sanitizer.Str(name) + "," + now + ") ON CONFLICT DO NOTHING RETURNING 1) INSERT INTO metricseries(propertyid,metric,bucketutc,dimensions,value,samples,histogram,computedutc) SELECT " + pid + ",'events.product'," + Sanitizer.Ts(MetricsStore.Hour(DateTime.UtcNow)) + "," + Sanitizer.Str(JsonSerializer.Serialize(dimensions)) + "::jsonb,1,1,'{}'::jsonb," + now + " FROM claimed ON CONFLICT(propertyid,metric,bucketutc,dimensions) DO UPDATE SET value=metricseries.value+1,samples=metricseries.samples+1,computedutc=EXCLUDED.computedutc;";
            return _Database.ExecuteQueryAsync(sql, false, token);
        }
    }
}
