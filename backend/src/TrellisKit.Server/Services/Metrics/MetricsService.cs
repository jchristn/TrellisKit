namespace TrellisKit.Server.Services.Metrics
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Metrics;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Server.Services.Usage;

    /// <summary>Property-scoped metric querying and producer orchestration. Dashboards read aggregates only.</summary>
    public sealed class MetricsService
    {
        private readonly ApplicationServices _Services;
        private readonly string _Holder = Guid.NewGuid().ToString("N");
        private readonly SemaphoreSlim _Pass = new SemaphoreSlim(1,1);
        private readonly List<Func<DateTime,CancellationToken,Task<IReadOnlyList<MetricObservation>>>> _Snapshots = new List<Func<DateTime,CancellationToken,Task<IReadOnlyList<MetricObservation>>>>();
        /// <summary>Property-local declarations.</summary>
        public MetricRegistry Registry { get; }
        /// <summary>Property-scoped aggregate store.</summary>
        public MetricsStore Store { get; }
        /// <summary>Property-owned configuration.</summary>
        public PropertyMetricsConfiguration Configuration { get; }
        /// <summary>Create metrics for this property.</summary>
        public MetricsService(ApplicationServices services)
        {
            _Services=services??throw new ArgumentNullException(nameof(services));
            Configuration=services.Property.Property.Metrics??new PropertyMetricsConfiguration();
            Registry=new MetricRegistry(Configuration);
            Store=new MetricsStore(services.Database);
        }
        /// <summary>Register a trusted extension snapshot producer, before workers start. At most 100 producers.</summary>
        public void RegisterSnapshot(Func<DateTime,CancellationToken,Task<IReadOnlyList<MetricObservation>>> producer)
        {
            ArgumentNullException.ThrowIfNull(producer);
            if(_Snapshots.Count>=100)throw new InvalidOperationException("At most 100 snapshot producers are allowed.");
            _Snapshots.Add(producer);
        }
        /// <summary>Record a trusted server-side observation after validating the declaration and dimensions.</summary>
        public Task RecordAsync(MetricObservation observation,CancellationToken token=default)
        {
            ArgumentNullException.ThrowIfNull(observation);
            MetricDefinition definition=Require(observation.Metric);
            if(observation.Dimensions.Count>8||observation.Dimensions.Any(d=>!definition.Groupings.Contains(d.Key)||d.Value==null||d.Value.Length>100))throw Invalid("Undeclared or oversized metric dimension.");
            return Store.RecordAsync(observation,definition,token);
        }
        /// <summary>Record a worker pass. Errors in observability never change the worker's result.</summary>
        public async Task RecordWorkerAsync(WorkerPassRecord pass,CancellationToken token=default)
        {
            if(!Configuration.Enabled)return;
            try { await Store.WorkerAsync(pass,token).ConfigureAwait(false); }
            catch(Exception e) when(e is not OperationCanceledException) { _Services.Logger.Warn("[Metrics] worker record failed: "+e.GetType().Name); }
        }
        /// <summary>Record a declared operational annotation, idempotently.</summary>
        public Task AnnotateAsync(MetricAnnotation annotation,CancellationToken token=default)
        {
            if(annotation.Label.Length>160||annotation.Kind.Length>64||annotation.Id.Length>160)throw Invalid("Annotation fields exceed their bounds.");
            return Store.AnnotateAsync(annotation,token);
        }
        /// <summary>Run bounded rollups and snapshots; no paid model calls or external sends occur during aggregation.</summary>
        public async Task<int> RunPassAsync(DateTime now,CancellationToken token=default)
        {
            if(!Configuration.Enabled)return 0;
            await _Pass.WaitAsync(token).ConfigureAwait(false);
            bool leased = false;
            try
            {
                leased = await _Services.Database.JobLeases.TryAcquireAsync("metrics.rollup", _Holder, now, TimeSpan.FromMinutes(10), token).ConfigureAwait(false);
                if (!leased) return 0;
                await EvaluateAlertsAsync(now, token).ConfigureAwait(false);
                DateTime? watermark=await Store.CompleteThroughAsync(token).ConfigureAwait(false);
                DateTime from=watermark?.AddHours(-1)??now.AddHours(-24);
                if(from<now.AddHours(-47))from=now.AddHours(-47);
                MetricsRollupStore rollup=new MetricsRollupStore(_Services.Database);
                int count=await rollup.RollupAsync(from,now,token).ConfigureAwait(false);
                UsageZone snapshotZone=await _Services.Usage.ZoneAsync(token).ConfigureAwait(false);
                count+=await rollup.SnapshotAsync(now,token,snapshotZone.Id,snapshotZone.WeekStart).ConfigureAwait(false);
                foreach(Func<DateTime,CancellationToken,Task<IReadOnlyList<MetricObservation>>> producer in _Snapshots)
                {
                    IReadOnlyList<MetricObservation> values=await producer(now,token).ConfigureAwait(false);
                    if(values.Count>1000)throw new InvalidOperationException("A snapshot producer returned more than 1000 values.");
                    foreach(MetricObservation value in values) { if(Require(value.Metric).Kind!="Gauge")throw new InvalidOperationException("Snapshot producers require gauge metrics.");await RecordAsync(value,token).ConfigureAwait(false);count++; }
                }
                await Store.CompleteAsync(now,token).ConfigureAwait(false);
                await Store.PruneAsync(now.AddDays(-Configuration.RetentionDays),now.AddDays(-Configuration.WorkerRetentionDays),token).ConfigureAwait(false);
                foreach(MetricDefinition definition in Registry.Definitions.Where(d=>d.Source!="Usage"&&d.RetentionDays<Configuration.RetentionDays))
                    await Store.PruneMetricAsync(definition.Key,now.AddDays(-definition.RetentionDays),token).ConfigureAwait(false);
                return count;
            }
            finally { try { if (leased) await _Services.Database.JobLeases.ReleaseAsync("metrics.rollup", _Holder, CancellationToken.None).ConfigureAwait(false); } finally { _Pass.Release(); } }
        }
        /// <summary>Query at most 2000 buckets and 20 groups plus Other. Only administrators may call this entry point.</summary>
        public async Task<MetricResult> QueryAsync(ContentActor actor,MetricQuery query,CancellationToken token=default)
        {
            if(!actor.IsSystemAdmin)throw ApiErrorException.Forbidden("Administrator access is required for metrics.");
            Validate(query);
            MetricDefinition definition=Require(query.Metric);
            UsageZone zone=Operational(definition)?UsageZone.Utc:await _Services.Usage.ZoneAsync(token).ConfigureAwait(false);
            MetricResult result=new MetricResult{Metric=query.Metric,Unit=definition.Unit,TimeZone=zone.Id,CompleteThroughUtc=await Store.CompleteThroughAsync(token).ConfigureAwait(false)};
            result.Series=await SeriesAsync(query,definition,zone,token).ConfigureAwait(false);
            if(query.Compare)
            {
                TimeSpan duration=query.To-query.From;
                MetricQuery previous=new MetricQuery{Metric=query.Metric,From=query.From-duration,To=query.From,Bucket=query.Bucket,GroupBy=query.GroupBy,Filters=query.Filters,Percentile=query.Percentile,Compare=false};
                result.Previous=await SeriesAsync(previous,definition,zone,token).ConfigureAwait(false);
            }
            result.Annotations=await Store.AnnotationsAsync(query.From,query.To,token).ConfigureAwait(false);
            return result;
        }
        /// <summary>Validate bounds, exact dimension allowlists, finite percentile, and UTC dates before any storage access.</summary>
        public void Validate(MetricQuery query)
        {
            ArgumentNullException.ThrowIfNull(query);
            MetricDefinition definition=Require(query.Metric);
            if(query.From.Kind!=DateTimeKind.Utc||query.To.Kind!=DateTimeKind.Utc||query.To<=query.From||(query.To-query.From).TotalDays>1830)throw Invalid("Use an increasing UTC range of at most 1830 days.");
            if(!definition.Buckets.Contains(query.Bucket))throw Invalid("Unsupported bucket for this metric.");
            double hours=query.Bucket=="Hour"?1:query.Bucket=="Day"?24:168;
            if(Math.Ceiling((query.To-query.From).TotalHours/hours)+1>2000)throw Invalid("At most 2000 buckets may be requested.");
            if(query.GroupBy!=null&&!definition.Groupings.Contains(query.GroupBy))throw Invalid("Unsupported grouping.");
            if(query.Filters==null||query.Filters.Count>8||query.Filters.Any(f=>!definition.Groupings.Contains(f.Key)||f.Value==null||f.Value.Length>100))throw Invalid("Unsupported metric filter.");
            if(!Double.IsFinite(query.Percentile)||query.Percentile<=0||query.Percentile>1)throw Invalid("Percentile must be in (0,1].");
        }
        private async Task<List<MetricSeries>> SeriesAsync(MetricQuery query,MetricDefinition definition,UsageZone zone,CancellationToken token)
        {
            List<MetricPoint> points=Points(query,zone);
            if(definition.Source=="Computed")return await ComputedAsync(query,points,zone,token).ConfigureAwait(false);
            if(definition.Source=="Usage")
            {
                List<UsageBucketRange> buckets=points.Select((p,i)=>new UsageBucketRange{Key=i.ToString(CultureInfo.InvariantCulture),StartUtc=p.StartUtc,EndUtc=p.EndUtc}).ToList();
                List<UsageAggregate> values=new List<UsageAggregate>();
                foreach(UsageBucketRange[] batch in buckets.Chunk(1000)) values.AddRange(query.Bucket=="Hour"?await _Services.Database.Usage.AggregateHourlyAsync(batch,new[]{query.Metric},token).ConfigureAwait(false):await _Services.Database.Usage.AggregateDailyAsync(batch,new[]{query.Metric},token).ConfigureAwait(false));
                UsageState? state=await _Services.Database.Usage.ReadStateAsync(token).ConfigureAwait(false);
                foreach(UsageAggregate value in values)
                {
                    MetricPoint point=points[Int32.Parse(value.Key,CultureInfo.InvariantCulture)];point.Value=definition.Kind=="Count"?value.Sum:value.Last;point.Samples=value.Rows;point.Complete=point.EndUtc<=state?.LastRollupUtc;
                }
                return new List<MetricSeries>{new MetricSeries{Key="total",Points=points}};
            }
            MetricQuery storage=new MetricQuery{Metric=query.Metric,From=points[0].StartUtc,To=query.To,Bucket=query.Bucket,GroupBy=query.GroupBy,Filters=query.Filters,Percentile=query.Percentile};
            DataTable rows=await Store.QueryAsync(storage,definition,zone.Id,token,zone.WeekStart).ConfigureAwait(false);
            if(rows.Rows.Count>42000)throw Invalid("Metric result exceeds its bounds.");
            Dictionary<string,MetricSeries> series=new Dictionary<string,MetricSeries>(StringComparer.Ordinal);
            foreach(DataRow row in rows.Rows)
            {
                string key=RowReader.GetString(row,"label");
                if(!series.TryGetValue(key,out MetricSeries? item)){item=new MetricSeries{Key=key,Points=Points(query,zone)};series.Add(key,item);}
                DateTime start=RowReader.GetDateTime(row,"bucket");MetricPoint? point=item.Points.FirstOrDefault(p=>p.StartUtc==start);if(point==null)continue;
                point.Samples=RowReader.GetLong(row,"samples");
                point.Suppressed=definition.MemberSensitive&&(query.GroupBy!=null||query.Filters.Count>0)&&point.Samples<5;
                if(!point.Suppressed)
                {
                    point.Value=definition.Kind=="Distribution"?MetricHistogram.Percentile(JsonSerializer.Deserialize<Dictionary<int,long>>(RowReader.GetString(row,"histogram"))??new Dictionary<int,long>(),query.Percentile):RowReader.GetDouble(row,"value");
                }
                else point.Samples=0;
                point.Complete=RowReader.GetBool(row,"complete")&&point.EndUtc<=query.To;
            }
            if(series.Count==0)series.Add("total",new MetricSeries{Key="total",Points=points});
            return series.Values.ToList();
        }
        private async Task<List<MetricSeries>> ComputedAsync(MetricQuery query,List<MetricPoint> points,UsageZone zone,CancellationToken token)
        {
            MetricQuery Copy(string metric) { return new MetricQuery{Metric=metric,From=query.From,To=query.To,Bucket=query.Bucket,GroupBy=query.GroupBy,Filters=new Dictionary<string,string>(query.Filters),Compare=false}; }
            if(query.Metric=="interactions.activity"||query.Metric=="storage.classes")
            {
                bool storage=query.Metric=="storage.classes";
                string dimension=storage?"class":"kind";
                string[] metrics=storage?UsageMeters.StorageClasses.ToArray():new[]{"posts.new","comments.new","reactions.new","follows.new"};
                if(query.GroupBy==null&&!query.Filters.ContainsKey(dimension))metrics=new[]{storage?"storage.total":"interactions.new"};
                List<MetricSeries> combined=new List<MetricSeries>();
                foreach(string metric in metrics)
                {
                    string label=metric.Split('.')[storage?1:0];
                    if(query.Filters.TryGetValue(dimension,out string? selected)&&selected!=label)continue;
                    MetricQuery child=Copy(metric);child.GroupBy=null;child.Filters.Clear();
                    List<MetricSeries> values=await SeriesAsync(child,Require(metric),zone,token).ConfigureAwait(false);
                    foreach(MetricSeries value in values){value.Key=query.GroupBy==null?"total":label;combined.Add(value);}
                }
                return combined;
            }
            if(query.Metric=="members.sources")
            {
                MetricQuery source=Copy("events.product");source.Filters["event"]="signup.registered";
                return await SeriesAsync(source,Require(source.Metric),zone,token).ConfigureAwait(false);
            }
            (string numerator,string denominator,double scale)=query.Metric switch
            {
                "members.stickiness" => ("users.active","users.activeMonthly",100),
                "engagement.perActive" => ("interactions.new","users.active",1),
                "library.completionRate" => ("library.completions","library.plays",100),
                "events.checkInRate" => ("events.attendance","events.attendance",100),
                "events.noShowRate" => ("events.attendance","events.attendance",100),
                "ai.costPerSuccess" => ("ai.cost","ai.calls",1),
                _ => ("requests.count","requests.count",100)
            };
            MetricQuery n=Copy(numerator),d=Copy(denominator);
            if(query.Metric=="requests.errorRate")n.Filters["statusClass"]="5xx";
            if(query.Metric=="events.noShowRate")n.Filters["status"]="NoShow";
            if(query.Metric=="events.checkInRate")n.Filters["status"]="CheckedIn";
            if(query.Metric=="ai.costPerSuccess")d.Filters["outcome"]="Ok";
            List<MetricSeries> top=await SeriesAsync(n,Require(numerator),zone,token).ConfigureAwait(false),bottom=await SeriesAsync(d,Require(denominator),zone,token).ConfigureAwait(false);
            // A known denominator with no matching events is a measured zero. An absent denominator remains a gap.
            foreach(MetricSeries series in bottom)
            {
                MetricSeries? dividend=top.FirstOrDefault(b=>b.Key==series.Key);
                for(int i=0;i<series.Points.Count;i++)
                {
                    MetricPoint p=series.Points[i];MetricPoint? other=dividend?.Points.ElementAtOrDefault(i);
                    bool suppressed=p.Suppressed||other?.Suppressed==true;
                    p.Value=!suppressed&&p.Value>0?(other?.Value??(query.Metric=="members.stickiness"||query.Metric=="engagement.perActive"?(double?)null:0))/p.Value*scale:null;
                    p.Suppressed=suppressed;p.Complete=p.Complete&&(other?.Value==null||other.Complete);
                }
            }
            return bottom;
        }

        /// <summary>Evaluate default and property thresholds with persisted once-per-transition delivery.</summary>
        public async Task<int> EvaluateAlertsAsync(DateTime now,CancellationToken token=default)
        {
            if(!Configuration.AlertsEnabled)return 0;
            UsageZone alertZone=await _Services.Usage.ZoneAsync(token).ConfigureAwait(false);
            Dictionary<string,bool> defaults=await new MetricAlertStore(_Services.Database).EvaluateAsync(now,Configuration.StorageQuotaBytes,Configuration.AlertThresholds,token,alertZone.Id).ConfigureAwait(false);
            int notified=0;
            foreach(KeyValuePair<string,bool> condition in defaults)
            {
                int consecutive=condition.Key=="errorRate"||condition.Key=="latency"?Configuration.AlertThresholds.SustainedMinutes:1;
                if(await Store.AlertAsync("default."+condition.Key,condition.Value,consecutive,now,token).ConfigureAwait(false)) { await NotifyAlertAsync(condition.Key,"An operational metric crossed its threshold.",now,token).ConfigureAwait(false);notified++; }
            }
            foreach(MetricAlertRule rule in Configuration.Alerts.Where(r=>r.Enabled))
            {
                if(rule.WindowMinutes<1||rule.WindowMinutes>10080||rule.Consecutive<1||rule.Consecutive>100||!Double.IsFinite(rule.Threshold)||!new[]{"Above","Below"}.Contains(rule.Operator))throw Invalid("Invalid metric alert rule.");
                MetricResult values=await QueryAsync(new ContentActor{IsSystemAdmin=true},new MetricQuery{Metric=rule.Metric,From=now.AddMinutes(-rule.WindowMinutes),To=now,Bucket="Hour",GroupBy=rule.GroupBy,Compare=false},token).ConfigureAwait(false);
                bool breached=values.Series.Any(s=>s.Points.Where(p=>p.Value.HasValue).Any(p=>rule.Operator=="Above"?p.Value>rule.Threshold:p.Value<rule.Threshold));
                if(await Store.AlertAsync("custom."+rule.Key,breached,rule.Consecutive,now,token).ConfigureAwait(false)) { await NotifyAlertAsync(rule.Metric,"The configured "+rule.Operator.ToLowerInvariant()+" threshold was crossed.",now,token).ConfigureAwait(false);notified++; }
            }
            return notified;
        }
        private async Task NotifyAlertAsync(string key,string detail,DateTime now,CancellationToken token)
        {
            List<RoleAssignment> assignments=await _Services.Database.RoleAssignments.EnumerateByRoleAsync(RoleEnum.SystemAdmin,100,token).ConfigureAwait(false);
            string id="metric:"+key+":"+now.ToString("yyyyMMddHHmm",CultureInfo.InvariantCulture);
            NotificationDraft draft=new NotificationDraft{Type=NotificationTypeEnum.MetricAlert,TargetType=TargetTypeEnum.Notification,TargetId=id,TitleText="Metric alert: "+key,Body=detail,Path="/admin/metrics/health",EventUtc=now,QueuePush=false};
            await _Services.Notifications.CreateForRecipientsAsync(draft,assignments.Select(a=>a.UserId).Distinct(StringComparer.Ordinal),token).ConfigureAwait(false);
            await _Services.Email.NotifyStaffAsync("staff_metric_alert",id,token,("metric",key),("detail",detail),("link",_Services.Settings.Web.PublicBaseUrl.TrimEnd('/')+"/admin/metrics/health")).ConfigureAwait(false);
        }
        private MetricDefinition Require(string key) { return Registry.Find(key)??throw new ApiErrorException(400,"UnknownMetric","The metric is not registered."); }
        private static bool Operational(MetricDefinition definition) { return definition.Key.StartsWith("requests.",StringComparison.Ordinal)||definition.Key.StartsWith("workers.",StringComparison.Ordinal)||definition.Key.StartsWith("ai.",StringComparison.Ordinal)||definition.Key.StartsWith("dependencies.",StringComparison.Ordinal)||definition.Key=="realtime.connections"; }
        private static ApiErrorException Invalid(string message) { return new ApiErrorException(400,"ValidationFailed",message); }
        private static List<MetricPoint> Points(MetricQuery query,UsageZone calendar)
        {
            List<MetricPoint> result=new List<MetricPoint>();
            // UTC hours are unambiguous even when local clocks repeat or skip an hour.
            if(query.Bucket=="Hour")
            {
                for(DateTime start=MetricsStore.Hour(query.From);start<query.To;start=start.AddHours(1))
                    result.Add(new MetricPoint{StartUtc=start,EndUtc=start.AddHours(1)});
            }
            else
            {
                DateOnly date=calendar.LocalDate(query.From);
                if(query.Bucket=="Week")date=calendar.WeekStartOf(date);
                for(int i=0;i<=2000;i++)
                {
                    DateOnly next=date.AddDays(query.Bucket=="Week"?7:1);
                    DateTime start=calendar.Midnight(date),end=calendar.Midnight(next);
                    if(start>=query.To)break;
                    result.Add(new MetricPoint{StartUtc=start,EndUtc=end});date=next;
                }
            }
            if(result.Count>2000)throw Invalid("At most 2000 buckets may be requested.");
            return result;
        }
    }
}
