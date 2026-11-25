namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;

    /// <summary>
    /// Attendance (docs/TRELLISKIT.md, Ranks, curricula, and attendance): one row per member, target (an event or a
    /// venue schedule session), and occurrence date.
    /// </summary>
    internal static class AttendanceSchema
    {
        internal const int Version = 134;
        internal const string Description = "Attendance check-ins";

        internal static List<string> Statements() => new List<string>
        {
            "CREATE TABLE IF NOT EXISTS attendance (propertyid TEXT NOT NULL, id TEXT NOT NULL PRIMARY KEY, userid TEXT NOT NULL, targettype TEXT NOT NULL, targetid TEXT NOT NULL, occurrencedate TEXT NOT NULL, " +
                "checkedinutc TIMESTAMPTZ NOT NULL, method TEXT NOT NULL, checkedinby TEXT NULL, createdutc TIMESTAMPTZ NOT NULL, updatedutc TIMESTAMPTZ NOT NULL);",
            "CREATE UNIQUE INDEX IF NOT EXISTS ux_attendance_member ON attendance (propertyid, userid, targettype, targetid, occurrencedate);",
            "CREATE INDEX IF NOT EXISTS idx_attendance_target ON attendance (propertyid, targettype, targetid, occurrencedate, checkedinutc DESC);",
            "CREATE INDEX IF NOT EXISTS idx_attendance_user ON attendance (propertyid, userid, checkedinutc DESC);",
            "CREATE INDEX IF NOT EXISTS idx_attendance_created ON attendance (propertyid, createdutc DESC, id DESC);"
        };
    }
}
