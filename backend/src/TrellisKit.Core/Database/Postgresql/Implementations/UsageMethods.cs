namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Database.Shared;

    /// <summary>
    /// PostgreSQL usage metering methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class UsageMethods : UsageMethodsBase
    {
        internal UsageMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
