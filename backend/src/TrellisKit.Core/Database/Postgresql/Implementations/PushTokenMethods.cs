namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Shared;
    using TrellisKit.Core.Database.Postgresql.Queries;

    /// <summary>
    /// PostgreSQL push token and push delivery methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class PushTokenMethods : PushTokenMethodsBase
    {
        internal PushTokenMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
