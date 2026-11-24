namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;

    /// <summary>Property-scoped aggregate metrics, worker history, annotations, and alert state.</summary>
    internal static class MetricsSchema
    {
        internal const int Version = 120;
        internal const string Description = "Generic metrics, snapshots, annotations, and worker history";
        internal static List<string> Statements()
        {
            return new List<string>
            {
                "ALTER TABLE requesthistory ADD COLUMN IF NOT EXISTS metricroute TEXT NOT NULL DEFAULT '', ADD COLUMN IF NOT EXISTS metricerror TEXT NOT NULL DEFAULT '', ADD COLUMN IF NOT EXISTS metricapp TEXT NOT NULL DEFAULT '', ADD COLUMN IF NOT EXISTS metricplatform TEXT NOT NULL DEFAULT '', ADD COLUMN IF NOT EXISTS metricversion TEXT NOT NULL DEFAULT '';",
                "CREATE TABLE IF NOT EXISTS metricseries (propertyid TEXT NOT NULL, metric TEXT NOT NULL, bucketutc TIMESTAMPTZ NOT NULL, dimensions JSONB NOT NULL DEFAULT '{}', value DOUBLE PRECISION NOT NULL, samples BIGINT NOT NULL DEFAULT 1, histogram JSONB NOT NULL DEFAULT '{}', complete BOOLEAN NOT NULL DEFAULT FALSE, computedutc TIMESTAMPTZ NOT NULL, PRIMARY KEY(propertyid,metric,bucketutc,dimensions));",
                "CREATE INDEX IF NOT EXISTS idx_metricseries_time ON metricseries(propertyid,bucketutc,metric);",
                "CREATE TABLE IF NOT EXISTS metricannotations (propertyid TEXT NOT NULL, id TEXT NOT NULL, kind TEXT NOT NULL, label TEXT NOT NULL, occurredutc TIMESTAMPTZ NOT NULL, PRIMARY KEY(propertyid,id));",
                "CREATE INDEX IF NOT EXISTS idx_metricannotations_time ON metricannotations(propertyid,occurredutc);",
                "CREATE TABLE IF NOT EXISTS metricworkerruns (propertyid TEXT NOT NULL, id TEXT NOT NULL, worker TEXT NOT NULL, startedutc TIMESTAMPTZ NOT NULL, endedutc TIMESTAMPTZ NOT NULL, outcome TEXT NOT NULL, items INTEGER NOT NULL, error TEXT NULL, intervalseconds DOUBLE PRECISION NOT NULL, PRIMARY KEY(propertyid,id));",
                "CREATE INDEX IF NOT EXISTS idx_metricworkerruns_time ON metricworkerruns(propertyid,startedutc,worker);",
                "CREATE TABLE IF NOT EXISTS metricalertstate (propertyid TEXT NOT NULL, key TEXT NOT NULL, streak INTEGER NOT NULL DEFAULT 0, active BOOLEAN NOT NULL DEFAULT FALSE, notifiedutc TIMESTAMPTZ NULL, updatedutc TIMESTAMPTZ NOT NULL, PRIMARY KEY(propertyid,key));",
                "CREATE TABLE IF NOT EXISTS metricstate (propertyid TEXT NOT NULL PRIMARY KEY, completethroughutc TIMESTAMPTZ NULL, updatedutc TIMESTAMPTZ NOT NULL);"
            };
        }
    }
}
