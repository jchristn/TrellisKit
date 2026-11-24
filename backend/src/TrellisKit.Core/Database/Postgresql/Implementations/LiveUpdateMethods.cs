namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Shared;
    using TrellisKit.Core.Database.Postgresql.Queries;

    /// <summary>
    /// PostgreSQL live update methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class LiveUpdateMethods : LiveUpdateMethodsBase
    {
        internal LiveUpdateMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
