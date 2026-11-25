namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Migration 135, "Lesson and catalog links and library search indexes": lessoncatalogentries (lce_), at most 50 per
    /// lesson and unique per lesson and entry, with labels and tags in the shared labels and tags tables (target type
    /// LessonCatalogEntry); a covering index for the Training "New for you" query; and, when the pg_trgm extension is
    /// installed in the database, trigram GIN indexes on the series and lesson columns that the library search matches
    /// (lower(COALESCE(column, '')) LIKE, see SqlDialect.Contains). The extension is created outside migrations (Docker init
    /// script, infrastructure bootstrap, test harness) because CREATE EXTENSION needs elevated rights; without it the
    /// trigram indexes are skipped and search keeps working with sequential scans.
    /// </summary>
    internal static class LessonCatalogSchema
    {
        #region Internal-Members

        internal const int Version = 135;

        internal const string Description = "Lesson and catalog links and library search indexes";

        #endregion

        #region Private-Members

        private static readonly string[] _TrigramColumns = new[]
        {
            "series:title", "series:summary", "series:description", "lessons:title", "lessons:summary", "lessons:description"
        };

        #endregion

        #region Internal-Methods

        internal static List<string> Statements()
        {
            List<string> statements = new List<string>
            {
                "CREATE TABLE IF NOT EXISTS lessoncatalogentries (id TEXT PRIMARY KEY, propertyid TEXT NOT NULL, lessonid TEXT NOT NULL, catalogentryid TEXT NOT NULL, " +
                    "position INTEGER NOT NULL CHECK (position BETWEEN 1 AND 50), startseconds INTEGER NULL CHECK (startseconds IS NULL OR startseconds BETWEEN 0 AND 86400), " +
                    "source TEXT NOT NULL CHECK (source IN ('Editor', 'Suggested')), createdby TEXT NOT NULL, createdutc TIMESTAMPTZ NOT NULL, UNIQUE (propertyid, lessonid, catalogentryid));",
                "CREATE INDEX IF NOT EXISTS lessoncatalogentries_lesson ON lessoncatalogentries (propertyid, lessonid, position, id);",
                "CREATE INDEX IF NOT EXISTS lessoncatalogentries_entry ON lessoncatalogentries (propertyid, catalogentryid, position);",
                "CREATE INDEX IF NOT EXISTS idx_lessons_series_published ON lessons (propertyid, seriesid, publishedutc DESC) WHERE deletedutc IS NULL;"
            };
            List<string> trigram = new List<string>();
            foreach (string pair in _TrigramColumns)
            {
                string[] parts = pair.Split(':');
                trigram.Add("EXECUTE format('CREATE INDEX IF NOT EXISTS " + "idx_" + parts[0] + "_" + parts[1] + "_trgm ON " + parts[0] +
                    " USING gin (lower(COALESCE(" + parts[1] + ", '''')) %I.gin_trgm_ops)', s);");
            }
            statements.Add("DO $$ DECLARE s TEXT; BEGIN " +
                "SELECT n.nspname INTO s FROM pg_extension e JOIN pg_namespace n ON n.oid = e.extnamespace WHERE e.extname = 'pg_trgm'; " +
                "IF s IS NOT NULL THEN " + String.Join(" ", trigram) + " END IF; END $$;");
            return statements;
        }

        #endregion
    }
}
