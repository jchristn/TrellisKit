namespace TrellisKit.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Postgresql;
    using TrellisKit.Core.Metrics;

    /// <summary>Default operational alert evaluations. Raw reads occur in the background worker only.</summary>
    public sealed class MetricAlertStore
    {
        private readonly DatabaseDriverBase _Database;
        private readonly string _Pid;
        /// <summary>Bind evaluations to one property.</summary>
        public MetricAlertStore(DatabaseDriverBase database)
        {
            _Database=database??throw new ArgumentNullException(nameof(database));
            _Pid=Sanitizer.Str(database.BoundPropertyId??throw new ArgumentException("Alerts require a property binding.",nameof(database)));
        }
        /// <summary>Evaluate the seven default signals. Unavailable measurements do not become failures.</summary>
        public async Task<Dictionary<string,bool>> EvaluateAsync(DateTime now,long? storageQuota,MetricAlertThresholds thresholds,CancellationToken token=default,string timeZone="UTC")
        {
            string stamp=Sanitizer.Ts(now),ten=Sanitizer.Ts(now.AddMinutes(-thresholds.SustainedMinutes));
            string zone=Sanitizer.Str(timeZone);
            string LocalDay(int days) => "((date_trunc('day',"+stamp+" AT TIME ZONE "+zone+")+interval '"+days+" days') AT TIME ZONE "+zone+")";
            string sql="SELECT 'errorRate' key,COALESCE(100.0*count(*) FILTER(WHERE statuscode>=500)/NULLIF(count(*),0)>"+Sanitizer.Num(thresholds.ServerErrorPercent)+",FALSE) breached FROM requesthistory WHERE propertyid="+_Pid+" AND createdutc >= "+ten+" AND createdutc < "+stamp+
                " UNION ALL SELECT 'latency',COALESCE(percentile_cont(.95) WITHIN GROUP(ORDER BY durationms)>"+Sanitizer.Num(thresholds.LatencyP95Ms)+",FALSE) FROM requesthistory WHERE propertyid="+_Pid+" AND createdutc >= "+ten+" AND createdutc < "+stamp+
                " UNION ALL SELECT 'endpointDown',EXISTS(SELECT 1 FROM aiendpoints WHERE propertyid="+_Pid+" AND enabled AND deletedutc IS NULL AND consecutivefailures>="+thresholds.EndpointFailedChecks+")"+
                " UNION ALL SELECT 'workerFailure',EXISTS(SELECT 1 FROM (SELECT worker,array_agg(outcome ORDER BY startedutc DESC) outcomes,MAX(endedutc) ended,(array_agg(intervalseconds ORDER BY startedutc DESC))[1] seconds FROM (SELECT *,row_number() OVER(PARTITION BY worker ORDER BY startedutc DESC) position FROM metricworkerruns WHERE propertyid="+_Pid+") recent WHERE position<="+thresholds.WorkerFailedPasses+" GROUP BY worker) w WHERE cardinality(outcomes)="+thresholds.WorkerFailedPasses+" AND outcomes <@ ARRAY['Failed'] OR extract(epoch FROM ("+stamp+"-ended))>"+thresholds.WorkerMissedIntervals+"*seconds)"+
                " UNION ALL SELECT 'rollupLag',EXISTS(SELECT 1 FROM metricstate WHERE propertyid="+_Pid+" AND completethroughutc < ("+stamp+"-interval '"+thresholds.RollupLagMinutes+" minutes'))"+
                " UNION ALL SELECT 'signupDrop',COALESCE((count(*) FILTER(WHERE createdutc >= "+LocalDay(-1)+")) < "+Sanitizer.Num(thresholds.SignupRatio)+"*(count(*) FILTER(WHERE createdutc < "+LocalDay(-1)+"))/14.0,FALSE) FROM users WHERE propertyid="+_Pid+" AND createdutc >= "+LocalDay(-15)+" AND createdutc < "+LocalDay(0);

            if(storageQuota.HasValue&&storageQuota>0)sql+=" UNION ALL SELECT 'storageProjection',COALESCE((array_agg(value ORDER BY periodstartutc DESC))[1]+GREATEST(0,COALESCE(regr_slope(value,extract(epoch FROM periodstartutc)/86400),0))*"+thresholds.StorageProjectionDays+">"+storageQuota.Value+",FALSE) FROM usagehourly WHERE propertyid="+_Pid+" AND meter='storage.total' AND periodstartutc >= ("+stamp+"-interval '7 days')";
            else sql+=" UNION ALL SELECT 'storageProjection',FALSE";
            DataTable rows=await _Database.ExecuteQueryAsync(sql+";",false,token).ConfigureAwait(false);
            return rows.Rows.Cast<DataRow>().ToDictionary(r=>RowReader.GetString(r,"key"),r=>RowReader.GetBool(r,"breached"),StringComparer.Ordinal);
        }
    }
}
