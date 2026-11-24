namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;

    /// <summary>
    /// PostgreSQL statements of migration 94, "Public landing pages" (docs/PHASE4_CONTRACT.md section 18): the member's
    /// opt-in public profile page with its slug (profilepages, one row per member), the previous profile slugs that
    /// redirect to the current one (profileslugs, like venueslugs and eventslugs), and the indexes the sitemap queries use.
    /// </summary>
    internal static class PublicPagesSchema
    {
        internal const int Version = 94;

        internal const string Description = "Public landing pages";

        internal static List<string> Statements()
        {
            return new List<string>
            {
                "CREATE TABLE IF NOT EXISTS profilepages (propertyid TEXT NOT NULL, userid TEXT NOT NULL, enabled BOOLEAN NOT NULL DEFAULT FALSE, slug TEXT NULL, " +
                    "createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL, PRIMARY KEY (propertyid, userid));",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_profilepages_slug ON profilepages (propertyid, lower(slug)) WHERE slug IS NOT NULL;",
                "CREATE INDEX IF NOT EXISTS idx_profilepages_enabled ON profilepages (propertyid, userid) WHERE enabled AND slug IS NOT NULL;",
                "CREATE TABLE IF NOT EXISTS profileslugs (propertyid TEXT NOT NULL, slug TEXT NOT NULL, userid TEXT NOT NULL, createdutc TIMESTAMPTZ NOT NULL, PRIMARY KEY (propertyid, slug));",
                "CREATE INDEX IF NOT EXISTS idx_profileslugs_user ON profileslugs (propertyid, userid);",
                "CREATE INDEX IF NOT EXISTS idx_eventslugs_event ON eventslugs (propertyid, eventid);",
                "CREATE INDEX IF NOT EXISTS idx_events_publicpages ON events (propertyid, endutc, id) WHERE status = 'Published' AND audience = 'Public' AND deletedutc IS NULL AND moderationstate = 'None';"
            };
        }
    }
}
