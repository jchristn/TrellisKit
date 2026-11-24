namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;

    /// <summary>
    /// PostgreSQL statements of migration 93, "Venue reviews and member media" (docs/PHASE4_CONTRACT.md sections 3.12
    /// and 3.13): member reviews of venues with the one instructor reply (venuereviews, one live review per member per
    /// venue), photos and videos members add to a venue (venuemedia, one row per attached media), and the venue rating
    /// and media counters recomputed from them.
    /// </summary>
    internal static class VenueContributionsSchema
    {
        internal const int Version = 93;

        internal const string Description = "Venue reviews and member media";

        internal static List<string> Statements()
        {
            return new List<string>
            {
                "CREATE TABLE IF NOT EXISTS venuereviews (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, venueid TEXT NOT NULL, authoruserid TEXT NOT NULL, rating INTEGER NOT NULL, " +
                    "body TEXT NOT NULL, replybody TEXT NULL, replyuserid TEXT NULL, replyinstructorid TEXT NULL, replyutc TIMESTAMPTZ NULL, replyeditedutc TIMESTAMPTZ NULL, editedutc TIMESTAMPTZ NULL, " +
                    "openreportcount INTEGER NOT NULL DEFAULT 0, moderationstate TEXT NOT NULL DEFAULT 'None', moderatedutc TIMESTAMPTZ NULL, moderatedbyuserid TEXT NULL, moderationreason TEXT NULL, " +
                    "deletedutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_venuereviews_author ON venuereviews (propertyid, venueid, authoruserid) WHERE deletedutc IS NULL;",
                "CREATE INDEX IF NOT EXISTS idx_venuereviews_venue ON venuereviews (propertyid, venueid, createdutc DESC, id DESC) WHERE deletedutc IS NULL;",
                "CREATE INDEX IF NOT EXISTS idx_venuereviews_rating ON venuereviews (propertyid, venueid, rating) WHERE deletedutc IS NULL AND moderationstate = 'None';",
                "CREATE INDEX IF NOT EXISTS idx_venuereviews_user ON venuereviews (propertyid, authoruserid);",
                "CREATE INDEX IF NOT EXISTS idx_venuereviews_replyuser ON venuereviews (propertyid, replyuserid) WHERE replyuserid IS NOT NULL;",
                "CREATE TABLE IF NOT EXISTS venuemedia (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, venueid TEXT NOT NULL, mediaid TEXT NOT NULL, uploaderuserid TEXT NOT NULL, " +
                    "kind TEXT NOT NULL, openreportcount INTEGER NOT NULL DEFAULT 0, moderationstate TEXT NOT NULL DEFAULT 'None', moderatedutc TIMESTAMPTZ NULL, moderatedbyuserid TEXT NULL, " +
                    "moderationreason TEXT NULL, deletedutc TIMESTAMPTZ NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_venuemedia_media ON venuemedia (propertyid, mediaid);",
                "CREATE INDEX IF NOT EXISTS idx_venuemedia_venue ON venuemedia (propertyid, venueid, createdutc DESC, id DESC) WHERE deletedutc IS NULL;",
                "CREATE INDEX IF NOT EXISTS idx_venuemedia_user ON venuemedia (propertyid, uploaderuserid);",
                "ALTER TABLE venues ADD COLUMN IF NOT EXISTS ratingcount INTEGER NOT NULL DEFAULT 0;",
                "ALTER TABLE venues ADD COLUMN IF NOT EXISTS ratingtotal INTEGER NOT NULL DEFAULT 0;",
                "ALTER TABLE venues ADD COLUMN IF NOT EXISTS mediacount INTEGER NOT NULL DEFAULT 0;"
            };
        }
    }
}
