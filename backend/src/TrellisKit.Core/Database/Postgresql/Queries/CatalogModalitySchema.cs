namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;
    internal static class CatalogModalitySchema
    {
        internal const int Version = 129;
        internal const string Description="Typed catalog localized names and program family descriptors";
        internal static List<string> Statements()
        {
            List<string> statements=new List<string>
            {
                "ALTER TABLE catalogentries ADD COLUMN familykey TEXT NULL CHECK(familykey IS NULL OR length(familykey) BETWEEN 1 AND 64);",
                "CREATE UNIQUE INDEX ux_catalogentries_propertyid_id ON catalogentries(propertyid,id);",
                "CREATE TABLE cataloglocalizednames(propertyid TEXT NOT NULL,entryid TEXT NOT NULL,position INTEGER NOT NULL CHECK(position BETWEEN 0 AND 9),languagetag TEXT NOT NULL CHECK(length(languagetag) BETWEEN 2 AND 35),text TEXT NOT NULL CHECK(length(text) BETWEEN 1 AND 160),PRIMARY KEY(propertyid,entryid,position),FOREIGN KEY(propertyid,entryid) REFERENCES catalogentries(propertyid,id) ON DELETE CASCADE);",
                "CREATE UNIQUE INDEX ux_cataloglocalizednames_identity ON cataloglocalizednames(propertyid,entryid,lower(languagetag),lower(text));",
                "CREATE INDEX idx_cataloglocalizednames_language ON cataloglocalizednames(propertyid,languagetag,entryid);",
                "CREATE INDEX idx_catalogentries_family ON catalogentries(propertyid,familykey,id) WHERE deletedutc IS NULL;"
            };
            statements.AddRange(new ContentChangeSource("cataloglocalizednames","cataloglocalizednames","CatalogEntry","entryid","","text","",null).Statements());
            statements.AddRange(new ContentChangeSource("catalogentries","catalogentries","CatalogEntry","id","createdbyuserid","name,summary,movementpattern,plane,laterality,techniquetype,fromposition,toposition,familykey","","deletedutc").Statements());
            return statements;
        }
    }
}
