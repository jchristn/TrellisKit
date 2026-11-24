namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;

    /// <summary>
    /// PostgreSQL statements of migration 99, "Job engine": job definitions (one per registered handler), runs, and
    /// items with retries and a dead-letter state, claimed with FOR UPDATE SKIP LOCKED (docs/INTEGRATION.md).
    /// </summary>
    internal static class JobsSchema
    {
        internal const int Version = 99;

        internal const string Description = "Job engine";

        internal static List<string> Statements()
        {
            return new List<string>
            {
                "CREATE TABLE IF NOT EXISTS jobdefinitions (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, key TEXT NOT NULL, name TEXT NOT NULL, description TEXT NOT NULL DEFAULT '', " +
                    "enabled BOOLEAN NOT NULL DEFAULT FALSE, intervaltype TEXT NOT NULL, intervalvalue INTEGER NOT NULL DEFAULT 1, timeofdayminutesutc INTEGER NOT NULL DEFAULT 0, daysofweek TEXT NOT NULL DEFAULT '', " +
                    "maxconcurrency INTEGER NOT NULL DEFAULT 2, maxattempts INTEGER NOT NULL DEFAULT 3, budgetmicrosperrun BIGINT NULL, requiresharness BOOLEAN NOT NULL DEFAULT FALSE, " +
                    "nextrunutc TIMESTAMPTZ NULL, lastrunutc TIMESTAMPTZ NULL, lockeduntilutc TIMESTAMPTZ NULL, updatedbyuserid TEXT NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_jobdefinitions_key ON jobdefinitions (propertyid, key);",
                "CREATE INDEX IF NOT EXISTS idx_jobdefinitions_due ON jobdefinitions (propertyid, nextrunutc) WHERE enabled AND nextrunutc IS NOT NULL;",
                "CREATE TABLE IF NOT EXISTS jobruns (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, definitionid TEXT NOT NULL, key TEXT NOT NULL, trigger TEXT NOT NULL, status TEXT NOT NULL, " +
                    "isdryrun BOOLEAN NOT NULL DEFAULT FALSE, skippedreason TEXT NULL, error TEXT NULL, requestedbyuserid TEXT NULL, workerid TEXT NULL, createdutc TIMESTAMPTZ NOT NULL, " +
                    "startedutc TIMESTAMPTZ NULL, completedutc TIMESTAMPTZ NULL, itemstotal INTEGER NOT NULL DEFAULT 0, itemssucceeded INTEGER NOT NULL DEFAULT 0, itemsfailed INTEGER NOT NULL DEFAULT 0, " +
                    "itemsdead INTEGER NOT NULL DEFAULT 0, itemsskipped INTEGER NOT NULL DEFAULT 0, costmicros BIGINT NOT NULL DEFAULT 0);",
                "CREATE INDEX IF NOT EXISTS idx_jobruns_key ON jobruns (propertyid, key, createdutc DESC, id DESC);",
                "CREATE INDEX IF NOT EXISTS idx_jobruns_open ON jobruns (propertyid, status, createdutc) WHERE status IN ('Queued', 'Running');",
                "CREATE INDEX IF NOT EXISTS idx_jobruns_created ON jobruns (propertyid, createdutc DESC, id DESC);",
                "CREATE TABLE IF NOT EXISTS jobitems (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, runid TEXT NOT NULL, key TEXT NOT NULL, targetkey TEXT NOT NULL, payload TEXT NULL, " +
                    "status TEXT NOT NULL, attempts INTEGER NOT NULL DEFAULT 0, maxattempts INTEGER NOT NULL DEFAULT 3, nextattemptutc TIMESTAMPTZ NOT NULL, lockeduntilutc TIMESTAMPTZ NULL, workerid TEXT NULL, " +
                    "requiresharness BOOLEAN NOT NULL DEFAULT FALSE, result TEXT NULL, error TEXT NULL, costmicros BIGINT NOT NULL DEFAULT 0, createdutc TIMESTAMPTZ NOT NULL, startedutc TIMESTAMPTZ NULL, completedutc TIMESTAMPTZ NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_jobitems_target ON jobitems (propertyid, runid, targetkey);",
                "CREATE INDEX IF NOT EXISTS idx_jobitems_claim ON jobitems (propertyid, nextattemptutc) WHERE status = 'Pending';",
                "CREATE INDEX IF NOT EXISTS idx_jobitems_run ON jobitems (propertyid, runid, status);",
                "CREATE INDEX IF NOT EXISTS idx_jobitems_dead ON jobitems (propertyid, createdutc DESC, id DESC) WHERE status = 'Dead';"
            };
        }
    }
}
