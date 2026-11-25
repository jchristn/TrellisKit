namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Database.Shared;

    /// <summary>
    /// PostgreSQL lesson and catalog link methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class LessonCatalogLinkMethods : LessonCatalogLinkMethodsBase
    {
        internal LessonCatalogLinkMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
