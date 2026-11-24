namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;

    /// <summary>
    /// PostgreSQL statements of migration 72, "Assistant tools" (docs/AI_CONTRACT.md section 8): the tool policy
    /// columns of aiassistants, the allowed tools per assistant, the stored tool calls of each reply with their result
    /// links, the property-wide kill switch per tool, and how each endpoint receives tools. Every new table has
    /// propertyid first and every index starts with propertyid.
    /// </summary>
    internal static class AiToolsSchema
    {
        internal const int Version = 72;

        internal const string Description = "Assistant tools";

        /// <summary>
        /// Tables created by this migration, in purge order (children before parents).
        /// </summary>
        internal static readonly string[] Tables = new[] { "aitoolcallresults", "aitoolcalls", "aitoolswitches", "aiassistanttools" };

        internal static List<string> Statements()
        {
            return new List<string>
            {
                "ALTER TABLE aiassistants ADD COLUMN IF NOT EXISTS toolsenabled BOOLEAN NOT NULL DEFAULT FALSE;",
                "ALTER TABLE aiassistants ADD COLUMN IF NOT EXISTS maxtooliterations INTEGER NOT NULL DEFAULT 4;",
                "ALTER TABLE aiassistants ADD COLUMN IF NOT EXISTS maxtoolcallsperturn INTEGER NOT NULL DEFAULT 6;",
                "ALTER TABLE aiassistants ADD COLUMN IF NOT EXISTS toolcalltimeoutms INTEGER NOT NULL DEFAULT 15000;",
                "ALTER TABLE aiassistants ADD COLUMN IF NOT EXISTS maxtooloutputcharacters INTEGER NOT NULL DEFAULT 8000;",
                "ALTER TABLE aiassistants ADD COLUMN IF NOT EXISTS maxtokensperturn INTEGER NOT NULL DEFAULT 32000;",
                "ALTER TABLE aiassistants ADD COLUMN IF NOT EXISTS showtoolactivity BOOLEAN NOT NULL DEFAULT TRUE;",
                "ALTER TABLE aiendpoints ADD COLUMN IF NOT EXISTS toolcalling TEXT NOT NULL DEFAULT 'Native';",
                "CREATE TABLE IF NOT EXISTS aiassistanttools (propertyid TEXT NOT NULL, assistantid TEXT NOT NULL, toolname TEXT NOT NULL, position INTEGER NOT NULL, " +
                "PRIMARY KEY (propertyid, assistantid, toolname));",
                "CREATE TABLE IF NOT EXISTS aitoolswitches (propertyid TEXT NOT NULL, toolname TEXT NOT NULL, disabled BOOLEAN NOT NULL, reason TEXT NULL, updatedbyuserid TEXT NULL, " +
                "updatedutc TIMESTAMPTZ NOT NULL, PRIMARY KEY (propertyid, toolname));",
                "CREATE TABLE IF NOT EXISTS aitoolcalls (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, messageid TEXT NOT NULL, position INTEGER NOT NULL, round INTEGER NOT NULL, " +
                "toolname TEXT NOT NULL, argumentsjson TEXT NOT NULL, status TEXT NOT NULL, resultsummary TEXT NULL, resultcount INTEGER NOT NULL DEFAULT 0, resultcharacters INTEGER NOT NULL DEFAULT 0, " +
                "durationms BIGINT NOT NULL DEFAULT 0, error TEXT NULL, createdutc TIMESTAMPTZ NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_aitoolcalls_message ON aitoolcalls (propertyid, messageid, position);",
                "CREATE INDEX IF NOT EXISTS idx_aitoolcalls_created ON aitoolcalls (propertyid, createdutc);",
                "CREATE TABLE IF NOT EXISTS aitoolcallresults (propertyid TEXT NOT NULL, toolcallid TEXT NOT NULL, position INTEGER NOT NULL, targettype TEXT NOT NULL, targetid TEXT NOT NULL, " +
                "title TEXT NOT NULL, path TEXT NOT NULL, PRIMARY KEY (propertyid, toolcallid, position));"
            };
        }
    }
}
