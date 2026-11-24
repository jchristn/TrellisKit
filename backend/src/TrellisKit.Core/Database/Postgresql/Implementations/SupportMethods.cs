namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Shared;
    using TrellisKit.Core.Database.Postgresql.Queries;

    /// <summary>
    /// PostgreSQL support ticket methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class SupportMethods : SupportMethodsBase
    {
        internal SupportMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
