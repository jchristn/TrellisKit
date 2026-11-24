namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;

    /// <summary>
    /// PostgreSQL statements of migration 100, "AI task roles and call ledger": executor hierarchies per task role
    /// (aitaskroles, aitaskroleexecutors) and one row per model call (aicalls). docs/INTEGRATION.md
    /// </summary>
    internal static class AiRolesSchema
    {
        internal const int Version = 100;

        internal const string Description = "AI task roles and call ledger";

        internal static List<string> Statements()
        {
            return new List<string>
            {
                "CREATE TABLE IF NOT EXISTS aitaskroles (propertyid TEXT NOT NULL, role TEXT NOT NULL, reasoningeffort TEXT NULL, availabilitypolicy TEXT NOT NULL DEFAULT 'FallThrough', " +
                    "exhaustionpolicy TEXT NOT NULL DEFAULT 'Fail', updatedbyuserid TEXT NULL, updatedutc TIMESTAMPTZ NOT NULL, PRIMARY KEY (propertyid, role));",
                "CREATE TABLE IF NOT EXISTS aitaskroleexecutors (propertyid TEXT NOT NULL, role TEXT NOT NULL, position INTEGER NOT NULL, runtime TEXT NOT NULL, endpointid TEXT NULL, model TEXT NULL, " +
                    "enabled BOOLEAN NOT NULL DEFAULT TRUE, PRIMARY KEY (propertyid, role, position));",
                "CREATE INDEX IF NOT EXISTS idx_aitaskroleexecutors_endpoint ON aitaskroleexecutors (propertyid, endpointid) WHERE endpointid IS NOT NULL;",
                "CREATE TABLE IF NOT EXISTS aicalls (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, createdutc TIMESTAMPTZ NOT NULL, purpose TEXT NOT NULL, role TEXT NULL, assistantid TEXT NULL, " +
                    "endpointid TEXT NULL, runtime TEXT NOT NULL, model TEXT NULL, executorposition INTEGER NULL, escalationreason TEXT NULL, outcome TEXT NOT NULL, errorcode TEXT NULL, " +
                    "isfallback BOOLEAN NOT NULL DEFAULT FALSE, istest BOOLEAN NOT NULL DEFAULT FALSE, prompttokens INTEGER NOT NULL DEFAULT 0, completiontokens INTEGER NOT NULL DEFAULT 0, " +
                    "reasoningtokens INTEGER NOT NULL DEFAULT 0, latencyms BIGINT NOT NULL DEFAULT 0, costmicros BIGINT NOT NULL DEFAULT 0, jobkey TEXT NULL, jobrunid TEXT NULL, jobitemid TEXT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_aicalls_created ON aicalls (propertyid, createdutc DESC, id DESC);",
                "CREATE INDEX IF NOT EXISTS idx_aicalls_role ON aicalls (propertyid, role, createdutc DESC) WHERE role IS NOT NULL;",
                "CREATE INDEX IF NOT EXISTS idx_aicalls_jobrun ON aicalls (propertyid, jobrunid) WHERE jobrunid IS NOT NULL;"
            };
        }
    }
}
