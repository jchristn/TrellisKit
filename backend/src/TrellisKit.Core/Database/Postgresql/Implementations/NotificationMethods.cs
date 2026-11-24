namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Shared;
    using TrellisKit.Core.Database.Postgresql.Queries;

    /// <summary>
    /// PostgreSQL notification center methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class NotificationMethods : NotificationMethodsBase
    {
        internal NotificationMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
