namespace TrellisKit.Server.Services.Metrics
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using TrellisKit.Core.Metrics;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.PropertyConfig;

    /// <summary>Property-local metric and dashboard registry. Registration is bounded, additive, and rejects undeclared dimensions.</summary>
    public sealed class MetricRegistry
    {
        private readonly Dictionary<string,MetricDefinition> _Definitions = new Dictionary<string,MetricDefinition>(StringComparer.Ordinal);
        private readonly int _RetentionDays;
        private readonly List<MetricPageDeclaration> _Pages = new List<MetricPageDeclaration>();
        private static readonly Regex _Key = new Regex("^[a-zA-Z][a-zA-Z0-9_.-]{0,99}$",RegexOptions.CultureInvariant);
        /// <summary>At most 256 registered metrics.</summary>
        public IReadOnlyList<MetricDefinition> Definitions => _Definitions.Values.ToList();
        /// <summary>At most 26 pages, each with at most 24 charts.</summary>
        public IReadOnlyList<MetricPageDeclaration> Pages => _Pages;
        /// <summary>Build the neutral engine catalog and merge a property's declarations.</summary>
        public MetricRegistry(PropertyMetricsConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            _RetentionDays=configuration.RetentionDays;
            if(configuration.RetentionDays<1||configuration.RetentionDays>1830||configuration.WorkerRetentionDays<1||configuration.WorkerRetentionDays>365) throw new ArgumentException("Metric retention is outside its bounds.",nameof(configuration));
            foreach(UsageMeter meter in UsageMeters.All) Register(new MetricDefinition {Key=meter.Id,Title=meter.Id,Unit=meter.Unit,Kind=meter.Kind.ToString(),Source="Usage",Drilldown=Drilldown(meter.Id),MemberSensitive=meter.Group=="users",Buckets=meter.DailyOnly?new List<string>{"Day","Week"}:new List<string>{"Hour","Day","Week"}});
            Add("requests.count","Requests","Count","count","Requests","route","statusClass","errorCode","app","platform","version","section");
            Add("requests.latency","Request latency","Distribution","ms","Requests","route","statusClass","app","platform","version");
            Add("requests.errorRate","Server error rate","Gauge","percent","Computed","route","app","platform","version");
            Add("requests.rateLimited","Rate limited requests","Count","count","Requests","route");
            Add("requests.load","Load pattern","Count","count","Requests","weekday","hour");
            Add("realtime.connections","Realtime connections","Gauge","count","Snapshot");
            Add("dependencies.health","Dependencies","Gauge","percent","Recorded","dependency");
            Add("members.methods","Sign-ups by method","Count","count","Snapshot","method");
            Add("members.sources","Sign-ups by source","Count","count","Computed","source");
            Add("members.profile","Profile field mix","Gauge","count","Snapshot","field","option");
            Add("members.membership","Memberships","Gauge","count","Snapshot","status","store");
            Add("members.lapses","Membership lapses and cancellations","Count","count","Recorded","status","transition","store");
            Add("members.retention","Retention cohorts","Gauge","percent","Snapshot","cohort","week");
            Add("members.stickiness","Daily / monthly active","Gauge","percent","Computed");
            Add("engagement.perActive","Interactions per active member","Gauge","count","Computed");
            Add("forums.posts","Forum threads","Count","count","Snapshot","forum");
            Add("forums.replies","Forum replies","Count","count","Snapshot","forum");
            Add("library.completionRate","Library completion rate","Gauge","percent","Computed","series");
            Add("events.attendance","Event attendance","Count","count","Snapshot","status");
            Add("events.noShowRate","No-show rate","Gauge","percent","Computed");
            Add("interactions.activity","Interactions","Count","count","Computed","kind");
            Add("storage.classes","Storage by class","Gauge","bytes","Computed","class");
            Add("events.checkInRate","Check-in rate","Gauge","percent","Computed");
            Add("storage.projected30Days","Storage projection in 30 days","Gauge","bytes","Snapshot");
            Add("forums.unanswered","Unanswered threads","Gauge","count","Snapshot","forum");
            Add("forums.replyTime","Time to first reply","Distribution","ms","Snapshot","forum");
            Add("library.plays","Lesson plays","Count","count","Snapshot","series");
            Add("library.completions","Lesson completions","Count","count","Snapshot","series");
            Add("events.rsvps","Event responses","Count","count","Snapshot","status");
            Add("directory.listings","Directory listings","Gauge","count","Snapshot","status");
            Add("directory.submissions","Directory submissions","Count","count","Snapshot","status");
            Add("directory.decisions","Directory decisions","Count","count","Snapshot","status");
            Add("messages.sent","Messages","Count","count","Snapshot");
            Add("messages.conversations","Conversations started","Count","count","Snapshot");
            Add("notifications.delivery","Notification delivery","Count","count","Snapshot","channel","stage");
            Add("contributors.share","Contribution share","Gauge","percent","Snapshot","percentile");
            Add("ai.calls","Model calls","Count","count","AI","endpoint","assistant","model","outcome","provider");
            Add("ai.tokens","Model tokens","Count","tokens","AI","class","endpoint","model");
            Add("ai.cost","Model cost","Count","usdMicros","AI","endpoint","assistant","model","provider","outcome");
            Add("ai.latency","Model latency","Distribution","ms","AI","endpoint","model");
            Add("ai.firstToken","Time to first token","Distribution","ms","AI","endpoint","model");
            Add("ai.health","Endpoint health","Gauge","percent","Snapshot","endpoint");
            Add("ai.messages","Assistant messages","Count","count","Snapshot","assistant");
            Add("ai.costPerSuccess","Cost per successful model call","Gauge","usdMicros","Computed","endpoint","assistant","model","provider");
            Add("ai.conversations","Assistant conversations","Count","count","Snapshot","assistant");
            Add("workers.runs","Worker passes","Count","count","Workers","worker","outcome");
            Add("workers.duration","Worker duration","Distribution","ms","Workers","worker");
            Add("workers.items","Worker items","Count","count","Workers","worker");
            Add("workers.lag","Time since last success","Gauge","ms","Snapshot","worker");
            Add("moderation.reports","Reports","Count","count","Snapshot","reason");
            Add("moderation.actions","Moderator actions","Count","count","Snapshot","action");
            Add("moderation.responseTime","Time to moderation action","Distribution","ms","Snapshot");
            Add("moderation.backlog","Open reports","Gauge","count","Snapshot","reason");
            Add("moderation.overturned","Overturned automatic holds","Count","count","Snapshot");
            Add("moderation.holds","Automatic holds","Count","count","Snapshot","action");
            Add("support.opened","Tickets opened","Count","count","Snapshot","type");
            Add("support.resolved","Tickets resolved","Count","count","Snapshot","type");
            Add("support.backlog","Open ticket backlog","Gauge","count","Snapshot","type");
            Add("support.firstResponse","First response time","Distribution","ms","Snapshot","type");
            Add("auth.problems","Sign-in problems","Count","count","Events","kind");
            Add("events.product","Product events","Count","count","Events","event","source");
            Page("health","Health","Is the service healthy right now?",new[]{"requests.count","requests.errorRate","requests.latency","requests.rateLimited","realtime.connections","dependencies.health","requests.load"});
            Page("growth","Growth","Is the community growing, and do members come back?",new[]{"users.total","users.new","users.deleted","users.active","users.activeWeekly","users.activeMonthly","members.stickiness","members.methods","members.sources","members.membership","members.lapses","members.profile","members.retention","events.product"});
            Page("engagement","Engagement","Are members doing things?",new[]{"interactions.activity","engagement.perActive","forums.posts","forums.unanswered","forums.replyTime","library.plays","library.completions","library.completionRate","events.rsvps","events.attendance","events.checkInRate","events.noShowRate","directory.listings","directory.submissions","directory.decisions","messages.sent","messages.conversations","notifications.delivery","contributors.share","storage.classes","storage.projected30Days"});
            Page("ai-usage","AI usage","What are the models doing, and what do they cost?",new[]{"ai.calls","ai.cost","ai.tokens","ai.latency","ai.firstToken","ai.health","ai.conversations","ai.messages","ai.costPerSuccess"});
            Page("workers","Workers","Are the background workers running?",new[]{"workers.runs","workers.duration","workers.items","workers.lag"});
            Page("moderation-support","Moderation and support","Is the community safe and supported?",new[]{"moderation.reports","moderation.actions","moderation.responseTime","moderation.holds","moderation.overturned","moderation.backlog","support.opened","support.resolved","support.backlog","support.firstResponse","auth.problems"});
            Chart("interactions.activity","StackedArea","kind");Chart("storage.classes","StackedArea","class");
            Chart("directory.submissions","StackedBars","status");Chart("directory.decisions","StackedBars","status");Chart("ai.messages","StackedBars","assistant");Chart("ai.costPerSuccess","RankedBars","model");Chart("dependencies.health","UptimeStrip","dependency");Chart("requests.rateLimited","RankedBars","route");
            Chart("forums.posts","RankedBars","forum");Chart("events.attendance","StackedBars","status");Chart("members.sources","StackedBars","source");
            Chart("requests.count","StackedArea","statusClass");Chart("requests.load","HeatMap","weekday");
            Chart("members.methods","StackedBars","method");Chart("members.membership","StackedArea","status");Chart("members.lapses","StackedBars","transition");Chart("members.profile","StackedBars","option");Chart("members.retention","CohortGrid","cohort");Chart("events.product","Funnel","event");
            Chart("forums.unanswered","RankedBars","forum");Chart("library.plays","RankedBars","series");Chart("library.completions","RankedBars","series");Chart("events.rsvps","StackedBars","status");Chart("directory.listings","StackedArea","status");Chart("notifications.delivery","Funnel","stage");Chart("contributors.share","RankedBars","percentile");
            Chart("ai.calls","StackedArea","endpoint");Chart("ai.cost","StackedBars","model");Chart("ai.tokens","StackedArea","class");Chart("ai.health","UptimeStrip","endpoint");Chart("ai.conversations","StackedBars","assistant");
            Chart("workers.runs","Swimlanes","worker");Chart("workers.duration","Line","worker");Chart("workers.items","Bars","worker");Chart("workers.lag","RankedBars","worker");
            Chart("moderation.reports","StackedBars","reason");Chart("moderation.actions","StackedBars","action");Chart("support.backlog","StackedArea","type");
            _Pages.First(p=>p.Id=="engagement").Charts.Add(new MetricChartDeclaration{Id="feature-use",Metric="requests.count",Title="Feature use",Form="StackedBars",GroupBy="section"});
            MetricPageDeclaration health=_Pages.First(p=>p.Id=="health");
            health.Charts.Add(new MetricChartDeclaration{Id="latency-p50",Metric="requests.latency",Title="Median request latency",Percentile=.50});
            health.Charts.Add(new MetricChartDeclaration{Id="latency-p99",Metric="requests.latency",Title="p99 request latency",Percentile=.99});
            health.Charts.Add(new MetricChartDeclaration{Id="slow-routes",Metric="requests.latency",Title="Slowest routes",Form="RankedBars",GroupBy="route"});
            health.Charts.Add(new MetricChartDeclaration{Id="failing-routes",Metric="requests.errorRate",Title="Failing routes",Form="RankedBars",GroupBy="route"});
            health.Charts.Add(new MetricChartDeclaration{Id="error-codes",Metric="requests.count",Title="Requests by error code",Form="StackedBars",GroupBy="errorCode"});
            health.Charts.Add(new MetricChartDeclaration{Id="request-clients",Metric="requests.count",Title="Requests by client",Form="StackedArea",GroupBy="platform"});
            health.Charts.Add(new MetricChartDeclaration{Id="request-versions",Metric="requests.errorRate",Title="Errors by application version",Form="Line",GroupBy="version"});
            MetricPageDeclaration aiPage=_Pages.First(p=>p.Id=="ai-usage");
            aiPage.Charts.Add(new MetricChartDeclaration{Id="ai-outcomes",Metric="ai.calls",Title="Model outcomes",Form="StackedBars",GroupBy="outcome"});
            aiPage.Charts.Add(new MetricChartDeclaration{Id="ai-latency-p50",Metric="ai.latency",Title="Median model latency",Percentile=.5});
            aiPage.Charts.Add(new MetricChartDeclaration{Id="ai-first-token-p50",Metric="ai.firstToken",Title="Median first token",Percentile=.5});
            aiPage.Charts.Add(new MetricChartDeclaration{Id="ai-provider-cost",Metric="ai.cost",Title="Cost by provider",Form="RankedBars",GroupBy="provider"});
            aiPage.Charts.Add(new MetricChartDeclaration{Id="ai-endpoint-latency",Metric="ai.latency",Title="Endpoint latency",Form="Line",GroupBy="endpoint"});
            foreach(MetricChartDeclaration chart in _Pages.SelectMany(p=>p.Charts))
            {
                if(chart.Metric=="forums.replyTime"||chart.Metric=="moderation.responseTime"||chart.Metric=="support.firstResponse")chart.Percentile=.5;
                if(chart.Metric=="requests.errorRate"&&chart.GroupBy==null)chart.Threshold=configuration.AlertThresholds.ServerErrorPercent;
            }
            if(configuration.Definitions.Count>100||configuration.Pages.Count>20||configuration.Events.Count>100||configuration.Alerts.Count>100) throw new ArgumentException("Metric declarations exceed their bounds.",nameof(configuration));
            foreach(MetricDefinition definition in configuration.Definitions) Register(definition);
            foreach(MetricEventDeclaration declared in configuration.Events) Register(new MetricDefinition { Key="events.custom."+declared.Name,Title=declared.Name,Source="Events",Groupings=declared.Dimensions.Keys.ToList() });
            foreach(MetricPageDeclaration page in configuration.Pages)
            {
                ValidatePage(page);
                MetricPageDeclaration? existing=_Pages.FirstOrDefault(p=>p.Id==page.Id);
                if(existing==null) _Pages.Add(page);
                else { if(existing.Charts.Count+page.Charts.Count>24)throw new ArgumentException("At most 24 charts per page.");existing.Charts.AddRange(page.Charts); }
            }
        }
        /// <summary>Register an extension metric. Existing keys cannot be replaced.</summary>
        public void Register(MetricDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);
            if(!_Key.IsMatch(definition.Key)||_Definitions.Count>=256||_Definitions.ContainsKey(definition.Key))throw new ArgumentException("Invalid or duplicate metric key: "+definition.Key);
            if(!new[]{"Count","Gauge","Distinct","Distribution"}.Contains(definition.Kind)||definition.Groupings.Count>8||definition.Groupings.Any(d=>!_Key.IsMatch(d))||definition.Buckets.Count==0||definition.Buckets.Count>3||definition.Buckets.Distinct(StringComparer.Ordinal).Count()!=definition.Buckets.Count||definition.Buckets.Any(b=>!new[]{"Hour","Day","Week"}.Contains(b))||definition.RetentionDays<1||definition.RetentionDays>1830||definition.ReadRole!="SystemAdmin")throw new ArgumentException("Invalid metric declaration: "+definition.Key);
            _Definitions.Add(definition.Key,definition);
        }
        /// <summary>Find a metric by exact key.</summary>
        public MetricDefinition? Find(string key) { return _Definitions.TryGetValue(key,out MetricDefinition? definition)?definition:null; }
        private void Add(string key,string title,string kind,string unit,string source,params string[] dimensions)
        {
            Register(new MetricDefinition{Key=key,Title=title,Kind=kind,Unit=unit,Source=source,RetentionDays=_RetentionDays,Groupings=dimensions.ToList(),MemberSensitive=key.StartsWith("members.",StringComparison.Ordinal)||key=="contributors.share",Feature=key.StartsWith("forums.",StringComparison.Ordinal)?"forums":key.StartsWith("library.",StringComparison.Ordinal)?"library":key.StartsWith("events.",StringComparison.Ordinal)&&key!="events.product"?"events":key.StartsWith("directory.",StringComparison.Ordinal)?"directory":key.StartsWith("messages.",StringComparison.Ordinal)?"messaging":key.StartsWith("ai.",StringComparison.Ordinal)?"ai":null,Drilldown=Drilldown(key)});
        }
        private static string? Drilldown(string key)
        {
            string family=key.Split('.')[0];
            return family switch {"requests"=>"/admin/requests","auth"=>"/admin/requests","members"=>"/admin/members","users"=>"/admin/members","forums"=>"/admin/forums","library"=>"/admin/library","events"=>"/admin/events","directory"=>"/admin/directory","support"=>"/admin/support","moderation"=>"/admin/moderation","ai"=>"/admin/ai-analytics","storage"=>"/admin/usage",_=>null};
        }
        private void Page(string id,string title,string question,string[] metrics)
        {
            _Pages.Add(new MetricPageDeclaration{Id=id,Title=title,Question=question,Charts=metrics.Select(key=>new MetricChartDeclaration{Id=key,Metric=key,Title=_Definitions[key].Title,Form=_Definitions[key].Kind=="Count"?"Bars":"Line"}).ToList()});
        }
        private void Chart(string metric,string form,string? group)
        {
            foreach(MetricChartDeclaration chart in _Pages.SelectMany(p=>p.Charts).Where(c=>c.Metric==metric)){chart.Form=form;chart.GroupBy=group;}
        }
        private void ValidatePage(MetricPageDeclaration page)
        {
            if(!_Key.IsMatch(page.Id)||page.Charts.Count>24)throw new ArgumentException("Invalid metrics page.");
            foreach(MetricChartDeclaration chart in page.Charts) { MetricDefinition? metric=Find(chart.Metric);if(metric==null||chart.GroupBy!=null&&!metric.Groupings.Contains(chart.GroupBy))throw new ArgumentException("A chart references an unknown metric or dimension."); }
        }
    }
}
