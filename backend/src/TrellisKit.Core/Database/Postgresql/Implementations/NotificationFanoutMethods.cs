namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Shared;
    using TrellisKit.Core.Database.Postgresql.Queries;

    /// <summary>
    /// PostgreSQL notification fan-out job methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class NotificationFanoutMethods : NotificationFanoutMethodsBase
    {
        internal NotificationFanoutMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
