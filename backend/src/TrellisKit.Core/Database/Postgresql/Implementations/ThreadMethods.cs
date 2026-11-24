namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Shared;
    using TrellisKit.Core.Database.Postgresql.Queries;

    /// <summary>
    /// PostgreSQL forum thread, revision, follow, read state, and trending methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class ThreadMethods : ThreadMethodsBase
    {
        internal ThreadMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
