namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Shared;
    using TrellisKit.Core.Database.Postgresql.Queries;

    /// <summary>
    /// PostgreSQL search index queue methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class SearchQueueMethods : SearchQueueMethodsBase
    {
        internal SearchQueueMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
