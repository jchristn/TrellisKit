namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;

    /// <summary>
    /// PostgreSQL statements of migration 91, "Update object garbage collection": updategcobjects queues stored objects
    /// that may become unreferenced (private source maps of deleted draft updates, content asset images), so
    /// UpdateAssetGcWorker can delete them in bounded batches under its job lease. Content asset hashes already
    /// referenced by remote asset revisions are backfilled.
    /// </summary>
    internal static class UpdateGcSchema
    {
        internal const int Version = 91;

        internal const string Description = "Update object garbage collection";

        internal static List<string> Statements()
        {
            return new List<string>
            {
                "CREATE TABLE IF NOT EXISTS updategcobjects (propertyid TEXT NOT NULL, kind TEXT NOT NULL, sha256hex TEXT NOT NULL, storagekey TEXT NOT NULL, sizebytes BIGINT NOT NULL DEFAULT 0, " +
                "queuedutc TIMESTAMPTZ NOT NULL, PRIMARY KEY (propertyid, kind, sha256hex));",
                "CREATE INDEX IF NOT EXISTS idx_updategcobjects_due ON updategcobjects (propertyid, kind, queuedutc);",
                "CREATE INDEX IF NOT EXISTS idx_updatesourcemaps_sha ON updatesourcemaps (propertyid, sha256hex);",
                "CREATE INDEX IF NOT EXISTS idx_remoteassets_darksha ON remoteassets (propertyid, darksha256hex);",
                "INSERT INTO updategcobjects (propertyid, kind, sha256hex, storagekey, sizebytes, queuedutc) SELECT DISTINCT propertyid, 'ContentAsset', sha256hex, 'app-content/' || propertyid || '/' || sha256hex, 0, now() " +
                "FROM remoteassets WHERE sha256hex IS NOT NULL ON CONFLICT DO NOTHING;",
                "INSERT INTO updategcobjects (propertyid, kind, sha256hex, storagekey, sizebytes, queuedutc) SELECT DISTINCT propertyid, 'ContentAsset', darksha256hex, 'app-content/' || propertyid || '/' || darksha256hex, 0, now() " +
                "FROM remoteassets WHERE darksha256hex IS NOT NULL ON CONFLICT DO NOTHING;"
            };
        }
    }
}
