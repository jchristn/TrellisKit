namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;

    /// <summary>
    /// PostgreSQL statements of migration 74, "Property documents and acceptances" (docs/TRELLISKIT.md section 14):
    /// versioned property documents and members' acceptances of them. Versions are seeded per property at startup from
    /// the property folder, so nothing is inserted here. Every table has propertyid first and every index starts with
    /// propertyid. Versions 72 and 73 are left free for work in progress elsewhere.
    /// </summary>
    internal static class DocumentsSchema
    {
        internal const int Version = 74;

        internal const string Description = "Property documents and acceptances";

        internal static List<string> Statements()
        {
            return new List<string>
            {
                "CREATE TABLE IF NOT EXISTS propertydocumentversions (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, documentkey TEXT NOT NULL, sequence INTEGER NOT NULL, " +
                "version TEXT NOT NULL, title TEXT NOT NULL, content TEXT NOT NULL, changesummary TEXT NULL, requiresreacceptance BOOLEAN NOT NULL DEFAULT TRUE, source TEXT NOT NULL, " +
                "seedhash TEXT NULL, createdbyuserid TEXT NULL, createdutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_docversions_sequence ON propertydocumentversions (propertyid, documentkey, sequence);",
                "CREATE TABLE IF NOT EXISTS propertydocumentacceptances (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, userid TEXT NOT NULL, documentkey TEXT NOT NULL, " +
                "versionid TEXT NOT NULL, version TEXT NOT NULL, sequence INTEGER NOT NULL, title TEXT NOT NULL, method TEXT NOT NULL, client TEXT NOT NULL, " +
                "acceptedutc TIMESTAMPTZ NOT NULL, createdutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_docacceptances_member ON propertydocumentacceptances (propertyid, userid, documentkey, sequence);",
                "CREATE INDEX IF NOT EXISTS idx_docacceptances_document ON propertydocumentacceptances (propertyid, documentkey, sequence, userid);",
                "CREATE INDEX IF NOT EXISTS idx_docacceptances_accepted ON propertydocumentacceptances (propertyid, acceptedutc, id);"
            };
        }
    }
}
