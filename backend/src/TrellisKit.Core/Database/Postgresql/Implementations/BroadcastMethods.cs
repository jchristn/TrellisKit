namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Shared;
    using TrellisKit.Core.Database.Postgresql.Queries;

    /// <summary>
    /// PostgreSQL broadcast, segment, audience, and delivery methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class BroadcastMethods : BroadcastMethodsBase
    {
        internal BroadcastMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
