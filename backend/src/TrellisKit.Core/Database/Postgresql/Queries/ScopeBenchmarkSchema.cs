namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;

    /// <summary>
    /// PostgreSQL statements of migration 103, "Prompt drafts and job run summaries" (docs/INTEGRATION.md owner
    /// requirement R7, docs/BENCHMARKS.md): a prompt version is a draft until it is published (publishedutc), so a
    /// change goes live only after the scope benchmark gate; job runs carry the parameters they were queued with and the
    /// summary their handler writes when they finish (the scope benchmark's pass rates and prompt versions). Versions
    /// saved before this migration were live when saved, so they are marked published at their creation time.
    /// </summary>
    internal static class ScopeBenchmarkSchema
    {
        internal const int Version = 103;

        internal const string Description = "Prompt drafts and job run summaries";

        internal static List<string> Statements()
        {
            return new List<string>
            {
                "ALTER TABLE aiprompttemplateversions ADD COLUMN IF NOT EXISTS publishedutc TIMESTAMPTZ NULL;",
                "UPDATE aiprompttemplateversions SET publishedutc = createdutc WHERE publishedutc IS NULL;",
                "ALTER TABLE jobruns ADD COLUMN IF NOT EXISTS parameters TEXT NULL;",
                "ALTER TABLE jobruns ADD COLUMN IF NOT EXISTS summary TEXT NULL;"
            };
        }
    }
}
