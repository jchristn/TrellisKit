namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;

    /// <summary>
    /// Phase 4 engagement migration 57 (docs/PHASE4_CONTRACT.md section 5.8): promoted lesson and event cards carry
    /// whether their source is live, so the feed hides a card while its source is hidden, removed, unpublished, or
    /// canceled.
    /// </summary>
    public static class EngagementSchema
    {
        #region Public-Members

        /// <summary>
        /// Migration version.
        /// </summary>
        public const int Version = 57;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Migration 57 statements.
        /// </summary>
        /// <returns>Statements.</returns>
        public static List<string> Statements()
        {
            return new List<string>
            {
                "ALTER TABLE feedposts ADD COLUMN IF NOT EXISTS sourcehidden BOOLEAN NOT NULL DEFAULT FALSE;",
                "CREATE INDEX IF NOT EXISTS idx_feedposts_source ON feedposts (propertyid, sourcetype, sourceid) WHERE sourceid IS NOT NULL;"
            };
        }

        #endregion
    }
}
