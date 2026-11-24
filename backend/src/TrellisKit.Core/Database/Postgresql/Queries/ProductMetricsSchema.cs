namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;
    /// <summary>Member milestone deduplication; identifiers are removed on account closure.</summary>
    internal static class ProductMetricsSchema
    {
        internal const int Version = 121;
        internal const string Description = "Product metric milestone deduplication";
        internal static List<string> Statements() { return new List<string> { "CREATE TABLE IF NOT EXISTS metricfirstactions(propertyid TEXT NOT NULL,userid TEXT NOT NULL,action TEXT NOT NULL,occurredutc TIMESTAMPTZ NOT NULL,PRIMARY KEY(propertyid,userid,action));" }; }
    }
}
