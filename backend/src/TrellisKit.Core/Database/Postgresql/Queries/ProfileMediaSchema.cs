namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;

    /// <summary>Owner-managed profile galleries with per-item moderation.</summary>
    internal static class ProfileMediaSchema
    {
        internal const int Version = 125;
        internal const string Description = "Profile multimedia galleries";
        internal static List<string> Statements() => new List<string>
        {
            "CREATE TABLE IF NOT EXISTS profilemedia (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, profileuserid TEXT NOT NULL, mediaid TEXT NOT NULL, uploaderuserid TEXT NOT NULL, kind TEXT NOT NULL, openreportcount INTEGER NOT NULL DEFAULT 0, moderationstate TEXT NOT NULL DEFAULT 'None', moderatedutc TIMESTAMPTZ NULL, moderatedbyuserid TEXT NULL, moderationreason TEXT NULL, deletedutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
            "CREATE UNIQUE INDEX IF NOT EXISTS ux_profilemedia_media ON profilemedia (propertyid, mediaid);",
            "CREATE INDEX IF NOT EXISTS idx_profilemedia_profile ON profilemedia (propertyid, profileuserid, createdutc DESC, id DESC) WHERE deletedutc IS NULL;",
            "CREATE INDEX IF NOT EXISTS idx_profilemedia_user ON profilemedia (propertyid, uploaderuserid);"
        };
    }
}
