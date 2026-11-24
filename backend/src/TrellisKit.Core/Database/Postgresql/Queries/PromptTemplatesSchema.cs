namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;

    /// <summary>
    /// PostgreSQL statements of migration 98, "AI prompt templates": the administrator's current text of each prompt in
    /// the prompt catalog (aiprompttemplates, one row per key; no row means the property or engine default) and every
    /// saved version (aiprompttemplateversions). docs/INTEGRATION.md owner requirement R7 and section 30.7.
    /// </summary>
    internal static class PromptTemplatesSchema
    {
        internal const int Version = 98;

        internal const string Description = "AI prompt templates";

        internal static List<string> Statements()
        {
            return new List<string>
            {
                "CREATE TABLE IF NOT EXISTS aiprompttemplates (propertyid TEXT NOT NULL, key TEXT NOT NULL, content TEXT NOT NULL, version INTEGER NOT NULL, " +
                    "updatedbyuserid TEXT NULL, updatedutc TIMESTAMPTZ NOT NULL, PRIMARY KEY (propertyid, key));",
                "CREATE TABLE IF NOT EXISTS aiprompttemplateversions (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, key TEXT NOT NULL, version INTEGER NOT NULL, " +
                    "content TEXT NOT NULL, isreset BOOLEAN NOT NULL DEFAULT FALSE, createdbyuserid TEXT NULL, createdutc TIMESTAMPTZ NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_aiprompttemplateversions_key ON aiprompttemplateversions (propertyid, key, createdutc DESC, id DESC);"
            };
        }
    }
}
