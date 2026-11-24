namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;

    /// <summary>
    /// PostgreSQL statements of migration 107, "AI call telemetry, cached token prices, and harness profiles": the
    /// optional telemetry columns of the call ledger (docs/AI_TELEMETRY.md), the optional cache read and cache write
    /// prices of a model endpoint, and the administrator's profile per CLI agent harness runtime (docs/HARNESSES.md).
    /// </summary>
    internal static class AiTelemetrySchema
    {
        internal const int Version = 107;

        internal const string Description = "AI call telemetry, cached token prices, and harness profiles";

        internal static List<string> Statements()
        {
            return new List<string>
            {
                "ALTER TABLE aicalls " +
                    "ADD COLUMN IF NOT EXISTS timetofirsttokenms BIGINT NULL, ADD COLUMN IF NOT EXISTS timetolasttokenms BIGINT NULL, ADD COLUMN IF NOT EXISTS streamingms BIGINT NULL, ADD COLUMN IF NOT EXISTS queuems BIGINT NULL, " +
                    "ADD COLUMN IF NOT EXISTS tokenspersecond DOUBLE PRECISION NULL, ADD COLUMN IF NOT EXISTS totaltokens INTEGER NULL, ADD COLUMN IF NOT EXISTS cachedreadtokens INTEGER NULL, ADD COLUMN IF NOT EXISTS cachecreationtokens INTEGER NULL, " +
                    "ADD COLUMN IF NOT EXISTS inputtexttokens BIGINT NULL, ADD COLUMN IF NOT EXISTS inputimagetokens BIGINT NULL, ADD COLUMN IF NOT EXISTS inputaudiotokens BIGINT NULL, ADD COLUMN IF NOT EXISTS inputvideotokens BIGINT NULL, " +
                    "ADD COLUMN IF NOT EXISTS toolcalls INTEGER NULL, ADD COLUMN IF NOT EXISTS toolexecms BIGINT NULL, ADD COLUMN IF NOT EXISTS retries INTEGER NULL, ADD COLUMN IF NOT EXISTS statuscode INTEGER NULL, " +
                    "ADD COLUMN IF NOT EXISTS finishreason TEXT NULL, ADD COLUMN IF NOT EXISTS streamed BOOLEAN NULL, ADD COLUMN IF NOT EXISTS chunks INTEGER NULL, ADD COLUMN IF NOT EXISTS requestbytes BIGINT NULL, " +
                    "ADD COLUMN IF NOT EXISTS responsebytes BIGINT NULL, ADD COLUMN IF NOT EXISTS responseid TEXT NULL, ADD COLUMN IF NOT EXISTS providermodel TEXT NULL, ADD COLUMN IF NOT EXISTS reasoningeffort TEXT NULL, " +
                    "ADD COLUMN IF NOT EXISTS loadms BIGINT NULL, ADD COLUMN IF NOT EXISTS promptevalms BIGINT NULL, ADD COLUMN IF NOT EXISTS evalms BIGINT NULL, ADD COLUMN IF NOT EXISTS exitcode INTEGER NULL;",
                "ALTER TABLE aiendpoints ADD COLUMN IF NOT EXISTS cachereadpricepermillion DOUBLE PRECISION NULL, ADD COLUMN IF NOT EXISTS cachewritepricepermillion DOUBLE PRECISION NULL;",
                "CREATE TABLE IF NOT EXISTS aiharnessprofiles (propertyid TEXT NOT NULL, runtime TEXT NOT NULL, model TEXT NULL, permissionmode TEXT NULL, timeoutseconds INTEGER NULL, " +
                    "maxtoolcalls INTEGER NULL, allowwebresearch BOOLEAN NOT NULL DEFAULT TRUE, baseurl TEXT NULL, credentialtype TEXT NOT NULL DEFAULT 'Environment', credentialsecret TEXT NULL, " +
                    "updatedbyuserid TEXT NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL, PRIMARY KEY (propertyid, runtime));"
            };
        }
    }
}
