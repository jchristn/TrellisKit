namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Shared;
    using TrellisKit.Core.Database.Postgresql.Queries;

    /// <summary>
    /// PostgreSQL link preview cache methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class LinkPreviewMethods : LinkPreviewMethodsBase
    {
        internal LinkPreviewMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
