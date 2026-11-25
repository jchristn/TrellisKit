namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;
    internal static class CatalogNotationSchema
    {
        internal const int Version = 130;
        internal const string Description="Typed catalog attire, instructional levels and declared notation";
        internal static List<string> Statements()
        {
            List<string> statements=new List<string>
            {
                "ALTER TABLE catalogentries ADD COLUMN attirekey TEXT NULL CHECK(attirekey IS NULL OR length(attirekey) BETWEEN 1 AND 64),ADD COLUMN levelkey TEXT NULL CHECK(levelkey IS NULL OR length(levelkey) BETWEEN 1 AND 64),ADD COLUMN notationsystemkey TEXT NULL CHECK(notationsystemkey IS NULL OR length(notationsystemkey) BETWEEN 1 AND 64),ADD COLUMN notationtext TEXT NULL CHECK(notationtext IS NULL OR length(notationtext) BETWEEN 1 AND 500),ADD CONSTRAINT ck_catalogentries_notation_pair CHECK((notationsystemkey IS NULL)=(notationtext IS NULL));",
                "CREATE INDEX idx_catalogentries_attire ON catalogentries(propertyid,attirekey,id) WHERE deletedutc IS NULL;",
                "CREATE INDEX idx_catalogentries_level ON catalogentries(propertyid,levelkey,id) WHERE deletedutc IS NULL;",
                "CREATE INDEX idx_catalogentries_notation ON catalogentries(propertyid,notationsystemkey,id) WHERE deletedutc IS NULL;"
            };
            statements.AddRange(new ContentChangeSource("catalogentries","catalogentries","CatalogEntry","id","createdbyuserid","name,summary,movementpattern,plane,laterality,techniquetype,fromposition,toposition,familykey,attirekey,levelkey,notationsystemkey,notationtext","","deletedutc").Statements());
            return statements;
        }
    }
}
