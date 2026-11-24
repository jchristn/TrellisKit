namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Shared;
    using TrellisKit.Core.Database.Postgresql.Queries;

    /// <summary>
    /// PostgreSQL notification preference and notification hours methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class NotificationPreferenceMethods : NotificationPreferenceMethodsBase
    {
        internal NotificationPreferenceMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
