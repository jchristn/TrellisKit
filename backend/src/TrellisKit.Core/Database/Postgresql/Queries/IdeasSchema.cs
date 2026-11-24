namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// PostgreSQL statements of migration 63, "Ideas board" (docs/PHASE5_CONTRACT.md sections 7 and 16). Every table has
    /// propertyid first and every index starts with propertyid.
    /// </summary>
    internal static class IdeasSchema
    {
        internal const int Version = 63;

        internal const string Description = "Ideas board";

        internal static List<string> Statements()
        {
            string listed = "deletedutc IS NULL AND mergedintoid IS NULL AND moderationstate = 'None'";
            return new List<string>
            {
                "CREATE TABLE IF NOT EXISTS ideastatuses (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, key TEXT NOT NULL, label TEXT NOT NULL, color TEXT NULL, " +
                "isclosed BOOLEAN NOT NULL DEFAULT FALSE, isinitial BOOLEAN NOT NULL DEFAULT FALSE, sortorder INTEGER NOT NULL DEFAULT 0, isretired BOOLEAN NOT NULL DEFAULT FALSE, " +
                "createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_ideastatuses_key ON ideastatuses (propertyid, lower(key));",
                "CREATE INDEX IF NOT EXISTS idx_ideastatuses_order ON ideastatuses (propertyid, isretired, sortorder, id);",

                "CREATE TABLE IF NOT EXISTS ideas (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, title TEXT NOT NULL, body TEXT NOT NULL, authoruserid TEXT NULL, " +
                "showauthor BOOLEAN NOT NULL DEFAULT TRUE, statuskey TEXT NOT NULL, statusnote TEXT NULL, statusutc TIMESTAMPTZ NULL, votecount INTEGER NOT NULL DEFAULT 0, " +
                "recentvotecount INTEGER NOT NULL DEFAULT 0, mergedintoid TEXT NULL, sourceticketid TEXT NULL, audience TEXT NOT NULL, islocked BOOLEAN NOT NULL DEFAULT FALSE, " +
                "commentcount INTEGER NOT NULL DEFAULT 0, reactiontotal INTEGER NOT NULL DEFAULT 0, openreportcount INTEGER NOT NULL DEFAULT 0, moderationstate TEXT NOT NULL DEFAULT 'None', " +
                "moderatedutc TIMESTAMPTZ NULL, moderatedbyuserid TEXT NULL, moderationreason TEXT NULL, createdbyuserid TEXT NULL, editedutc TIMESTAMPTZ NULL, deletedutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_ideas_top ON ideas (propertyid, votecount DESC, id DESC) WHERE " + listed + ";",
                "CREATE INDEX IF NOT EXISTS idx_ideas_trending ON ideas (propertyid, recentvotecount DESC, id DESC) WHERE " + listed + ";",
                "CREATE INDEX IF NOT EXISTS idx_ideas_status ON ideas (propertyid, statuskey, votecount DESC);",
                "CREATE INDEX IF NOT EXISTS idx_ideas_created ON ideas (propertyid, createdutc DESC, id DESC);",
                "CREATE INDEX IF NOT EXISTS idx_ideas_author ON ideas (propertyid, authoruserid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_ideas_merged ON ideas (propertyid, mergedintoid) WHERE mergedintoid IS NOT NULL;",
                "CREATE INDEX IF NOT EXISTS idx_ideas_updated ON ideas (propertyid, updatedutc DESC, id DESC);",
                "CREATE INDEX IF NOT EXISTS idx_ideas_ticket ON ideas (propertyid, sourceticketid) WHERE sourceticketid IS NOT NULL;",

                "CREATE TABLE IF NOT EXISTS ideavotes (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, ideaid TEXT NOT NULL, userid TEXT NOT NULL, createdutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_ideavotes ON ideavotes (propertyid, ideaid, userid);",
                "CREATE INDEX IF NOT EXISTS idx_ideavotes_recent ON ideavotes (propertyid, ideaid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_ideavotes_user ON ideavotes (propertyid, userid, createdutc);",

                "CREATE TABLE IF NOT EXISTS ideafollows (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, ideaid TEXT NOT NULL, userid TEXT NOT NULL, source TEXT NOT NULL, createdutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_ideafollows ON ideafollows (propertyid, ideaid, userid);",
                "CREATE INDEX IF NOT EXISTS idx_ideafollows_user ON ideafollows (propertyid, userid);"
            };
        }
    }
}
