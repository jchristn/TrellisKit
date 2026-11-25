namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;

    internal static class CatalogImportSchema
    {
        internal const int Version = 128;
        internal const string Description = "Reviewed bounded catalog import sources and draft provenance";
        internal static List<string> Statements()
        {
            return new List<string>
            {
                "CREATE TABLE catalogimportsources (propertyid TEXT NOT NULL,id TEXT NOT NULL PRIMARY KEY,name TEXT NOT NULL,url TEXT NOT NULL,license TEXT NOT NULL,attribution TEXT NULL,enabled BOOLEAN NOT NULL DEFAULT FALSE,maxentries INTEGER NOT NULL CHECK(maxentries BETWEEN 1 AND 100),revision BIGINT NOT NULL,reviewedutc TIMESTAMPTZ NULL,reviewedbyuserid TEXT NULL,lastattemptutc TIMESTAMPTZ NULL,createdutc TIMESTAMPTZ NOT NULL,updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX ux_catalogimportsources_url ON catalogimportsources(propertyid,url);",
                "CREATE INDEX idx_catalogimportsources_active ON catalogimportsources(propertyid,enabled,reviewedutc,id);",
                "CREATE TABLE catalogimportitems (propertyid TEXT NOT NULL,sourceid TEXT NOT NULL,itemkey TEXT NOT NULL,entryid TEXT NOT NULL,sourcehash TEXT NOT NULL,entryrevision BIGINT NOT NULL,sourcerevision BIGINT NOT NULL,updatedutc TIMESTAMPTZ NOT NULL,PRIMARY KEY(propertyid,sourceid,itemkey),UNIQUE(propertyid,entryid));"
            };
        }
    }
}
