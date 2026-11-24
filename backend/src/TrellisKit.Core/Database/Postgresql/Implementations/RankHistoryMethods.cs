namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Database.Shared;

    /// <summary>
    /// PostgreSQL rank history methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class RankHistoryMethods : RankHistoryMethodsBase
    {
        internal RankHistoryMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
