namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;

    /// <summary>
    /// PostgreSQL statements of migration 85, "Idea purge": ideas removed by a moderator (or deleted) are purged by the
    /// content purge worker after moderation.retainRemovedDays like posts, threads, and comments, so ideas gain
    /// purgedutc and a partial index for the worker's candidate query.
    /// </summary>
    internal static class IdeaPurgeSchema
    {
        internal const int Version = 85;

        internal const string Description = "Idea purge";

        internal static List<string> Statements()
        {
            return new List<string>
            {
                "ALTER TABLE ideas ADD COLUMN IF NOT EXISTS purgedutc TIMESTAMPTZ NULL;",
                "CREATE INDEX IF NOT EXISTS idx_ideas_purge ON ideas (propertyid, id) WHERE purgedutc IS NULL AND (deletedutc IS NOT NULL OR moderationstate = 'Removed');"
            };
        }
    }
}
