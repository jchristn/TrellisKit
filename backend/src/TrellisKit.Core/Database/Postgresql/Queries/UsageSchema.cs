namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;

    /// <summary>
    /// PostgreSQL statements of migration 70, "Usage metering" (docs/USAGE_METERING_CONTRACT.md section 4.1): hourly and
    /// daily rollups, member activity hours, measured video rendition sizes, rollup state, and the (propertyid,
    /// createdutc) indexes the rollups read on source tables that lacked one. Every table has propertyid first and every
    /// index starts with propertyid. Versions 50 to 69 are reserved for Phases 4 and 5.
    /// </summary>
    internal static class UsageSchema
    {
        internal const int Version = 70;

        internal const string Description = "Usage metering";

        internal static List<string> Statements()
        {
            return new List<string>
            {
                "CREATE TABLE IF NOT EXISTS usagehourly (propertyid TEXT NOT NULL, meter TEXT NOT NULL, periodstartutc TIMESTAMPTZ NOT NULL, value BIGINT NOT NULL DEFAULT 0, " +
                "computedutc TIMESTAMPTZ NOT NULL, PRIMARY KEY (propertyid, meter, periodstartutc));",
                "CREATE INDEX IF NOT EXISTS idx_usagehourly_period ON usagehourly (propertyid, periodstartutc);",
                "CREATE TABLE IF NOT EXISTS usagedaily (propertyid TEXT NOT NULL, id TEXT NOT NULL, meter TEXT NOT NULL, day TEXT NOT NULL, timezone TEXT NOT NULL, " +
                "periodstartutc TIMESTAMPTZ NOT NULL, periodendutc TIMESTAMPTZ NOT NULL, value BIGINT NOT NULL DEFAULT 0, closed BOOLEAN NOT NULL DEFAULT FALSE, " +
                "computedutc TIMESTAMPTZ NOT NULL, createdutc TIMESTAMPTZ NOT NULL, PRIMARY KEY (propertyid, meter, day));",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_usagedaily_id ON usagedaily (propertyid, id);",
                "CREATE INDEX IF NOT EXISTS idx_usagedaily_period ON usagedaily (propertyid, periodstartutc, meter);",
                "CREATE INDEX IF NOT EXISTS idx_usagedaily_open ON usagedaily (propertyid, day) WHERE closed = FALSE;",
                "CREATE TABLE IF NOT EXISTS usageactivity (propertyid TEXT NOT NULL, userid TEXT NOT NULL, hourutc TIMESTAMPTZ NOT NULL, PRIMARY KEY (propertyid, hourutc, userid));",
                "CREATE TABLE IF NOT EXISTS usagemediasizes (propertyid TEXT NOT NULL, mediaid TEXT NOT NULL, sizebytes BIGINT NOT NULL DEFAULT 0, objectcount BIGINT NOT NULL DEFAULT 0, " +
                "measuredutc TIMESTAMPTZ NOT NULL, PRIMARY KEY (propertyid, mediaid));",
                "CREATE INDEX IF NOT EXISTS idx_usagemediasizes_measured ON usagemediasizes (propertyid, measuredutc);",
                "CREATE TABLE IF NOT EXISTS usagestate (propertyid TEXT NOT NULL PRIMARY KEY, timezone TEXT NOT NULL, backfillstartutc TIMESTAMPTZ NULL, backfillcursorutc TIMESTAMPTZ NULL, " +
                "hourlycursorutc TIMESTAMPTZ NULL, backfilledutc TIMESTAMPTZ NULL, lastrolluputc TIMESTAMPTZ NULL, lastflushutc TIMESTAMPTZ NULL, storagescanutc TIMESTAMPTZ NULL, " +
                "storagecursor TEXT NULL, storagemeasured BIGINT NOT NULL DEFAULT 0, storagedriftbytes BIGINT NOT NULL DEFAULT 0, storagemissing BIGINT NOT NULL DEFAULT 0, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_usage_feedposts_created ON feedposts (propertyid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_usage_forumthreads_created ON forumthreads (propertyid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_usage_comments_created ON comments (propertyid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_usage_reactions_created ON reactions (propertyid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_usage_pollvotes_created ON pollvotes (propertyid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_usage_forumfollows_created ON forumfollows (propertyid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_usage_threadfollows_created ON threadfollows (propertyid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_usage_media_created ON mediaattachments (propertyid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_usage_pushdeliveries_created ON pushdeliveries (propertyid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_usage_emailsends_created ON emailsends (propertyid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_usage_emailmessages_sent ON emailmessages (propertyid, sentutc) WHERE sentutc IS NOT NULL;",
                "CREATE INDEX IF NOT EXISTS idx_usage_aimessages_created ON aimessages (propertyid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_usage_authsessions_created ON authsessions (propertyid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_usage_authsessions_used ON authsessions (propertyid, lastusedutc);",
                "CREATE INDEX IF NOT EXISTS idx_usage_users_deleted ON users (propertyid, deletedutc) WHERE deletedutc IS NOT NULL;"
            };
        }
    }
}
