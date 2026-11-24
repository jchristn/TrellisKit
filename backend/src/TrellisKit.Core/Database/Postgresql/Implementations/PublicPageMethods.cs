namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Database.Shared;

    /// <summary>
    /// PostgreSQL public landing page methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class PublicPageMethods : PublicPageMethodsBase
    {
        internal PublicPageMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
