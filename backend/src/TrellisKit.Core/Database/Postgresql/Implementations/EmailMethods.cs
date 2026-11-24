namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Database.Shared;

    /// <summary>
    /// PostgreSQL administrative email methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class EmailMethods : EmailMethodsBase
    {
        internal EmailMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
