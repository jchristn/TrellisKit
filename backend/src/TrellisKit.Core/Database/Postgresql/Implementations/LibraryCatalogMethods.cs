namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Database.Shared;

    /// <summary>
    /// PostgreSQL library catalog methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class LibraryCatalogMethods : LibraryCatalogMethodsBase
    {
        internal LibraryCatalogMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
