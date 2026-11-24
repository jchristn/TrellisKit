namespace TrellisKit.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Postgresql;
    using TrellisKit.Core.Metrics;

    /// <summary>Persistent aggregate metrics. All reads and writes use a property-bound driver and explicit property predicates.</summary>
    public sealed class MetricsStore
    {
        private readonly DatabaseDriverBase _Database;
        private readonly string _Property;
        /// <summary>Create storage for a single property. Throws for an unbound driver.</summary>
        public MetricsStore(DatabaseDriverBase database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Property = database.BoundPropertyId ?? throw new ArgumentException("Metrics need a property-bound database.", nameof(database));
        }
        private string Scope => "propertyid = " + Sanitizer.Str(_Property);
        private string Pid => Sanitizer.Str(_Property);
        /// <summary>Persist an aggregate sample into its UTC hour. Gauges replace; counts add; distributions merge their bins.</summary>
        public Task RecordAsync(MetricObservation observation, MetricDefinition definition, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(observation);
            ArgumentNullException.ThrowIfNull(definition);
            if (!Double.IsFinite(observation.Value) || observation.Samples < 1) throw new ArgumentOutOfRangeException(nameof(observation));
            DateTime hour = Hour(observation.OccurredUtc);
            string dimensions = Sanitizer.Str(JsonSerializer.Serialize(observation.Dimensions)) + "::jsonb";
            string histogram = definition.Kind == "Distribution" ? "jsonb_build_object(" + Sanitizer.Str(MetricHistogram.Bin(observation.Value).ToString(CultureInfo.InvariantCulture)) + "," + observation.Samples + ")" : "'{}'::jsonb";
            string value = Sanitizer.Num(observation.Value), samples = Sanitizer.Num(observation.Samples);
            string update = definition.Kind == "Gauge" || definition.Kind == "Distinct" ? "value=EXCLUDED.value,samples=EXCLUDED.samples" : "value=metricseries.value+EXCLUDED.value,samples=metricseries.samples+EXCLUDED.samples";
            string merge = "(SELECT COALESCE(jsonb_object_agg(k,n),'{}'::jsonb) FROM (SELECT k,SUM(n) n FROM (SELECT key k,value::bigint n FROM jsonb_each_text(metricseries.histogram) UNION ALL SELECT key,value::bigint FROM jsonb_each_text(EXCLUDED.histogram)) bins GROUP BY k) totals)";
            return _Database.ExecuteQueryAsync("INSERT INTO metricseries(propertyid,metric,bucketutc,dimensions,value,samples,histogram,computedutc) VALUES(" + Pid + "," + Sanitizer.Str(definition.Key) + "," + Sanitizer.Ts(hour) + "," + dimensions + "," + value + "," + samples + "," + histogram + "," + Sanitizer.Ts(DateTime.UtcNow) + ") ON CONFLICT(propertyid,metric,bucketutc,dimensions) DO UPDATE SET " + update + ",histogram=" + merge + ",computedutc=EXCLUDED.computedutc;", false, token);
        }
        /// <summary>Read at most 2000 buckets and 20 groups plus Other, aggregating only stored rollups. Filters are exact declared dimension values.</summary>
        public Task<DataTable> QueryAsync(MetricQuery query, MetricDefinition definition, string zone, CancellationToken token = default, DayOfWeek weekStart = DayOfWeek.Monday)
        {
            ArgumentNullException.ThrowIfNull(query);
            string group = query.GroupBy == null ? "'total'" : "COALESCE(dimensions->>" + Sanitizer.Str(query.GroupBy) + ",'unknown')";
            int shift = ((int)weekStart + 6) % 7;
            string local = "bucketutc AT TIME ZONE " + Sanitizer.Str(zone);
            string bucket = query.Bucket == "Hour" ? "date_trunc('hour',bucketutc AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'" : query.Bucket == "Week" ? "(date_trunc('week'," + local + " - interval '" + shift + " days') + interval '" + shift + " days') AT TIME ZONE " + Sanitizer.Str(zone) : "date_trunc('day'," + local + ") AT TIME ZONE " + Sanitizer.Str(zone);
            string where = Scope + " AND metric=" + Sanitizer.Str(query.Metric) + " AND bucketutc >= " + Sanitizer.Ts(query.From) + " AND bucketutc < " + Sanitizer.Ts(query.To);
            foreach (KeyValuePair<string,string> filter in query.Filters) where += " AND dimensions->>" + Sanitizer.Str(filter.Key) + "=" + Sanitizer.Str(filter.Value);
            bool gauge = definition.Kind == "Gauge" || definition.Kind == "Distinct";
            string samples = gauge ? (definition.MemberSensitive ? "MIN(samples) FILTER (WHERE bucketutc = latestutc)" : "SUM(samples) FILTER (WHERE bucketutc = latestutc)") : "SUM(samples)";
            string value = gauge ? "SUM(value) FILTER (WHERE bucketutc = latestutc)" : "SUM(value)";
            // Group ranking is computed before folding: dropped groups remain represented by Other.
            string sql = "WITH selected AS (SELECT *," + group + " g FROM metricseries WHERE " + where + "), topgroups AS (SELECT g FROM selected GROUP BY g ORDER BY SUM(samples) DESC,g LIMIT 20), folded AS (SELECT *,CASE WHEN g IN (SELECT g FROM topgroups) THEN g ELSE 'Other' END label FROM selected), dated AS (SELECT *,MAX(bucketutc) OVER(PARTITION BY " + bucket + ",label) latestutc FROM folded), totals AS (SELECT " + bucket + " bucket,label," + value + " value," + samples + " samples,bool_and(complete) complete FROM dated GROUP BY 1,2), bins AS (SELECT " + bucket + " bucket,label,h.key,SUM(h.value::bigint) n FROM folded CROSS JOIN LATERAL jsonb_each_text(histogram) h GROUP BY 1,2,3), sketches AS (SELECT bucket,label,jsonb_object_agg(key,n) histogram FROM bins GROUP BY 1,2) SELECT t.*,COALESCE(s.histogram,'{}'::jsonb)::text histogram FROM totals t LEFT JOIN sketches s USING(bucket,label) ORDER BY bucket,label LIMIT 42001;";
            return _Database.ExecuteQueryAsync(sql, false, token);
        }
        /// <summary>Store a marked moment once by id.</summary>
        public Task AnnotateAsync(MetricAnnotation annotation, CancellationToken token = default)
        {
            return _Database.ExecuteQueryAsync("INSERT INTO metricannotations(propertyid,id,kind,label,occurredutc) VALUES(" + Pid + "," + Sanitizer.Str(annotation.Id) + "," + Sanitizer.Str(annotation.Kind) + "," + Sanitizer.Str(annotation.Label) + "," + Sanitizer.Ts(annotation.OccurredUtc) + ") ON CONFLICT(propertyid,id) DO NOTHING;", false, token);
        }
        /// <summary>At most 200 annotations in a half-open window.</summary>
        public async Task<List<MetricAnnotation>> AnnotationsAsync(DateTime from, DateTime to, CancellationToken token = default)
        {
            DataTable rows = await _Database.ExecuteQueryAsync("SELECT * FROM metricannotations WHERE " + Scope + " AND occurredutc >= " + Sanitizer.Ts(from) + " AND occurredutc < " + Sanitizer.Ts(to) + " ORDER BY occurredutc DESC,id LIMIT 200;",false,token).ConfigureAwait(false);
            return rows.Rows.Cast<DataRow>().Select(r => new MetricAnnotation { Id=RowReader.GetString(r,"id"),Kind=RowReader.GetString(r,"kind"),Label=RowReader.GetString(r,"label"),OccurredUtc=RowReader.GetDateTime(r,"occurredutc") }).ToList();
        }
        /// <summary>Record a worker pass, idempotently.</summary>
        public Task WorkerAsync(WorkerPassRecord pass, CancellationToken token = default)
        {
            return _Database.ExecuteQueryAsync("INSERT INTO metricworkerruns(propertyid,id,worker,startedutc,endedutc,outcome,items,error,intervalseconds) VALUES("+Pid+","+Sanitizer.Str(pass.Id)+","+Sanitizer.Str(pass.Worker)+","+Sanitizer.Ts(pass.StartedUtc)+","+Sanitizer.Ts(pass.EndedUtc)+","+Sanitizer.Str(pass.Outcome)+","+Sanitizer.Num(pass.ItemsProcessed)+","+Sanitizer.Str(pass.Error)+","+Sanitizer.Num(pass.IntervalSeconds)+") ON CONFLICT(propertyid,id) DO NOTHING;",false,token);
        }
        /// <summary>Read at most 2000 recent worker passes.</summary>
        public async Task<List<WorkerPassRecord>> WorkersAsync(DateTime from, DateTime to, CancellationToken token = default)
        {
            DataTable rows=await _Database.ExecuteQueryAsync("SELECT * FROM metricworkerruns WHERE "+Scope+" AND startedutc >= "+Sanitizer.Ts(from)+" AND startedutc < "+Sanitizer.Ts(to)+" ORDER BY startedutc DESC,id LIMIT 2000;",false,token).ConfigureAwait(false);
            return rows.Rows.Cast<DataRow>().Select(r=>new WorkerPassRecord {Id=RowReader.GetString(r,"id"),Worker=RowReader.GetString(r,"worker"),StartedUtc=RowReader.GetDateTime(r,"startedutc"),EndedUtc=RowReader.GetDateTime(r,"endedutc"),Outcome=RowReader.GetString(r,"outcome"),ItemsProcessed=RowReader.GetInt(r,"items"),Error=RowReader.GetNullableString(r,"error"),IntervalSeconds=RowReader.GetDouble(r,"intervalseconds")}).ToList();
        }
        /// <summary>Latest complete rollup watermark, or null before the first pass.</summary>
        public async Task<DateTime?> CompleteThroughAsync(CancellationToken token = default)
        {
            DataTable rows=await _Database.ExecuteQueryAsync("SELECT completethroughutc FROM metricstate WHERE "+Scope+";",false,token).ConfigureAwait(false);
            return rows.Rows.Count==0?null:RowReader.GetNullableDateTime(rows.Rows[0],"completethroughutc");
        }
        /// <summary>Mark closed hours complete and persist the watermark. Existing gaps remain absent.</summary>
        public Task CompleteAsync(DateTime now,CancellationToken token=default)
        {
            DateTime hour=Hour(now);
            return _Database.ExecuteQueriesAsync(new[]{"UPDATE metricseries SET complete=TRUE WHERE "+Scope+" AND bucketutc < "+Sanitizer.Ts(hour)+" AND NOT complete;","INSERT INTO metricstate(propertyid,completethroughutc,updatedutc) VALUES("+Pid+","+Sanitizer.Ts(hour)+","+Sanitizer.Ts(now)+") ON CONFLICT(propertyid) DO UPDATE SET completethroughutc=EXCLUDED.completethroughutc,updatedutc=EXCLUDED.updatedutc;"},true,token);
        }
        /// <summary>Delete expired data in bounded batches, independently for aggregate and worker retention.</summary>
        public Task PruneAsync(DateTime seriesBefore,DateTime workersBefore,CancellationToken token=default)
        {
            return _Database.ExecuteQueriesAsync(new[]{Prune("metricseries","bucketutc",seriesBefore),Prune("metricannotations","occurredutc",seriesBefore),Prune("metricworkerruns","startedutc",workersBefore)},true,token);
        }
        /// <summary>Apply shorter metric-specific retention without deleting another property's data.</summary>
        public Task PruneMetricAsync(string metric,DateTime before,CancellationToken token=default)
        {
            return _Database.ExecuteQueryAsync("DELETE FROM metricseries WHERE "+Scope+" AND ctid IN (SELECT ctid FROM metricseries WHERE "+Scope+" AND metric="+Sanitizer.Str(metric)+" AND bucketutc < "+Sanitizer.Ts(before)+" LIMIT 5000);",false,token);
        }
        private string Prune(string table,string column,DateTime before) { return "DELETE FROM "+table+" WHERE "+Scope+" AND ctid IN (SELECT ctid FROM "+table+" WHERE "+Scope+" AND "+column+" < "+Sanitizer.Ts(before)+" LIMIT 5000);"; }
        /// <summary>Advance a threshold streak once per minute and claim a new alert transition atomically.</summary>
        public async Task<bool> AlertAsync(string key,bool breached,int consecutive,DateTime now,CancellationToken token=default)
        {
            DateTime minute=new DateTime(now.Ticks/TimeSpan.TicksPerMinute*TimeSpan.TicksPerMinute,DateTimeKind.Utc);
            string stamp=Sanitizer.Ts(minute), condition=Sanitizer.Bool(breached);
            string next="CASE WHEN "+condition+" THEN CASE WHEN metricalertstate.updatedutc="+Sanitizer.Ts(minute.AddMinutes(-1))+" THEN metricalertstate.streak+1 ELSE 1 END ELSE 0 END";
            string active="("+next+">="+Math.Clamp(consecutive,1,100)+")";
            DataTable rows=await _Database.ExecuteQueryAsync("INSERT INTO metricalertstate(propertyid,key,streak,active,notifiedutc,updatedutc) VALUES("+Pid+","+Sanitizer.Str(key)+","+(breached?1:0)+","+Sanitizer.Bool(breached&&consecutive<=1)+","+(breached&&consecutive<=1?stamp:"NULL")+","+stamp+") ON CONFLICT(propertyid,key) DO UPDATE SET streak="+next+",active="+active+",notifiedutc=CASE WHEN "+active+" AND NOT metricalertstate.active THEN "+stamp+" ELSE metricalertstate.notifiedutc END,updatedutc="+stamp+" WHERE metricalertstate.updatedutc < "+stamp+" RETURNING active,notifiedutc;",false,token).ConfigureAwait(false);
            return rows.Rows.Count>0&&RowReader.GetBool(rows.Rows[0],"active")&&RowReader.GetNullableDateTime(rows.Rows[0],"notifiedutc")==minute;
        }
        /// <summary>UTC hour containing a timestamp.</summary>
        public static DateTime Hour(DateTime utc) { return new DateTime(utc.ToUniversalTime().Ticks / TimeSpan.TicksPerHour * TimeSpan.TicksPerHour,DateTimeKind.Utc); }
    }
}
