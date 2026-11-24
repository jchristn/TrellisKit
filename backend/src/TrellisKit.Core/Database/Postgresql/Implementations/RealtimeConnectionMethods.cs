namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Shared;
    using TrellisKit.Core.Database.Postgresql.Queries;

    /// <summary>
    /// PostgreSQL realtime connection registry methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class RealtimeConnectionMethods : RealtimeConnectionMethodsBase
    {
        internal RealtimeConnectionMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
