namespace TrellisKit.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Postgresql;

    /// <summary>Engine metric producers. Raw source tables are scanned only by the worker, never by dashboard requests.</summary>
    public sealed class MetricsRollupStore
    {
        private readonly DatabaseDriverBase _Database;
        private readonly string _Pid;
        /// <summary>Create a producer bound to one property.</summary>
        public MetricsRollupStore(DatabaseDriverBase database)
        {
            _Database=database??throw new ArgumentNullException(nameof(database));
            _Pid=Sanitizer.Str(database.BoundPropertyId??throw new ArgumentException("Metrics require a property binding.",nameof(database)));
        }
        /// <summary>Recompute an overlapping hour window, replacing aggregates atomically. At most 48 hours per pass.</summary>
        public async Task<int> RollupAsync(DateTime from,DateTime now,CancellationToken token=default)
        {
            if(now<=from||(now-from).TotalHours>48)throw new ArgumentOutOfRangeException(nameof(from));
            List<string> sql=new List<string>();
            string start=Sanitizer.Ts(MetricsStore.Hour(from)),end=Sanitizer.Ts(now),stamp=Sanitizer.Ts(now);
            string window="createdutc >= "+start+" AND createdutc < "+end;
            string requests="SELECT *,date_trunc('hour',createdutc) bucket,jsonb_build_object('route',CASE WHEN metricroute='' THEN 'unknown' ELSE metricroute END,'statusClass',(statuscode/100)::text||'xx','errorCode',CASE WHEN metricerror='' THEN statuscode::text ELSE metricerror END,'app',metricapp,'platform',metricplatform,'version',metricversion,'section',split_part(metricroute,'/',4)) dimensions FROM requesthistory WHERE propertyid="+_Pid+" AND "+window;
            Count(sql,"requests.count",requests,"1",start,end,stamp);
            Distribution(sql,"requests.latency",requests,"durationms",start,end,stamp);
            Count(sql,"requests.rateLimited",requests+" AND statuscode=429","1",start,end,stamp);
            Count(sql,"requests.load","SELECT *,date_trunc('hour',createdutc) bucket,jsonb_build_object('weekday',extract(isodow FROM createdutc AT TIME ZONE 'UTC')::text,'hour',extract(hour FROM createdutc AT TIME ZONE 'UTC')::text) dimensions FROM requesthistory WHERE propertyid="+_Pid+" AND "+window,"1",start,end,stamp);
            string ai="SELECT c.*,date_trunc('hour',c.createdutc) bucket,jsonb_build_object('endpoint',COALESCE(c.endpointid,'none'),'assistant',COALESCE(c.assistantid,'none'),'model',COALESCE(c.model,'unknown'),'outcome',c.outcome,'provider',COALESCE(e.apitype,c.runtime)) dimensions FROM aicalls c LEFT JOIN aiendpoints e ON e.propertyid="+_Pid+" AND e.id=c.endpointid WHERE c.propertyid="+_Pid+" AND NOT c.istest AND c.createdutc >= "+start+" AND c.createdutc < "+end;
            Count(sql,"ai.calls",ai,"1",start,end,stamp); Count(sql,"ai.cost",ai,"costmicros",start,end,stamp);
            Distribution(sql,"ai.latency",ai,"latencyms",start,end,stamp);
            Distribution(sql,"ai.firstToken",ai+" AND c.timetofirsttokenms IS NOT NULL","timetofirsttokenms",start,end,stamp);
            string tokens="SELECT c.*,date_trunc('hour',c.createdutc) bucket,jsonb_build_object('endpoint',COALESCE(c.endpointid,'none'),'model',COALESCE(c.model,'unknown'),'class',v.kind) dimensions,v.tokens FROM aicalls c CROSS JOIN LATERAL (VALUES ('input',c.prompttokens),('output',c.completiontokens),('cachedRead',COALESCE(c.cachedreadtokens,0)),('cacheWrite',COALESCE(c.cachecreationtokens,0))) v(kind,tokens) WHERE c.propertyid="+_Pid+" AND NOT c.istest AND c.createdutc >= "+start+" AND c.createdutc < "+end;
            Count(sql,"ai.tokens",tokens,"tokens",start,end,stamp);
            string workers="SELECT *,date_trunc('hour',startedutc) bucket,jsonb_build_object('worker',worker,'outcome',outcome) dimensions FROM metricworkerruns WHERE propertyid="+_Pid+" AND startedutc >= "+start+" AND startedutc < "+end;
            Count(sql,"workers.runs",workers,"1",start,end,stamp);Count(sql,"workers.items",workers,"items",start,end,stamp);Distribution(sql,"workers.duration",workers,"extract(epoch FROM (endedutc-startedutc))*1000",start,end,stamp);
            Temporal(sql,"messages.sent","messages","createdutc",null,null,start,end,stamp);
            Temporal(sql,"messages.conversations","conversations","createdutc",null,null,start,end,stamp);
            Temporal(sql,"moderation.reports","reports","createdutc","reason","reason",start,end,stamp);
            Temporal(sql,"moderation.actions","moderationactions","createdutc","action","action",start,end,stamp);
            Count(sql,"moderation.overturned","SELECT m.*,date_trunc('hour',m.createdutc) bucket,'{}'::jsonb dimensions FROM moderationactions m WHERE m.propertyid="+_Pid+" AND m.action IN ('Unhide','Restore') AND m.createdutc >= "+start+" AND m.createdutc < "+end+" AND EXISTS(SELECT 1 FROM wordfilterhits w WHERE w.propertyid="+_Pid+" AND w.targetid=m.targetid AND w.targettype=m.targettype AND w.action='Flag' AND w.createdutc<=m.createdutc)","1",start,end,stamp);
            Temporal(sql,"moderation.holds","wordfilterhits","createdutc","action","action",start,end,stamp);
            Temporal(sql,"support.opened","tickets","createdutc","type","typekey",start,end,stamp);
            Temporal(sql,"support.resolved","tickets","resolvedutc","type","typekey",start,end,stamp);
            Distribution(sql,"support.firstResponse",TemporalSource("tickets","firstresponseutc","type","typekey",start,end),"extract(epoch FROM(firstresponseutc-createdutc))*1000",start,end,stamp);
            Distribution(sql,"moderation.responseTime",TemporalSource("reports","resolvedutc",null,null,start,end),"extract(epoch FROM(resolvedutc-createdutc))*1000",start,end,stamp);
            Temporal(sql,"events.rsvps","eventrsvps","createdutc","status","status",start,end,stamp);
            Temporal(sql,"directory.submissions","directorysubmissions","createdutc","status","status",start,end,stamp);
            Temporal(sql,"directory.decisions","directorysubmissions","decidedutc","status","status",start,end,stamp);
            Temporal(sql,"ai.messages","aimessages","createdutc","assistant","assistantid",start,end,stamp," AND role='Assistant'");
            Temporal(sql,"ai.conversations","aiconversations","createdutc","assistant","assistantid",start,end,stamp);
            Temporal(sql,"library.plays","lessonplays","startedutc","series","(SELECT l.seriesid FROM lessons l WHERE l.propertyid="+_Pid+" AND l.id=lessonplays.lessonid)",start,end,stamp);
            Temporal(sql,"library.completions","lessonprogress","completedutc","series","(SELECT l.seriesid FROM lessons l WHERE l.propertyid="+_Pid+" AND l.id=lessonprogress.lessonid)",start,end,stamp);
            Count(sql,"members.methods","SELECT u.*,date_trunc('hour',u.createdutc) bucket,jsonb_build_object('method',COALESCE((SELECT e.provider FROM externalidentities e WHERE e.propertyid="+_Pid+" AND e.userid=u.id ORDER BY e.createdutc LIMIT 1),'Email')) dimensions FROM users u WHERE u.propertyid="+_Pid+" AND u.createdutc >= "+start+" AND u.createdutc < "+end,"1",start,end,stamp);
            string notifications="SELECT date_trunc('hour',t) bucket,jsonb_build_object('channel',channel,'stage',stage) dimensions FROM (SELECT createdutc t,'inApp' channel,'sent' stage FROM notifications WHERE propertyid="+_Pid+" UNION ALL SELECT readutc,'inApp','opened' FROM notifications WHERE propertyid="+_Pid+" UNION ALL SELECT sentutc,'email','sent' FROM emailmessages WHERE propertyid="+_Pid+" UNION ALL SELECT deliveredutc,'email','delivered' FROM emailmessages WHERE propertyid="+_Pid+" UNION ALL SELECT createdutc,'push','sent' FROM pushdeliveries WHERE propertyid="+_Pid+" AND status IN ('Sent','Ok') UNION ALL SELECT receiptcheckedutc,'push','delivered' FROM pushdeliveries WHERE propertyid="+_Pid+" AND status='Ok') events WHERE t >= "+start+" AND t < "+end;
            Count(sql,"notifications.delivery",notifications,"1",start,end,stamp);
            Temporal(sql,"forums.posts","forumthreads","createdutc","forum","forumid",start,end,stamp);
            Count(sql,"forums.replies","SELECT c.*,date_trunc('hour',c.createdutc) bucket,jsonb_build_object('forum',t.forumid) dimensions FROM comments c JOIN forumthreads t ON t.propertyid="+_Pid+" AND t.id=c.targetid WHERE c.propertyid="+_Pid+" AND c.targettype='ForumThread' AND c.createdutc >= "+start+" AND c.createdutc < "+end,"1",start,end,stamp);
            Count(sql,"events.attendance","SELECT date_trunc('hour',e.endutc) bucket,jsonb_build_object('status',CASE WHEN r.checkedinutc IS NOT NULL THEN 'CheckedIn' ELSE 'NoShow' END) dimensions FROM eventrsvps r JOIN events e ON e.propertyid="+_Pid+" AND e.id=r.eventid WHERE r.propertyid="+_Pid+" AND r.status='Going' AND e.status<>'Canceled' AND e.endutc >= "+start+" AND e.endutc < "+end,"1",start,end,stamp);
            Count(sql,"auth.problems","SELECT date_trunc('hour',createdutc) bucket,jsonb_build_object('kind',CASE WHEN statuscode=429 THEN 'RateLimited' WHEN statuscode=423 THEN 'LockedOut' WHEN path LIKE '%reset%' THEN 'PasswordReset' ELSE 'FailedSignIn' END) dimensions FROM requesthistory WHERE propertyid="+_Pid+" AND "+window+" AND path LIKE '/api/v1/auth/%' AND ((statuscode IN(401,423,429)) OR (path LIKE '%reset%' AND statuscode<400))","1",start,end,stamp);
            string replies="SELECT t.createdutc,date_trunc('hour',first.replyutc) bucket,jsonb_build_object('forum',t.forumid) dimensions,extract(epoch FROM (first.replyutc-t.createdutc))*1000 duration FROM forumthreads t CROSS JOIN LATERAL (SELECT MIN(c.createdutc) replyutc FROM comments c WHERE c.propertyid="+_Pid+" AND c.targettype='ForumThread' AND c.targetid=t.id AND c.authoruserid<>t.authoruserid AND c.deletedutc IS NULL) first WHERE t.propertyid="+_Pid+" AND first.replyutc IS NOT NULL AND first.replyutc >= "+start+" AND first.replyutc < "+end;
            Distribution(sql,"forums.replyTime",replies,"duration",start,end,stamp);
            await _Database.ExecuteQueriesAsync(sql,true,token).ConfigureAwait(false);
            return sql.Count/2;
        }
        /// <summary>Capture current lifecycle gauges without inventing historical snapshots.</summary>
        public async Task<int> SnapshotAsync(DateTime now,CancellationToken token=default,string timeZone="UTC",DayOfWeek weekStart=DayOfWeek.Monday)
        {
            string bucket=Sanitizer.Ts(MetricsStore.Hour(now)),stamp=Sanitizer.Ts(now);
            string zone=Sanitizer.Str(timeZone);int shift=((int)weekStart+6)%7;
            string Week(string timestamp) => "(date_trunc('week',"+timestamp+" AT TIME ZONE "+zone+" - interval '"+shift+" days') + interval '"+shift+" days')";
            string currentWeek=Week(stamp),cohortWeek=Week("createdutc");
            List<string> sql=new List<string>();
            Snapshot(sql,"directory.listings","SELECT jsonb_build_object('status',status) dimensions,count(*)::double precision value,count(*) samples FROM venues WHERE propertyid="+_Pid+" GROUP BY status",bucket,stamp);
            Snapshot(sql,"moderation.backlog","SELECT jsonb_build_object('reason',reason) dimensions,count(*)::double precision value,count(*) samples FROM reports WHERE propertyid="+_Pid+" AND resolvedutc IS NULL GROUP BY reason",bucket,stamp);
            Snapshot(sql,"support.backlog","SELECT jsonb_build_object('type',typekey) dimensions,count(*)::double precision value,count(*) samples FROM tickets WHERE propertyid="+_Pid+" AND status NOT IN ('Resolved','Closed') GROUP BY typekey",bucket,stamp);
            Snapshot(sql,"members.membership","SELECT jsonb_build_object('status',status,'store',store) dimensions,count(DISTINCT userid)::double precision value,count(DISTINCT userid) samples FROM subscriptions WHERE propertyid="+_Pid+" GROUP BY status,store UNION ALL SELECT jsonb_build_object('status','Active','store',source),count(DISTINCT userid)::double precision,count(DISTINCT userid) FROM entitlements WHERE propertyid="+_Pid+" AND source<>'Subscription' AND revokedutc IS NULL AND startsutc <= "+stamp+" AND (endsutc IS NULL OR endsutc > "+stamp+") GROUP BY source",bucket,stamp);
            Snapshot(sql,"realtime.connections","SELECT '{}'::jsonb dimensions,count(*)::double precision value,count(*) samples FROM realtimeconnections WHERE propertyid="+_Pid+" AND lastseenutc > ("+stamp+" - interval '5 minutes')",bucket,stamp);
            Snapshot(sql,"ai.health","SELECT jsonb_build_object('endpoint',id) dimensions,CASE WHEN healthstatus='Healthy' THEN 100.0 ELSE 0.0 END value,1::bigint samples FROM aiendpoints WHERE propertyid="+_Pid+" AND enabled AND deletedutc IS NULL",bucket,stamp);
            Snapshot(sql,"workers.lag","SELECT jsonb_build_object('worker',worker) dimensions,extract(epoch FROM ("+stamp+"-MAX(endedutc)))*1000 value,count(*) samples FROM metricworkerruns WHERE propertyid="+_Pid+" AND outcome='Ok' GROUP BY worker",bucket,stamp);
            Snapshot(sql,"forums.unanswered","SELECT jsonb_build_object('forum',forumid) dimensions,count(*)::double precision value,count(*) samples FROM forumthreads WHERE propertyid="+_Pid+" AND deletedutc IS NULL AND commentcount=0 GROUP BY forumid",bucket,stamp);
            Snapshot(sql,"members.profile","SELECT jsonb_build_object('field',f.key,'option',o.optionkey) dimensions,count(DISTINCT o.userid)::double precision value,count(DISTINCT o.userid) samples FROM profilevalueoptions o JOIN profilefields f ON f.propertyid="+_Pid+" AND f.id=o.fieldid WHERE o.propertyid="+_Pid+" AND f.enabled AND f.type IN ('Select','MultiSelect','SingleSelect') GROUP BY f.key,o.optionkey",bucket,stamp);
            Snapshot(sql,"members.retention","WITH cohorts AS (SELECT id,"+cohortWeek+" cohort FROM users WHERE propertyid="+_Pid+" AND NOT isdeleted AND createdutc >= (("+currentWeek+"-interval '12 weeks') AT TIME ZONE "+zone+") AND createdutc <= "+stamp+"), active AS (SELECT DISTINCT userid FROM usageactivity WHERE propertyid="+_Pid+" AND hourutc >= ("+currentWeek+" AT TIME ZONE "+zone+") AND hourutc < "+stamp+") SELECT jsonb_build_object('cohort',to_char(cohort,'YYYY-MM-DD'),'week',(("+currentWeek+"::date-cohort::date)/7)::text) dimensions,100.0*count(a.userid)/count(*) value,count(*) samples FROM cohorts c LEFT JOIN active a ON a.userid=c.id GROUP BY cohort",bucket,stamp);
            Snapshot(sql,"contributors.share","WITH contributions AS (SELECT authoruserid,count(*) n FROM (SELECT authoruserid FROM feedposts WHERE propertyid="+_Pid+" AND deletedutc IS NULL AND createdutc >= ("+stamp+" - interval '30 days') UNION ALL SELECT authoruserid FROM comments WHERE propertyid="+_Pid+" AND deletedutc IS NULL AND createdutc >= ("+stamp+" - interval '30 days')) c GROUP BY authoruserid), ranked AS (SELECT n,row_number() OVER(ORDER BY n DESC) rank,count(*) OVER() members,SUM(n) OVER() total FROM contributions) SELECT jsonb_build_object('percentile',p::text) dimensions,100.0*SUM(CASE WHEN rank<=CEIL(members*p/100.0) THEN n ELSE 0 END)/NULLIF(MAX(total),0) value,MAX(members) samples FROM ranked CROSS JOIN (VALUES(1),(10),(50)) fractions(p) GROUP BY p",bucket,stamp);
            Snapshot(sql,"storage.projected30Days","SELECT '{}'::jsonb dimensions,GREATEST(0,(array_agg(value ORDER BY periodstartutc DESC))[1]+COALESCE(regr_slope(value,extract(epoch FROM periodstartutc)/86400),0)*30) value,count(*) samples FROM usagehourly WHERE propertyid="+_Pid+" AND meter='storage.total' AND periodstartutc >= ("+stamp+"-interval '7 days') HAVING count(*)>=2",bucket,stamp);
            Snapshot(sql,"dependencies.health","SELECT jsonb_build_object('dependency','database') dimensions,100.0 value,1::bigint samples UNION ALL SELECT jsonb_build_object('dependency','email'),100.0*count(*) FILTER(WHERE status IN ('Sent','Delivered'))/NULLIF(count(*),0),count(*) FROM emailmessages WHERE propertyid="+_Pid+" AND createdutc >= ("+stamp+" - interval '1 hour') AND status IN ('Sent','Delivered','Failed') HAVING count(*)>0 UNION ALL SELECT jsonb_build_object('dependency','models'),100.0*count(*) FILTER(WHERE healthstatus='Healthy')/NULLIF(count(*),0),count(*) FROM aiendpoints WHERE propertyid="+_Pid+" AND enabled AND deletedutc IS NULL HAVING count(*)>0 UNION ALL SELECT jsonb_build_object('dependency','push'),100.0*count(*) FILTER(WHERE status='Ok')/NULLIF(count(*),0),count(*) FROM pushdeliveries WHERE propertyid="+_Pid+" AND receiptcheckedutc >= ("+stamp+"-interval '1 hour') AND status IN ('Ok','Error') HAVING count(*)>0 UNION ALL SELECT jsonb_build_object('dependency','storage'),100.0*count(*) FILTER(WHERE status='Ready')/NULLIF(count(*),0),count(*) FROM mediaattachments WHERE propertyid="+_Pid+" AND updatedutc >= ("+stamp+"-interval '1 hour') AND status IN ('Ready','Failed') HAVING count(*)>0",bucket,stamp);
            await _Database.ExecuteQueriesAsync(sql,true,token).ConfigureAwait(false);
            return sql.Count/2;
        }
        private string TemporalSource(string table,string time,string? dimension,string? column,string from,string to,string extra="")
        {
            return "SELECT *,date_trunc('hour',"+time+") bucket,"+(dimension==null?"'{}'::jsonb":"jsonb_build_object("+Sanitizer.Str(dimension)+","+column+")")+" dimensions FROM "+table+" WHERE propertyid="+_Pid+" AND "+time+" >= "+from+" AND "+time+" < "+to+extra;
        }
        private void Temporal(List<string> sql,string metric,string table,string time,string? dimension,string? column,string from,string to,string stamp,string extra="") { Count(sql,metric,TemporalSource(table,time,dimension,column,from,to,extra),"1",from,to,stamp); }
        private void Clear(List<string> sql,string metric,string from,string to) { sql.Add("DELETE FROM metricseries WHERE propertyid="+_Pid+" AND metric="+Sanitizer.Str(metric)+" AND bucketutc >= "+from+" AND bucketutc < "+to+";"); }
        private void Count(List<string> sql,string metric,string source,string value,string from,string to,string stamp)
        {
            Clear(sql,metric,from,to);sql.Add("INSERT INTO metricseries(propertyid,metric,bucketutc,dimensions,value,samples,histogram,computedutc) SELECT "+_Pid+","+Sanitizer.Str(metric)+",bucket,dimensions,SUM("+value+")::double precision,count(*),'{}'::jsonb,"+stamp+" FROM ("+source+") source GROUP BY bucket,dimensions;");
        }
        private void Distribution(List<string> sql,string metric,string source,string value,string from,string to,string stamp)
        {
            Clear(sql,metric,from,to);sql.Add("INSERT INTO metricseries(propertyid,metric,bucketutc,dimensions,value,samples,histogram,computedutc) SELECT "+_Pid+","+Sanitizer.Str(metric)+",bucket,dimensions,SUM(total),SUM(n),jsonb_object_agg(bin,n),"+stamp+" FROM (SELECT bucket,dimensions,LEAST(192,CEIL(LN(GREATEST(1.0,"+value+"))/LN(1.1)))::int bin,SUM("+value+") total,count(*) n FROM ("+source+") source GROUP BY bucket,dimensions,bin) bins GROUP BY bucket,dimensions;");
        }
        private void Snapshot(List<string> sql,string metric,string source,string bucket,string stamp)
        {
            Clear(sql,metric,bucket,"("+bucket+" + interval '1 hour')");sql.Add("INSERT INTO metricseries(propertyid,metric,bucketutc,dimensions,value,samples,histogram,computedutc) SELECT "+_Pid+","+Sanitizer.Str(metric)+","+bucket+",dimensions,value,samples,'{}'::jsonb,"+stamp+" FROM ("+source+") snapshot;");
        }
    }
}
