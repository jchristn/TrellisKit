namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;

    /// <summary>
    /// PostgreSQL statements of migration 71, "Brand palettes and the property AI switch" (docs/TRELLISKIT.md sections
    /// 2.1 and 9.5): versioned brand palettes with one row per scheme and color key and the contrast overrides each
    /// version was saved with, and the per-property AI switch. Rows are seeded per property at startup from
    /// property.json, so nothing is inserted here. Every table has propertyid first and every index starts with
    /// propertyid. Versions 50 to 69 are reserved for Phases 4 and 5.
    /// </summary>
    internal static class BrandingSchema
    {
        internal const int Version = 71;

        internal const string Description = "Brand palettes and the property AI switch";

        internal static List<string> Statements()
        {
            return new List<string>
            {
                "CREATE TABLE IF NOT EXISTS propertybrandpalettes (propertyid TEXT NOT NULL, version INTEGER NOT NULL, source TEXT NOT NULL, note TEXT NULL, seedhash TEXT NULL, " +
                "createdbyuserid TEXT NULL, createdutc TIMESTAMPTZ NOT NULL, PRIMARY KEY (propertyid, version));",
                "CREATE TABLE IF NOT EXISTS propertybrandcolors (propertyid TEXT NOT NULL, version INTEGER NOT NULL, scheme TEXT NOT NULL, colorkey TEXT NOT NULL, value TEXT NOT NULL, " +
                "PRIMARY KEY (propertyid, version, scheme, colorkey));",
                "CREATE TABLE IF NOT EXISTS propertybrandoverrides (propertyid TEXT NOT NULL, version INTEGER NOT NULL, scheme TEXT NOT NULL, pair TEXT NOT NULL, " +
                "ratio DOUBLE PRECISION NOT NULL, required DOUBLE PRECISION NOT NULL, reason TEXT NULL, PRIMARY KEY (propertyid, version, scheme, pair));",
                "CREATE TABLE IF NOT EXISTS propertyai (propertyid TEXT NOT NULL PRIMARY KEY, enabled BOOLEAN NOT NULL DEFAULT FALSE, sourcehash TEXT NULL, " +
                "revision INTEGER NOT NULL DEFAULT 1, updatedbyuserid TEXT NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);"
            };
        }
    }
}
