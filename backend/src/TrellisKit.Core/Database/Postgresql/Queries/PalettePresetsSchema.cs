namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;

    /// <summary>
    /// PostgreSQL statements of migration 92, "Palette presets" (docs/TRELLISKIT.md section 2.1): which engine palette
    /// presets a property's members may choose (propertypalettechoices, one row per property; no row means every
    /// preset), and each member's preference (users.palettepreset, null for the property's brand palette).
    /// </summary>
    internal static class PalettePresetsSchema
    {
        internal const int Version = 92;

        internal const string Description = "Palette presets";

        internal static List<string> Statements()
        {
            return new List<string>
            {
                "CREATE TABLE IF NOT EXISTS propertypalettechoices (propertyid TEXT NOT NULL PRIMARY KEY, mode TEXT NOT NULL, presetids TEXT NOT NULL DEFAULT '', " +
                "revision INTEGER NOT NULL DEFAULT 1, updatedbyuserid TEXT NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NULL);",
                "ALTER TABLE users ADD COLUMN IF NOT EXISTS palettepreset TEXT NULL;"
            };
        }
    }
}
