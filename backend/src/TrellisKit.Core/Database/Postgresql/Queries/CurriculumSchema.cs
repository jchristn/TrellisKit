namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;

    /// <summary>
    /// Curricula (docs/TRELLISKIT.md, Ranks, curricula, and attendance): curricula, their items (lessons, series, catalog
    /// entry identifiers stored as text, and custom text), and members' self-completion and verification of items.
    /// </summary>
    internal static class CurriculumSchema
    {
        internal const int Version = 133;
        internal const string Description = "Curricula, items, and progress";

        internal static List<string> Statements() => new List<string>
        {
            "CREATE TABLE IF NOT EXISTS curricula (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL, description TEXT NULL, fieldkey TEXT NULL, visibility TEXT NOT NULL DEFAULT 'Members', " +
                "status TEXT NOT NULL DEFAULT 'Draft', position INTEGER NOT NULL DEFAULT 0, createdbyuserid TEXT NULL, publishedutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
            "CREATE INDEX IF NOT EXISTS idx_curricula_status ON curricula (propertyid, status, position, id);",
            "CREATE INDEX IF NOT EXISTS idx_curricula_created ON curricula (propertyid, createdutc DESC, id DESC);",
            "CREATE TABLE IF NOT EXISTS curriculumitems (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, curriculumid TEXT NOT NULL, optionkey TEXT NULL, itemtype TEXT NOT NULL, targetid TEXT NULL, " +
                "customtext TEXT NULL, position INTEGER NOT NULL DEFAULT 0, required BOOLEAN NOT NULL DEFAULT TRUE, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
            "CREATE INDEX IF NOT EXISTS idx_curriculumitems_curriculum ON curriculumitems (propertyid, curriculumid, position);",
            "CREATE INDEX IF NOT EXISTS idx_curriculumitems_target ON curriculumitems (propertyid, itemtype, targetid);",
            "CREATE TABLE IF NOT EXISTS curriculumprogress (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, curriculumid TEXT NOT NULL, itemid TEXT NOT NULL, userid TEXT NOT NULL, " +
                "status TEXT NOT NULL, verifiedby TEXT NULL, verifiedutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
            "CREATE UNIQUE INDEX IF NOT EXISTS ux_curriculumprogress_item ON curriculumprogress (propertyid, itemid, userid);",
            "CREATE INDEX IF NOT EXISTS idx_curriculumprogress_user ON curriculumprogress (propertyid, userid, curriculumid);"
        };
    }
}
