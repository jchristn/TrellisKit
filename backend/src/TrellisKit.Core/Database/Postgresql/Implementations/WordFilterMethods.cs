namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Shared;
    using TrellisKit.Core.Database.Postgresql.Queries;

    /// <summary>
    /// PostgreSQL word filter term and hit methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class WordFilterMethods : WordFilterMethodsBase
    {
        internal WordFilterMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
