namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;
    internal static class CatalogApparatusSchema
    {
        internal const int Version = 131;
        internal const string Description="Source-reviewed catalog apparatus and contextual spring setup";
        internal static List<string> Statements()
        {
            List<string> statements=new List<string>
            {
                "ALTER TABLE catalogentries ADD COLUMN apparatuskey TEXT NULL CHECK(apparatuskey IS NULL OR length(apparatuskey) BETWEEN 1 AND 64),ADD COLUMN apparatusmanufacturer TEXT NULL CHECK(apparatusmanufacturer IS NULL OR length(apparatusmanufacturer) BETWEEN 1 AND 120),ADD COLUMN apparatusmodel TEXT NULL CHECK(apparatusmodel IS NULL OR length(apparatusmodel) BETWEEN 1 AND 120),ADD COLUMN apparatussetuptext TEXT NULL CHECK(apparatussetuptext IS NULL OR length(apparatussetuptext) BETWEEN 1 AND 500),ADD COLUMN apparatusspringsetuptext TEXT NULL CHECK(apparatusspringsetuptext IS NULL OR length(apparatusspringsetuptext) BETWEEN 1 AND 500),ADD CONSTRAINT ck_catalogentries_apparatus_context CHECK((apparatuskey IS NOT NULL OR (apparatusmanufacturer IS NULL AND apparatusmodel IS NULL AND apparatussetuptext IS NULL AND apparatusspringsetuptext IS NULL)) AND (apparatusmodel IS NULL OR apparatusmanufacturer IS NOT NULL) AND (apparatusspringsetuptext IS NULL OR (apparatuskey IS NOT NULL AND apparatusmanufacturer IS NOT NULL AND apparatusmodel IS NOT NULL)));"
            };
            statements.AddRange(new ContentChangeSource("catalogentries","catalogentries","CatalogEntry","id","createdbyuserid","name,summary,movementpattern,plane,laterality,techniquetype,fromposition,toposition,familykey,attirekey,levelkey,notationsystemkey,notationtext,apparatuskey,apparatusmanufacturer,apparatusmodel,apparatussetuptext,apparatusspringsetuptext","","deletedutc").Statements());
            return statements;
        }
    }
}
