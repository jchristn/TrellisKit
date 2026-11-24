namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;
    /// <summary>Fenced planning claims, separate from executable job runs.</summary>
    internal static class JobPlanningSchema
    {
        internal const int Version = 122;
        internal const string Description = "Recoverable job planning leases";
        internal static List<string> Statements() { return new List<string> { "ALTER TABLE jobruns ADD COLUMN IF NOT EXISTS planningtoken TEXT NULL, ADD COLUMN IF NOT EXISTS planninguntilutc TIMESTAMPTZ NULL;" }; }
    }
}
