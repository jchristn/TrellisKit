namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Metrics;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Metrics;

    /// <summary>Metrics isolation, bound enforcement, privacy, mergeable percentiles, idempotent rollups, and alert transitions.</summary>
    public static class MetricsSuite
    {
        /// <summary>Suite identifier.</summary>
        public const string Id="Metrics";
        /// <summary>Build neutral-property cases.</summary>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id,"Generic metrics",new[]
            {
                SuiteCases.Unit(Id,"RegistryAndSketch","Property declarations cannot replace engine definitions and weighted sketches merge correctly",ct=>
                {
                    MetricRegistry registry=new MetricRegistry(new PropertyMetricsConfiguration());
                    Check.True(registry.Definitions.Any(m=>m.Key=="users.new"),"Usage meters are registered");
                    Check.Throws<ArgumentException>(()=>registry.Register(new MetricDefinition{Key="users.new"}),"Engine keys cannot be replaced");
                    Check.Equal(365,new MetricRegistry(new PropertyMetricsConfiguration{RetentionDays=365}).Find("requests.count")!.RetentionDays,"Built-in aggregate retention follows property configuration");
                    Dictionary<int,long> bins=new Dictionary<int,long>{{MetricHistogram.Bin(10),90}};
                    MetricHistogram.Merge(bins,new Dictionary<int,long>{{MetricHistogram.Bin(1000),10}});
                    Check.True(MetricHistogram.Percentile(bins,.5)<12,"Weighted median");
                    Check.True(MetricHistogram.Percentile(bins,.95)>=1000,"Tail survives aggregation");
                    Check.True(MetricRequestDimensions.Route("GET","/api/v1/members/private-person@example.test")=="GET /api/v1/members/{id}","Raw identifiers never become metric dimensions");
                    return Task.CompletedTask;
                }),
                SuiteCases.Http(Id,"BoundsAndAuthorization","Metric access is admin-only and rejects unsupported grouping and excessive ranges",async(f,ct)=>
                {
                    TestMember member=await Api.MemberAsync(f,ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f,HttpMethod.Get,"/api/v1/admin/metrics",member.AccessToken,null,HttpStatusCode.Forbidden,ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f,HttpMethod.Get,"/api/v1/admin/metrics",HttpFixture.AdminToken,null,HttpStatusCode.OK,ct).ConfigureAwait(false);
                    DateTime now=DateTime.UtcNow;
                    await Check.ThrowsAsync<ApiErrorException>(()=>f.Services.Metrics.QueryAsync(new ContentActor{IsSystemAdmin=true},new MetricQuery{Metric="requests.count",From=now.AddDays(-100),To=now,Bucket="Hour"},ct),"Too many buckets").ConfigureAwait(false);
                    await Check.ThrowsAsync<ApiErrorException>(()=>f.Services.Metrics.QueryAsync(new ContentActor{IsSystemAdmin=true},new MetricQuery{Metric="requests.count",From=now.AddDays(-1),To=now,GroupBy="email"},ct),"Undeclared dimensions").ConfigureAwait(false);
                }),
                SuiteCases.Http(Id,"GroupsGapsAndIsolation","Twenty largest groups plus Other preserve totals, absent hours stay gaps, and another property never leaks",async(f,ct)=>
                {
                    DateTime start=MetricsStore.Hour(DateTime.UtcNow.AddDays(-1));
                    MetricDefinition definition=f.Services.Metrics.Registry.Find("requests.count")!;
                    for(int i=0;i<25;i++)await f.Services.Metrics.RecordAsync(new MetricObservation{Metric=definition.Key,OccurredUtc=start,Value=i+1,Dimensions=new Dictionary<string,string>{{"route","route"+i}}},ct).ConfigureAwait(false);
                    MetricsStore other=new MetricsStore(f.Services.Database.Deployment.ForProperty(PropertyIds.Generate()));
                    await other.RecordAsync(new MetricObservation{Metric=definition.Key,OccurredUtc=start,Value=99999,Dimensions=new Dictionary<string,string>{{"route","private"}}},definition,ct).ConfigureAwait(false);
                    MetricResult result=await f.Services.Metrics.QueryAsync(new ContentActor{IsSystemAdmin=true},new MetricQuery{Metric=definition.Key,From=start,To=start.AddHours(2),Bucket="Hour",GroupBy="route",Compare=false},ct).ConfigureAwait(false);
                    Check.Equal(21,result.Series.Count,"Top 20 plus Other");Check.True(result.Series.Any(s=>s.Key=="Other"),"Folded group");
                    Check.Equal(325.0,result.Series.Sum(s=>s.Points[0].Value??0),"No discarded or cross-property values");Check.True(result.Series.All(s=>s.Points[1].Value==null),"Honest gaps");
                }),
                SuiteCases.Http(Id,"PrivacyAndGauge","Small profile groups are suppressed and gauge totals sum the latest snapshot only",async(f,ct)=>
                {
                    DateTime start=DateTime.UtcNow.Date.AddDays(-1).AddHours(10);
                    foreach(int hour in new[]{0,1})foreach(string option in new[]{"small","large"})await f.Services.Metrics.RecordAsync(new MetricObservation{Metric="members.profile",OccurredUtc=start.AddHours(hour),Value=option=="small"?2:10, Samples=option=="small"?2:10,Dimensions=new Dictionary<string,string>{{"field","level"},{"option",option}}},ct).ConfigureAwait(false);
                    MetricResult grouped=await f.Services.Metrics.QueryAsync(new ContentActor{IsSystemAdmin=true},new MetricQuery{Metric="members.profile",From=start.Date,To=start.Date.AddDays(1),Bucket="Day",GroupBy="option",Compare=false},ct).ConfigureAwait(false);
                    Check.True(grouped.Series.Single(s=>s.Key=="small").Points[0].Suppressed,"Repeated snapshots do not bypass minimum group size");
                    MetricResult filtered=await f.Services.Metrics.QueryAsync(new ContentActor{IsSystemAdmin=true},new MetricQuery{Metric="members.profile",From=start.Date,To=start.Date.AddDays(1),Bucket="Day",Filters=new Dictionary<string,string>{{"option","small"}},Compare=false},ct).ConfigureAwait(false);
                    Check.True(filtered.Series[0].Points[0].Suppressed,"An exact filter cannot bypass small-group suppression");
                    await f.Services.Metrics.RecordAsync(new MetricObservation{Metric="members.profile",OccurredUtc=start.AddHours(1),Value=4,Samples=4,Dimensions=new Dictionary<string,string>{{"field","another"},{"option","small"}}},ct).ConfigureAwait(false);
                    MetricResult acrossFields=await f.Services.Metrics.QueryAsync(new ContentActor{IsSystemAdmin=true},new MetricQuery{Metric="members.profile",From=start.Date,To=start.Date.AddDays(1),Bucket="Day",GroupBy="option",Compare=false},ct).ConfigureAwait(false);
                    Check.True(acrossFields.Series.Single(s=>s.Key=="small").Points[0].Suppressed,"Summing overlapping field groups cannot manufacture five distinct members");
                    MetricResult total=await f.Services.Metrics.QueryAsync(new ContentActor{IsSystemAdmin=true},new MetricQuery{Metric="members.profile",From=start.Date,To=start.Date.AddDays(1),Bucket="Day",Compare=false},ct).ConfigureAwait(false);
                    Check.Equal(16.0,total.Series[0].Points[0].Value??-1,"Total sums categories once at latest timestamp");
                }),
                SuiteCases.Http(Id,"RollupAndSnapshots","Request rollups and every engine snapshot producer run against real schema and are idempotent",async(f,ct)=>
                {
                    DateTime now=MetricsStore.Hour(DateTime.UtcNow).AddMinutes(30),start=now.AddHours(-2);
                    await f.Services.Database.RequestHistory.CreateAsync(new RequestHistoryEntry{Method="GET",Path="/api/v1/health",MetricRoute="GET /api/v1/health",StatusCode=500,DurationMs=1500,CreatedUtc=start,CompletedUtc=start.AddMilliseconds(1500)},ct).ConfigureAwait(false);
                    await f.Services.Metrics.RunPassAsync(now,ct).ConfigureAwait(false);
                    await f.Services.Metrics.RunPassAsync(now,ct).ConfigureAwait(false);
                    MetricResult result=await f.Services.Metrics.QueryAsync(new ContentActor{IsSystemAdmin=true},new MetricQuery{Metric="requests.count",From=MetricsStore.Hour(start),To=MetricsStore.Hour(start).AddHours(1),Bucket="Hour",Compare=false},ct).ConfigureAwait(false);
                    Check.Equal(1.0,result.Series[0].Points[0].Value??-1,"Replay replaces instead of doubling");Check.True(result.Series[0].Points[0].Complete,"Historical bucket marked complete");
                }),
                SuiteCases.Http(Id,"CalendarAndDst","Business days and weeks use property settings while repeated DST hours remain distinct",async(f,ct)=>
                {
                    await Api.ExpectAsync(f,HttpMethod.Put,"/api/v1/admin/property/settings",HttpFixture.AdminToken,new {values=new Dictionary<string,object>{{"usage.timeZone","America/New_York"},{"usage.weekStartsOn","Sunday"}}},HttpStatusCode.OK,ct).ConfigureAwait(false);
                    DateTime start=new DateTime(2025,11,2,4,0,0,DateTimeKind.Utc);
                    foreach(int hour in new[]{1,2})await f.Services.Metrics.RecordAsync(new MetricObservation{Metric="messages.sent",OccurredUtc=start.AddHours(hour),Value=1},ct).ConfigureAwait(false);
                    MetricQuery query=new MetricQuery{Metric="messages.sent",From=start,To=start.AddHours(25),Bucket="Hour",Compare=false};
                    MetricResult hours=await f.Services.Metrics.QueryAsync(new ContentActor{IsSystemAdmin=true},query,ct).ConfigureAwait(false);
                    Check.Equal(25,hours.Series[0].Points.Count,"Both occurrences of local 1am are represented");Check.Equal(2.0,hours.Series[0].Points.Sum(p=>p.Value??0),"Repeated hours do not merge or disappear");
                    query.Bucket="Day";MetricResult day=await f.Services.Metrics.QueryAsync(new ContentActor{IsSystemAdmin=true},query,ct).ConfigureAwait(false);
                    Check.Equal(25.0,(day.Series[0].Points[0].EndUtc-day.Series[0].Points[0].StartUtc).TotalHours,"DST day boundaries");Check.Equal(2.0,day.Series[0].Points[0].Value??-1,"Day sums both UTC hours");
                    query.Bucket="Week";MetricResult week=await f.Services.Metrics.QueryAsync(new ContentActor{IsSystemAdmin=true},query,ct).ConfigureAwait(false);
                    Check.Equal(start,week.Series[0].Points[0].StartUtc,"Configured Sunday week begins at local midnight");Check.Equal(2.0,week.Series[0].Points[0].Value??-1,"Storage grouping matches configured week");
                }),
                SuiteCases.Http(Id,"DerivedRates","Measured zero errors remain zero, missing measurements remain gaps, and attendance and cost ratios use their sources",async(f,ct)=>
                {
                    DateTime start=MetricsStore.Hour(DateTime.UtcNow.AddDays(-1));
                    await f.Services.Metrics.RecordAsync(new MetricObservation{Metric="requests.count",OccurredUtc=start,Value=10,Dimensions=new Dictionary<string,string>{{"statusClass","2xx"},{"route","GET /health"}}},ct).ConfigureAwait(false);
                    await f.Services.Metrics.RecordAsync(new MetricObservation{Metric="events.attendance",OccurredUtc=start,Value=8,Dimensions=new Dictionary<string,string>{{"status","CheckedIn"}}},ct).ConfigureAwait(false);
                    await f.Services.Metrics.RecordAsync(new MetricObservation{Metric="events.attendance",OccurredUtc=start,Value=2,Dimensions=new Dictionary<string,string>{{"status","NoShow"}}},ct).ConfigureAwait(false);
                    await f.Services.Metrics.RecordAsync(new MetricObservation{Metric="ai.cost",OccurredUtc=start,Value=120,Dimensions=new Dictionary<string,string>{{"model","sample"},{"outcome","Ok"}}},ct).ConfigureAwait(false);
                    await f.Services.Metrics.RecordAsync(new MetricObservation{Metric="ai.calls",OccurredUtc=start,Value=3,Dimensions=new Dictionary<string,string>{{"model","sample"},{"outcome","Ok"}}},ct).ConfigureAwait(false);
                    foreach((string metric,double expected) in new[]{("requests.errorRate",0.0),("events.noShowRate",20.0),("events.checkInRate",80.0),("ai.costPerSuccess",40.0)})
                    {
                        MetricResult result=await f.Services.Metrics.QueryAsync(new ContentActor{IsSystemAdmin=true},new MetricQuery{Metric=metric,From=start,To=start.AddHours(2),Bucket="Hour",Compare=false},ct).ConfigureAwait(false);
                        Check.Equal(expected,result.Series[0].Points[0].Value??-1,"Correct ratio "+metric);Check.True(result.Series[0].Points[1].Value==null,"Unmeasured denominator stays a gap "+metric);
                    }
                }),
                SuiteCases.Http(Id,"ExtensionAndRetention","A neutral extension snapshot uses the common query and per-metric retention remains property scoped",async(f,ct)=>
                {
                    DateTime now=MetricsStore.Hour(DateTime.UtcNow).AddMinutes(30),old=now.AddDays(-2);
                    MetricDefinition extension=new MetricDefinition{Key="sample.capacity",Title="Capacity",Kind="Gauge",Source="Snapshot",RetentionDays=1};f.Services.Metrics.Registry.Register(extension);
                    f.Services.Metrics.RegisterSnapshot((at,token)=>Task.FromResult<IReadOnlyList<MetricObservation>>(new[]{new MetricObservation{Metric=extension.Key,OccurredUtc=at,Value=17}}));
                    await f.Services.Metrics.RecordAsync(new MetricObservation{Metric=extension.Key,OccurredUtc=old,Value=9},ct).ConfigureAwait(false);
                    MetricsStore other=new MetricsStore(f.Services.Database.Deployment.ForProperty(PropertyIds.Generate()));await other.RecordAsync(new MetricObservation{Metric=extension.Key,OccurredUtc=old,Value=99},extension,ct).ConfigureAwait(false);
                    await f.Services.Metrics.RunPassAsync(now,ct).ConfigureAwait(false);
                    MetricResult current=await f.Services.Metrics.QueryAsync(new ContentActor{IsSystemAdmin=true},new MetricQuery{Metric=extension.Key,From=MetricsStore.Hour(now),To=now,Bucket="Hour",Compare=false},ct).ConfigureAwait(false);Check.Equal(17.0,current.Series[0].Points[0].Value??-1,"Snapshot is queryable");
                    MetricQuery expired=new MetricQuery{Metric=extension.Key,From=MetricsStore.Hour(old),To=MetricsStore.Hour(old).AddHours(1),Bucket="Hour",Compare=false};
                    Check.Equal(0,(await f.Services.Metrics.Store.QueryAsync(expired,extension,"UTC",ct).ConfigureAwait(false)).Rows.Count,"Custom one-day retention applied");Check.Equal(1,(await other.QueryAsync(expired,extension,"UTC",ct).ConfigureAwait(false)).Rows.Count,"Other property retained");
                }),
                SuiteCases.Http(Id,"DefaultAlertSignals","Default request and worker alerts honor configured thresholds and isolate properties",async(f,ct)=>
                {
                    DateTime now=DateTime.UtcNow;MetricAlertThresholds thresholds=new MetricAlertThresholds();MetricAlertStore alerts=new MetricAlertStore(f.Services.Database);
                    await f.Services.Database.RequestHistory.CreateAsync(new RequestHistoryEntry{Method="GET",Path="/health",StatusCode=500,DurationMs=3000,CreatedUtc=now.AddMinutes(-1),CompletedUtc=now.AddMinutes(-1).AddSeconds(3)},ct).ConfigureAwait(false);
                    for(int i=1;i<=2;i++)await f.Services.Metrics.Store.WorkerAsync(new WorkerPassRecord{Worker="Sample",StartedUtc=now.AddMinutes(-i),EndedUtc=now.AddMinutes(-i).AddSeconds(1),Outcome="Failed",IntervalSeconds=60},ct).ConfigureAwait(false);
                    Dictionary<string,bool> signals=await alerts.EvaluateAsync(now,null,thresholds,ct).ConfigureAwait(false);Check.Equal(7,signals.Count,"Seven configured default families");Check.True(signals["errorRate"]&&signals["latency"]&&signals["workerFailure"],"Request and consecutive worker failures detected");Check.True(!signals["storageProjection"],"No quota means no invented storage failure");
                    thresholds.ServerErrorPercent=100;thresholds.LatencyP95Ms=4000;signals=await alerts.EvaluateAsync(now,null,thresholds,ct).ConfigureAwait(false);Check.True(!signals["errorRate"]&&!signals["latency"],"Property thresholds apply");
                    Dictionary<string,bool> isolated=await new MetricAlertStore(f.Services.Database.Deployment.ForProperty(PropertyIds.Generate())).EvaluateAsync(now,null,new MetricAlertThresholds(),ct).ConfigureAwait(false);Check.True(isolated.Values.All(v=>!v),"No signal leaks into another property");
                }),
                SuiteCases.Http(Id,"AlertCalendarsAndSlowWorkers","Daily workers remain observable after one day and signup alerts use the property's local yesterday",async(f,ct)=>
                {
                    DateTime now=new DateTime(2026,1,3,12,0,0,DateTimeKind.Utc);MetricAlertThresholds thresholds=new MetricAlertThresholds();MetricAlertStore alerts=new MetricAlertStore(f.Services.Database);
                    await f.Services.Metrics.Store.WorkerAsync(new WorkerPassRecord{Worker="Slow",StartedUtc=now.AddHours(-40),EndedUtc=now.AddHours(-40).AddSeconds(1),Outcome="Ok",IntervalSeconds=12*3600},ct).ConfigureAwait(false);
                    Dictionary<string,bool> signals=await alerts.EvaluateAsync(now,null,thresholds,ct).ConfigureAwait(false);Check.True(signals["workerFailure"],"A slow worker's retained pass still triggers three-interval lag");
                    thresholds.WorkerMissedIntervals=4;signals=await alerts.EvaluateAsync(now,null,thresholds,ct).ConfigureAwait(false);Check.True(!signals["workerFailure"],"Configured longer interval allowance applies");
                    TestMember baseline=await Api.RegisterAsync(f,ct).ConfigureAwait(false),recent=await Api.RegisterAsync(f,ct).ConfigureAwait(false);
                    await f.Raw.ExecuteQueryAsync("UPDATE users SET createdutc='2025-12-25T12:00:00Z' WHERE propertyid='"+f.Services.PropertyId+"' AND id='"+baseline.Id+"'; UPDATE users SET createdutc='2026-01-03T09:00:00Z' WHERE propertyid='"+f.Services.PropertyId+"' AND id='"+recent.Id+"';",false,ct).ConfigureAwait(false);
                    Dictionary<string,bool> utc=await alerts.EvaluateAsync(now,null,thresholds,ct,"UTC").ConfigureAwait(false),local=await alerts.EvaluateAsync(now,null,thresholds,ct,"Pacific/Honolulu").ConfigureAwait(false);
                    Check.True(utc["signupDrop"],"UTC yesterday has zero signups");Check.True(!local["signupDrop"],"Local yesterday includes the recent signup, preventing a false alert");
                }),
                SuiteCases.Http(Id,"DurableMembershipTransitions","Cancellation and lapse writes count once under concurrent reconciliation and never move when mutable rows change",async(f,ct)=>
                {
                    TestMember member=await Api.RegisterAsync(f,ct).ConfigureAwait(false);DateTime start=MetricsStore.Hour(DateTime.UtcNow.AddHours(-3));
                    Subscription subscription=new Subscription{Id=IdGenerator.GenerateSubscriptionId(),UserId=member.Id,Store=StoreEnum.Stripe,StoreSubscriptionId="sample-subscription",ProductId="sample",Status=SubscriptionStatusEnum.Active,AutoRenew=true,CreatedUtc=start.AddDays(-30),UpdatedUtc=start};
                    await f.Services.Database.Subscriptions.CreateAsync(subscription,ct).ConfigureAwait(false);
                    subscription.AutoRenew=false;
                    await Task.WhenAll(Enumerable.Range(0,8).Select(_=>f.Services.Database.Subscriptions.UpdateAsync(subscription,ct))).ConfigureAwait(false);
                    subscription.Status=SubscriptionStatusEnum.Expired;subscription.UpdatedUtc=start.AddHours(1);
                    await Task.WhenAll(Enumerable.Range(0,8).Select(_=>f.Services.Database.Subscriptions.UpdateAsync(subscription,ct))).ConfigureAwait(false);
                    subscription.UpdatedUtc=start.AddHours(2);await f.Services.Database.Subscriptions.UpdateAsync(subscription,ct).ConfigureAwait(false);
                    string otherId=PropertyIds.Generate();DatabaseDriverBase other=f.Services.Database.Deployment.ForProperty(otherId);await other.Subscriptions.UpdateAsync(subscription,ct).ConfigureAwait(false);
                    await f.Services.Metrics.RunPassAsync(start.AddHours(3).AddMinutes(30),ct).ConfigureAwait(false);
                    MetricQuery query=new MetricQuery{Metric="members.lapses",From=start,To=start.AddHours(3),Bucket="Hour",Compare=false};
                    MetricResult result=await f.Services.Metrics.QueryAsync(new ContentActor{IsSystemAdmin=true},query,ct).ConfigureAwait(false);
                    Check.Equal(1.0,result.Series[0].Points[0].Value??-1,"Concurrent cancellation records once in the original hour");Check.Equal(1.0,result.Series[0].Points[1].Value??-1,"Expired transition records once");Check.True(result.Series[0].Points[2].Value==null,"Repeated reconciliation cannot move or repeat a past lapse");
                    MetricDefinition definition=f.Services.Metrics.Registry.Find(query.Metric)!;query.GroupBy="transition";DataTable rows=await f.Services.Metrics.Store.QueryAsync(query,definition,"UTC",ct).ConfigureAwait(false);
                    Check.True(rows.Rows.Cast<DataRow>().Any(r=>RowReader.GetString(r,"label")=="Cancelled")&&rows.Rows.Cast<DataRow>().Any(r=>RowReader.GetString(r,"label")=="Expired"),"Auto-renew cancellation remains distinct from terminal status");
                    Check.Equal(0,(await new MetricsStore(other).QueryAsync(query,definition,"UTC",ct).ConfigureAwait(false)).Rows.Count,"Wrong-property writes neither mutate nor emit");
                    subscription.Id=IdGenerator.GenerateSubscriptionId();subscription.StoreSubscriptionId="historical-expired";await f.Services.Database.Subscriptions.CreateAsync(subscription,ct).ConfigureAwait(false);
                    query.GroupBy=null;Check.Equal(2.0,(await f.Services.Metrics.QueryAsync(new ContentActor{IsSystemAdmin=true},query,ct).ConfigureAwait(false)).Series[0].Points.Sum(p=>p.Value??0),"Importing an already-expired row does not invent its past transition");
                }),
                SuiteCases.Http(Id,"RetentionCalendar","Cohort dates, week ages and return activity share the property timezone and configured Sunday week",async(f,ct)=>
                {
                    DateTime now=new DateTime(2026,1,11,12,0,0,DateTimeKind.Utc);TestMember early=await Api.RegisterAsync(f,ct).ConfigureAwait(false),late=await Api.RegisterAsync(f,ct).ConfigureAwait(false);
                    await f.Raw.ExecuteQueryAsync("UPDATE users SET createdutc='2026-01-04T05:00:00Z' WHERE propertyid='"+f.Services.PropertyId+"' AND id='"+early.Id+"'; UPDATE users SET createdutc='2026-01-04T11:00:00Z' WHERE propertyid='"+f.Services.PropertyId+"' AND id='"+late.Id+"';",false,ct).ConfigureAwait(false);
                    await f.Services.Database.Usage.RecordActivityAsync(early.Id,new DateTime(2026,1,11,9,0,0,DateTimeKind.Utc),ct).ConfigureAwait(false);await f.Services.Database.Usage.RecordActivityAsync(late.Id,new DateTime(2026,1,11,11,0,0,DateTimeKind.Utc),ct).ConfigureAwait(false);
                    await new MetricsRollupStore(f.Services.Database).SnapshotAsync(now,ct,"Pacific/Honolulu",DayOfWeek.Sunday).ConfigureAwait(false);
                    MetricQuery query=new MetricQuery{Metric="members.retention",From=MetricsStore.Hour(now),To=now.AddMinutes(1),Bucket="Hour",GroupBy="cohort",Compare=false};MetricDefinition definition=f.Services.Metrics.Registry.Find(query.Metric)!;
                    DataTable rows=await f.Services.Metrics.Store.QueryAsync(query,definition,"Pacific/Honolulu",ct,DayOfWeek.Sunday).ConfigureAwait(false);
                    Check.Equal(0.0,RowReader.GetDouble(rows.Rows.Cast<DataRow>().Single(r=>RowReader.GetString(r,"label")=="2025-12-28"),"value"),"Saturday local activity does not count as this week's return");Check.Equal(100.0,RowReader.GetDouble(rows.Rows.Cast<DataRow>().Single(r=>RowReader.GetString(r,"label")=="2026-01-04"),"value"),"Sunday local activity counts in the matching cohort");
                    query.GroupBy="week";rows=await f.Services.Metrics.Store.QueryAsync(query,definition,"Pacific/Honolulu",ct,DayOfWeek.Sunday).ConfigureAwait(false);Check.True(rows.Rows.Cast<DataRow>().Any(r=>RowReader.GetString(r,"label")=="1")&&rows.Rows.Cast<DataRow>().Any(r=>RowReader.GetString(r,"label")=="2"),"Week age uses local calendar dates");
                }),
                SuiteCases.Http(Id,"EnumerationCollections","Registry pages and persistent histories obey standard filters, cursors and exact totals",async(f,ct)=>
                {
                    JsonElement catalog=await Api.ExpectAsync(f,HttpMethod.Post,"/api/v1/admin/metrics/enumerate",HttpFixture.AdminToken,new {maxResults=2,ordering="KeyAscending"},HttpStatusCode.OK,ct).ConfigureAwait(false);
                    Check.Equal(2,catalog.GetProperty("objects").GetArrayLength(),"Registry page size");Check.True(catalog.GetProperty("totalRecords").GetInt64()>2,"Exact registry total is not page length");
                    string cursor=Api.Str(catalog,"continuationToken");JsonElement next=await Api.ExpectAsync(f,HttpMethod.Get,"/api/v1/admin/metrics?maxResults=2&continuationToken="+Uri.EscapeDataString(cursor),HttpFixture.AdminToken,null,HttpStatusCode.OK,ct).ConfigureAwait(false);
                    Check.True(Api.Str(catalog.GetProperty("objects")[1],"key")!=Api.Str(next.GetProperty("objects")[0],"key"),"Registry continuation advances");
                    await Api.ExpectErrorAsync(f,HttpMethod.Post,"/api/v1/admin/metrics/enumerate",HttpFixture.AdminToken,new {ordering="NotAllowed"},HttpStatusCode.BadRequest,"ValidationFailed",ct).ConfigureAwait(false);
                    JsonElement empty=await Api.ExpectAsync(f,HttpMethod.Post,"/api/v1/admin/metrics/pages/enumerate",HttpFixture.AdminToken,new {createdAfterUtc=DateTime.UtcNow.AddYears(1)},HttpStatusCode.OK,ct).ConfigureAwait(false);Check.Equal(0,empty.GetProperty("objects").GetArrayLength(),"Catalog created filter honored");
                    DateTime now=DateTime.UtcNow;
                    for(int i=0;i<12;i++)await f.Services.Metrics.Store.WorkerAsync(new WorkerPassRecord{Id="enum"+i.ToString("D2"),Worker="EnumerationWorker",StartedUtc=now.AddMinutes(-i),EndedUtc=now.AddMinutes(-i).AddSeconds(1)},ct).ConfigureAwait(false);
                    JsonElement passes=await Api.ExpectAsync(f,HttpMethod.Post,"/api/v1/admin/metrics/workers/enumerate",HttpFixture.AdminToken,new {maxResults=5,filters=new {worker="EnumerationWorker"}},HttpStatusCode.OK,ct).ConfigureAwait(false);
                    Check.Equal(12L,passes.GetProperty("totalRecords").GetInt64(),"History exact total");Check.Equal(7L,passes.GetProperty("recordsRemaining").GetInt64(),"History exact remaining");
                    string workerCursor=Api.Str(passes,"continuationToken");await f.Services.Metrics.Store.WorkerAsync(new WorkerPassRecord{Id="newer",Worker="EnumerationWorker",StartedUtc=now.AddMinutes(1),EndedUtc=now.AddMinutes(1).AddSeconds(1)},ct).ConfigureAwait(false);
                    JsonElement older=await Api.ExpectAsync(f,HttpMethod.Post,"/api/v1/admin/metrics/workers/enumerate",HttpFixture.AdminToken,new {maxResults=5,continuationToken=workerCursor,filters=new {worker="EnumerationWorker"}},HttpStatusCode.OK,ct).ConfigureAwait(false);
                    Check.Equal("enum05",Api.Str(older.GetProperty("objects")[0],"id"),"Keyset unaffected by a newer pass");
                    JsonElement noLabels=await Api.ExpectAsync(f,HttpMethod.Post,"/api/v1/admin/metrics/workers/enumerate",HttpFixture.AdminToken,new {labels=new[]{"absent"}},HttpStatusCode.OK,ct).ConfigureAwait(false);Check.Equal(0L,noLabels.GetProperty("totalRecords").GetInt64(),"Empty operational metadata filters match none");
                    Check.Throws<ArgumentException>(()=>new MetricRegistry(new PropertyMetricsConfiguration{Definitions=new List<MetricDefinition>{new MetricDefinition{Key="sample.invalid",Buckets=new List<string>{"Hour","Hour"}}}}),"Duplicate bucket declarations rejected");
                }),
                SuiteCases.Http(Id,"ObservationEnumeration","Chart transport pages individual observations with query-bound cursors, exact totals and no aggregate wrapper",async(f,ct)=>
                {
                    DateTime start=MetricsStore.Hour(DateTime.UtcNow.AddDays(-3));
                    for(int i=0;i<30;i++)foreach(string route in new[]{"one","two"})await f.Services.Metrics.RecordAsync(new MetricObservation{Metric="requests.count",OccurredUtc=start.AddHours(i),Value=1,Dimensions=new Dictionary<string,string>{{"route",route}}},ct).ConfigureAwait(false);
                    MetricQuery selection=new MetricQuery{Metric="requests.count",From=start,To=start.AddHours(30),Bucket="Hour",GroupBy="route",Compare=false};
                    string? cursor=null;HashSet<string> ids=new HashSet<string>();int pages=0;
                    do
                    {
                        JsonElement page=await Api.ExpectAsync(f,HttpMethod.Post,"/api/v1/admin/metrics/query",HttpFixture.AdminToken,new {query=selection,maxResults=7,continuationToken=cursor},HttpStatusCode.OK,ct).ConfigureAwait(false);
                        Check.Equal(60L,page.GetProperty("totalRecords").GetInt64(),"Exact bucket/group count");Check.True(page.GetProperty("objects").GetArrayLength()<=7,"Actual observations are paged");Check.True(!page.TryGetProperty("series",out JsonElement _),"No hidden unpaged series");
                        foreach(JsonElement row in page.GetProperty("objects").EnumerateArray())Check.True(ids.Add(Api.Str(row,"id")),"No repeated bucket across pages");
                        cursor=page.GetProperty("continuationToken").ValueKind==JsonValueKind.String?page.GetProperty("continuationToken").GetString():null;pages++;
                    }while(cursor!=null&&pages<20);
                    Check.Equal(60,ids.Count,"Every observation reachable");
                    JsonElement filtered=await Api.ExpectAsync(f,HttpMethod.Post,"/api/v1/admin/metrics/query/enumerate",HttpFixture.AdminToken,new {query=selection,createdAfterUtc=start.AddHours(25)},HttpStatusCode.OK,ct).ConfigureAwait(false);Check.Equal(10L,filtered.GetProperty("totalRecords").GetInt64(),"Standard created window filters actual buckets");
                    JsonElement first=await Api.ExpectAsync(f,HttpMethod.Post,"/api/v1/admin/metrics/query",HttpFixture.AdminToken,new {query=selection,maxResults=1},HttpStatusCode.OK,ct).ConfigureAwait(false);selection.From=start.AddHours(1);
                    await Api.ExpectErrorAsync(f,HttpMethod.Post,"/api/v1/admin/metrics/query",HttpFixture.AdminToken,new {query=selection,maxResults=1,continuationToken=Api.Str(first,"continuationToken")},HttpStatusCode.BadRequest,"InvalidContinuationToken",ct).ConfigureAwait(false);
                }),
                SuiteCases.Http(Id,"AlertsAndAnnotations","Alert transitions fire once, rearm on recovery, and annotations deduplicate",async(f,ct)=>
                {
                    DateTime now=MetricsStore.Hour(DateTime.UtcNow);
                    MetricsStore store=f.Services.Metrics.Store;
                    Check.True(!await store.AlertAsync("test.rule",true,2,now,ct).ConfigureAwait(false),"First breach waits");
                    Check.True(!await store.AlertAsync("test.rule",true,2,now,ct).ConfigureAwait(false),"Same minute cannot advance streak");
                    Check.True(await store.AlertAsync("test.rule",true,2,now.AddMinutes(1),ct).ConfigureAwait(false),"Threshold fires once");
                    Check.True(!await store.AlertAsync("test.rule",true,2,now.AddMinutes(2),ct).ConfigureAwait(false),"Continued breach does not spam");
                    await store.AlertAsync("test.rule",false,2,now.AddMinutes(3),ct).ConfigureAwait(false);
                    await store.AlertAsync("test.rule",true,2,now.AddMinutes(4),ct).ConfigureAwait(false);
                    Check.True(await store.AlertAsync("test.rule",true,2,now.AddMinutes(5),ct).ConfigureAwait(false),"Recovery rearms");
                    await store.AlertAsync("test.gap",true,2,now,ct).ConfigureAwait(false);
                    Check.True(!await store.AlertAsync("test.gap",true,2,now.AddMinutes(3),ct).ConfigureAwait(false),"Missing evaluations break a consecutive-minute streak");
                    MetricAnnotation annotation=new MetricAnnotation{Id="same",Kind="Deploy",Label="Deployment",OccurredUtc=now};
                    await store.AnnotateAsync(annotation,ct).ConfigureAwait(false);await store.AnnotateAsync(annotation,ct).ConfigureAwait(false);
                    Check.Equal(1,(await store.AnnotationsAsync(now,now.AddHours(1),ct).ConfigureAwait(false)).Count(a=>a.Id=="same"),"Idempotent annotation");
                })
            });
        }
    }
}
