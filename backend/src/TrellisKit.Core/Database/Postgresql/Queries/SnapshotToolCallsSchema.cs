namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;

    /// <summary>
    /// PostgreSQL statements of migration 90, "Moderation snapshot tool calls": the moderation copy of a reported or
    /// withheld assistant reply keeps the reply's tool calls (name, arguments, status, bounded result summary, counts,
    /// and result links as JSON), so moderators see them after the member deletes the conversation
    /// (docs/AI_CONTRACT.md section 12.3).
    /// </summary>
    internal static class SnapshotToolCallsSchema
    {
        internal const int Version = 90;

        internal const string Description = "Moderation snapshot tool calls";

        internal static List<string> Statements()
        {
            return new List<string>
            {
                "CREATE TABLE IF NOT EXISTS aimoderationsnapshottoolcalls (propertyid TEXT NOT NULL, snapshotid TEXT NOT NULL, position INTEGER NOT NULL, toolcallid TEXT NOT NULL, " +
                "round INTEGER NOT NULL, toolname TEXT NOT NULL, argumentsjson TEXT NOT NULL, status TEXT NOT NULL, resultsummary TEXT NULL, resultcount INTEGER NOT NULL DEFAULT 0, " +
                "resultcharacters INTEGER NOT NULL DEFAULT 0, durationms BIGINT NOT NULL DEFAULT 0, error TEXT NULL, resultsjson TEXT NOT NULL DEFAULT '[]', createdutc TIMESTAMPTZ NOT NULL, " +
                "PRIMARY KEY (propertyid, snapshotid, position));"
            };
        }
    }
}
