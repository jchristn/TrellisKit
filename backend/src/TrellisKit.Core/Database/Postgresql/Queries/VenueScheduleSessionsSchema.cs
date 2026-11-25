namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;

    /// <summary>Neutral session vocabulary for structured venue schedules and configurable session labels.</summary>
    internal static class VenueScheduleSessionsSchema
    {
        internal const int Version = 124;
        internal const string Description = "Venue schedule sessions and session labels";
        internal static List<string> Statements() => new List<string>
        {
            // Migration 123 created the kind check inline, so PostgreSQL named it venuescheduleentries_kind_check.
            // Drop it (and this migration's own name) before converting rows, then add the neutral named constraint.
            "ALTER TABLE venuescheduleentries DROP CONSTRAINT IF EXISTS venuescheduleentries_kind_check;",
            "ALTER TABLE venuescheduleentries DROP CONSTRAINT IF EXISTS ck_venuescheduleentries_kind;",
            "UPDATE venuescheduleentries SET kind = 'Session' WHERE kind = 'Class';",
            "ALTER TABLE venuescheduleentries ADD CONSTRAINT ck_venuescheduleentries_kind CHECK (kind IN ('Hours', 'Session'));",
            "UPDATE directorysubmissionfields SET field = 'schedule.sessions.' || substr(field, 18) WHERE field LIKE 'schedule.classes.%';",
            "ALTER TABLE directoryconfig ADD COLUMN IF NOT EXISTS sessionlabel TEXT NOT NULL DEFAULT 'Session';",
            "ALTER TABLE directoryconfig ADD COLUMN IF NOT EXISTS sessionlabelplural TEXT NOT NULL DEFAULT 'Sessions';"
        };
    }
}
