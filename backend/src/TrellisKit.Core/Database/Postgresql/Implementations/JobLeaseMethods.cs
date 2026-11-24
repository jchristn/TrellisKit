namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Shared;
    using TrellisKit.Core.Database.Postgresql.Queries;

    /// <summary>
    /// PostgreSQL singleton job lease methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class JobLeaseMethods : JobLeaseMethodsBase
    {
        internal JobLeaseMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
