namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Shared;
    using TrellisKit.Core.Database.Postgresql.Queries;

    /// <summary>
    /// PostgreSQL per-user posting permission methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class UserPermissionMethods : UserPermissionMethodsBase
    {
        internal UserPermissionMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
