namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System;

    /// <summary>Record real membership state transitions atomically with their source write. Reconciliation replays are no-ops.</summary>
    internal static class SubscriptionMetricsSql
    {
        internal static string Update(string updateSql, string propertyId, string subscriptionId, DateTime occurredUtc)
        {
            string pid = Sanitizer.Str(propertyId), id = Sanitizer.Str(subscriptionId), stamp = Sanitizer.Ts(occurredUtc);
            // The locked prior row is a dependency of the update, so simultaneous writes cannot count the same transition twice.
            return "WITH prior AS MATERIALIZED (SELECT status,autorenew FROM subscriptions WHERE propertyid=" + pid + " AND id=" + id + " FOR UPDATE), changed AS (" +
                updateSql.TrimEnd(';') + " AND EXISTS(SELECT 1 FROM prior) RETURNING status,autorenew,store), transitions AS (SELECT c.status,c.store,CASE " +
                "WHEN c.status<>p.status AND c.status IN ('Expired','Revoked','Refunded') THEN c.status " +
                "WHEN p.autorenew AND NOT c.autorenew AND c.status IN ('Active','InGrace','InBillingRetry','Paused') THEN 'Cancelled' ELSE NULL END kind FROM changed c CROSS JOIN prior p) " +
                "INSERT INTO metricseries(propertyid,metric,bucketutc,dimensions,value,samples,histogram,computedutc) SELECT " + pid + ",'members.lapses',date_trunc('hour'," + stamp + ")," +
                "jsonb_build_object('status',status,'transition',kind,'store',store),1,1,'{}'::jsonb," + stamp + " FROM transitions WHERE kind IS NOT NULL " +
                "ON CONFLICT(propertyid,metric,bucketutc,dimensions) DO UPDATE SET value=metricseries.value+1,samples=metricseries.samples+1,computedutc=EXCLUDED.computedutc;";
        }
    }
}
