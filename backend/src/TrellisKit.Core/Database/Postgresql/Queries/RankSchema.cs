namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Verified ranks (docs/TRELLISKIT.md, Ranks, curricula, and attendance): the member's current verified rank per Rank
    /// profile field, every award and revoke, rank requirements, and self-reported lineage; the rank awarding rules of
    /// Rank profile fields; and the content changelog triggers on member-written promotion notes and lineage names.
    /// </summary>
    internal static class RankSchema
    {
        internal const int Version = 132;
        internal const string Description = "Verified ranks, promotions, requirements, and lineage";

        internal static List<string> Statements()
        {
            List<string> statements = new List<string>
            {
                "CREATE TABLE IF NOT EXISTS memberranks (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, userid TEXT NOT NULL, fieldkey TEXT NOT NULL, optionkey TEXT NOT NULL, level INTEGER NOT NULL DEFAULT 0, " +
                    "status TEXT NOT NULL DEFAULT 'Verified', promotionid TEXT NULL, awardedby TEXT NULL, awardedutc TIMESTAMPTZ NOT NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_memberranks_member ON memberranks (propertyid, userid, lower(fieldkey));",
                "CREATE INDEX IF NOT EXISTS idx_memberranks_field ON memberranks (propertyid, lower(fieldkey), status);",
                "CREATE TABLE IF NOT EXISTS rankpromotions (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, userid TEXT NOT NULL, fieldkey TEXT NOT NULL, optionkey TEXT NOT NULL, level INTEGER NOT NULL DEFAULT 0, " +
                    "action TEXT NOT NULL, actorid TEXT NULL, note TEXT NULL, eventid TEXT NULL, reason TEXT NULL, awardid TEXT NULL, revokedutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_rankpromotions_created ON rankpromotions (propertyid, createdutc DESC, id DESC);",
                "CREATE INDEX IF NOT EXISTS idx_rankpromotions_user ON rankpromotions (propertyid, userid, createdutc DESC, id DESC);",
                "CREATE INDEX IF NOT EXISTS idx_rankpromotions_actor ON rankpromotions (propertyid, actorid) WHERE actorid IS NOT NULL;",
                "CREATE TABLE IF NOT EXISTS rankrequirements (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, fieldkey TEXT NOT NULL, optionkey TEXT NOT NULL, level INTEGER NULL, " +
                    "minimumdaysingrade INTEGER NOT NULL DEFAULT 0, minimumattendance INTEGER NOT NULL DEFAULT 0, notes TEXT NULL, curriculumid TEXT NULL, position INTEGER NOT NULL DEFAULT 0, " +
                    "createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_rankrequirements_rank ON rankrequirements (propertyid, lower(fieldkey), lower(optionkey), COALESCE(level, -1));",
                "CREATE TABLE IF NOT EXISTS lineageentries (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, userid TEXT NOT NULL, fieldkey TEXT NOT NULL, optionkey TEXT NOT NULL, promotedbyuserid TEXT NULL, " +
                    "promotedbyname TEXT NULL, promotedutc TIMESTAMPTZ NULL, verified BOOLEAN NOT NULL DEFAULT FALSE, verifiedby TEXT NULL, verifiedutc TIMESTAMPTZ NULL, position INTEGER NOT NULL DEFAULT 0, " +
                    "createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_lineageentries_user ON lineageentries (propertyid, userid, lower(fieldkey), position);",
                "CREATE INDEX IF NOT EXISTS idx_lineageentries_promoter ON lineageentries (propertyid, promotedbyuserid) WHERE promotedbyuserid IS NOT NULL;",
                "ALTER TABLE profilefields ADD COLUMN IF NOT EXISTS rankawarding TEXT NULL;"
            };
            statements.AddRange(Sources.SelectMany(s => s.Statements()));
            return statements;
        }

        /// <summary>
        /// Member-written text on the rank tables (docs/INTEGRATION.md content review contract).
        /// </summary>
        internal static readonly IReadOnlyList<ContentChangeSource> Sources = new List<ContentChangeSource>
        {
            new ContentChangeSource("rankpromotions", "rankpromotions", "RankPromotion", "id", "actorid", "note,reason", "", null, "User", "userid", requireText: true),
            new ContentChangeSource("lineageentries", "lineageentries", "LineageEntry", "id", "userid", "promotedbyname", "", null, "User", "userid", requireText: true)
        };
    }
}
