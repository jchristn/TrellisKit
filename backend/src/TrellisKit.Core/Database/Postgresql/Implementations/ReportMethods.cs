namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Shared;
    using TrellisKit.Core.Database.Postgresql.Queries;

    /// <summary>
    /// PostgreSQL report methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class ReportMethods : ReportMethodsBase
    {
        internal ReportMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
