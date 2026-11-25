namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;

    internal static class ExerciseCatalogSchema
    {
        internal const int Version = 127;
        internal const string Description = "Reviewed exercise and technique catalog";
        internal static List<string> Statements()
        {
            List<string> statements = new List<string>
            {
                "CREATE TABLE catalogentries (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, kind TEXT NOT NULL CHECK(kind IN ('Exercise','Technique')), name TEXT NOT NULL, slug TEXT NOT NULL, summary TEXT NULL, program TEXT NULL, difficulty TEXT NOT NULL, movementpattern TEXT NULL, plane TEXT NULL, laterality TEXT NULL, techniquetype TEXT NULL, fromposition TEXT NULL, toposition TEXT NULL, status TEXT NOT NULL DEFAULT 'Draft' CHECK(status IN ('Draft','InReview','Published','Rejected','Archived')), moderationstate TEXT NOT NULL DEFAULT 'None', revision BIGINT NOT NULL DEFAULT 1, createdbyuserid TEXT NULL, reviewedbyuserid TEXT NULL, reviewnote TEXT NULL, reviewedutc TIMESTAMPTZ NULL, publishedutc TIMESTAMPTZ NULL, deletedutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX ux_catalogentries_slug ON catalogentries(propertyid, lower(slug)) WHERE deletedutc IS NULL;",
                "CREATE INDEX idx_catalogentries_published ON catalogentries(propertyid,status,kind,program,lower(name),id) WHERE deletedutc IS NULL;",
                "CREATE TABLE catalogentryvalues (propertyid TEXT NOT NULL, entryid TEXT NOT NULL, field TEXT NOT NULL, position INTEGER NOT NULL CHECK(position BETWEEN 0 AND 49), value TEXT NOT NULL, PRIMARY KEY(propertyid,entryid,field,position));",
                "CREATE INDEX idx_catalogvalues_lookup ON catalogentryvalues(propertyid,field,lower(value),entryid);",
                "CREATE TABLE catalogsources (propertyid TEXT NOT NULL, entryid TEXT NOT NULL, position INTEGER NOT NULL CHECK(position BETWEEN 0 AND 19), url TEXT NULL, title TEXT NOT NULL, license TEXT NOT NULL, attribution TEXT NULL, method TEXT NOT NULL, model TEXT NULL, retrievedutc TIMESTAMPTZ NULL, PRIMARY KEY(propertyid,entryid,position));",
                "CREATE TABLE catalogrelationships (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, sourceid TEXT NOT NULL, targetid TEXT NOT NULL, kind TEXT NOT NULL, note TEXT NULL, createdutc TIMESTAMPTZ NOT NULL, UNIQUE(propertyid,sourceid,targetid,kind), CHECK(sourceid <> targetid));",
                "CREATE INDEX idx_catalogrelationships_source ON catalogrelationships(propertyid,sourceid,kind,id);",
                "CREATE INDEX idx_catalogrelationships_target ON catalogrelationships(propertyid,targetid,kind,id);",
            };
            statements.AddRange(new ContentChangeSource("catalogentries", "catalogentries", "CatalogEntry", "id", "createdbyuserid", "name,summary,movementpattern,plane,laterality,techniquetype,fromposition,toposition", "", "deletedutc").Statements());
            statements.AddRange(new ContentChangeSource("catalogsources", "catalogsources", "CatalogEntry", "entryid", "", "title,url,license,attribution", "", null).Statements());
            statements.AddRange(new ContentChangeSource("catalogentryvalues", "catalogentryvalues", "CatalogEntry", "entryid", "", "value", "", null).Statements());
            statements.AddRange(new ContentChangeSource("catalogrelationships", "catalogrelationships", "CatalogEntry", "sourceid", "", "note", "", null).Statements());
            return statements;
        }
    }
}
