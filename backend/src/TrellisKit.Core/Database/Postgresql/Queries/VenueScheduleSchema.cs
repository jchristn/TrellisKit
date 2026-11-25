namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;

    /// <summary>Structured weekly venue schedules; preserves existing free-text schedules.</summary>
    internal static class VenueScheduleSchema
    {
        internal const int Version = 123;
        internal const string Description = "Structured venue schedules";
        internal static List<string> Statements() => new List<string>
        {
            "ALTER TABLE venues ADD COLUMN IF NOT EXISTS hasschedule BOOLEAN NOT NULL DEFAULT FALSE;",
            "ALTER TABLE venues ADD COLUMN IF NOT EXISTS scheduletimezone TEXT NULL;",
            "ALTER TABLE venues ADD COLUMN IF NOT EXISTS schedulenotes TEXT NULL;",
            "CREATE TABLE IF NOT EXISTS venuescheduleentries (propertyid TEXT NOT NULL, venueid TEXT NOT NULL, kind TEXT NOT NULL CHECK (kind IN ('Hours', 'Class')), position INTEGER NOT NULL, dayofweek INTEGER NOT NULL CHECK (dayofweek BETWEEN 0 AND 6), starttime TEXT NOT NULL, endtime TEXT NOT NULL, endsnextday BOOLEAN NOT NULL DEFAULT FALSE, name TEXT NULL, instructor TEXT NULL, PRIMARY KEY (propertyid, venueid, kind, position));"
        };
    }
}
