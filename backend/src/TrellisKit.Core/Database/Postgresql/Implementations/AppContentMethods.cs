namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Shared;
    using TrellisKit.Core.Database.Postgresql.Queries;

    /// <summary>
    /// PostgreSQL remote string, banner, remote asset, and string catalog methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class AppContentMethods : AppContentMethodsBase
    {
        internal AppContentMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
