namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Database.Shared;

    /// <summary>
    /// PostgreSQL RoleAssignment methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class RoleAssignmentMethods : RoleAssignmentMethodsBase
    {
        internal RoleAssignmentMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
