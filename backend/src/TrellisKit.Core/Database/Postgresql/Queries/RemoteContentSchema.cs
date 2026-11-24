namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// PostgreSQL statements of migration 80, "Remote strings, banners, assets, and update failure details"
    /// (docs/LIVE_UPDATES_CONTRACT.md sections 14, 16.3, and the as-built notes): the per-property copy of the engine
    /// string catalog, the temporal revision tables of remote string overrides, banners, and remote assets (banner
    /// targeting rules live in featureflagrules with ownertype Banner), and the private message and stack trace of
    /// client-reported failures used for symbolication. Every table has propertyid first and every index starts with it.
    /// </summary>
    internal static class RemoteContentSchema
    {
        internal const int Version = 80;

        internal const string Description = "Remote strings, banners, assets, and update failure details";

        internal static List<string> Statements()
        {
            return new List<string>
            {
                "CREATE TABLE IF NOT EXISTS appstringcatalog (propertyid TEXT NOT NULL, stringkey TEXT NOT NULL, client TEXT NOT NULL, defaultvalue TEXT NOT NULL, tokens TEXT NOT NULL, " +
                "maxlength INTEGER NOT NULL, catalogsha TEXT NOT NULL, PRIMARY KEY (propertyid, stringkey));",
                "CREATE INDEX IF NOT EXISTS idx_appstringcatalog_client ON appstringcatalog (propertyid, client, stringkey);",

                "CREATE TABLE IF NOT EXISTS remotestrings (propertyid TEXT NOT NULL, revisionid TEXT NOT NULL PRIMARY KEY, objectid TEXT NOT NULL, stringkey TEXT NOT NULL, value TEXT NULL, notes TEXT NULL, " +
                "state TEXT NOT NULL, validfromversion BIGINT NULL, validtoversion BIGINT NULL, isdraft BOOLEAN NOT NULL DEFAULT TRUE, editedbyuserid TEXT NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_remotestrings_current ON remotestrings (propertyid, stringkey) WHERE validtoversion IS NULL AND isdraft = FALSE;",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_remotestrings_draft ON remotestrings (propertyid, stringkey) WHERE isdraft = TRUE;",
                "CREATE INDEX IF NOT EXISTS idx_remotestrings_version ON remotestrings (propertyid, validfromversion, validtoversion);",

                "CREATE TABLE IF NOT EXISTS banners (propertyid TEXT NOT NULL, revisionid TEXT NOT NULL PRIMARY KEY, objectid TEXT NOT NULL, title TEXT NOT NULL, body TEXT NOT NULL, style TEXT NOT NULL, " +
                "placement TEXT NOT NULL, dismissible BOOLEAN NOT NULL DEFAULT TRUE, priority INTEGER NOT NULL DEFAULT 0, startsutc TIMESTAMPTZ NULL, endsutc TIMESTAMPTZ NULL, linklabel TEXT NULL, " +
                "linkpath TEXT NULL, linkurl TEXT NULL, imageassetobjectid TEXT NULL, state TEXT NOT NULL, validfromversion BIGINT NULL, validtoversion BIGINT NULL, isdraft BOOLEAN NOT NULL DEFAULT TRUE, " +
                "editedbyuserid TEXT NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_banners_current ON banners (propertyid, objectid) WHERE validtoversion IS NULL AND isdraft = FALSE;",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_banners_draft ON banners (propertyid, objectid) WHERE isdraft = TRUE;",
                "CREATE INDEX IF NOT EXISTS idx_banners_version ON banners (propertyid, validfromversion, validtoversion);",
                "CREATE TABLE IF NOT EXISTS bannerclients (propertyid TEXT NOT NULL, revisionid TEXT NOT NULL, client TEXT NOT NULL, PRIMARY KEY (propertyid, revisionid, client));",

                "CREATE TABLE IF NOT EXISTS remoteassets (propertyid TEXT NOT NULL, revisionid TEXT NOT NULL PRIMARY KEY, objectid TEXT NOT NULL, assetkey TEXT NOT NULL, contenttype TEXT NOT NULL, " +
                "sizebytes BIGINT NOT NULL DEFAULT 0, width INTEGER NULL, height INTEGER NULL, sha256hex TEXT NULL, darksha256hex TEXT NULL, darkwidth INTEGER NULL, darkheight INTEGER NULL, " +
                "darksizebytes BIGINT NULL, alttext TEXT NULL, state TEXT NOT NULL, validfromversion BIGINT NULL, validtoversion BIGINT NULL, isdraft BOOLEAN NOT NULL DEFAULT TRUE, editedbyuserid TEXT NULL, " +
                "createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_remoteassets_current ON remoteassets (propertyid, objectid) WHERE validtoversion IS NULL AND isdraft = FALSE;",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_remoteassets_draft ON remoteassets (propertyid, objectid) WHERE isdraft = TRUE;",
                "CREATE INDEX IF NOT EXISTS idx_remoteassets_version ON remoteassets (propertyid, validfromversion, validtoversion);",
                "CREATE INDEX IF NOT EXISTS idx_remoteassets_sha ON remoteassets (propertyid, sha256hex);",

                "CREATE TABLE IF NOT EXISTS updateeventdetails (propertyid TEXT NOT NULL, eventid TEXT NOT NULL PRIMARY KEY, updateuuid TEXT NULL, type TEXT NOT NULL, errorcode TEXT NULL, message TEXT NULL, " +
                "stacktrace TEXT NULL, occurredutc TIMESTAMPTZ NOT NULL, receivedutc TIMESTAMPTZ NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_updateeventdetails_update ON updateeventdetails (propertyid, updateuuid, occurredutc DESC);",
                "CREATE INDEX IF NOT EXISTS idx_updateeventdetails_prune ON updateeventdetails (propertyid, receivedutc);",
                "CREATE INDEX IF NOT EXISTS idx_updateassets_gc ON updateassets (propertyid, lastreferencedutc);"
            };
        }
    }
}
