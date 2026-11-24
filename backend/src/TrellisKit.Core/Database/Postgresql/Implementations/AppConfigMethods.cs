namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Shared;
    using TrellisKit.Core.Database.Postgresql.Queries;

    /// <summary>
    /// PostgreSQL feature flag, kill switch, and content version methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class AppConfigMethods : AppConfigMethodsBase
    {
        internal AppConfigMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
