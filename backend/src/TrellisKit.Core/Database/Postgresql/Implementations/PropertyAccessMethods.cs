namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Database.Shared;

    /// <summary>
    /// PostgreSQL access configuration methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class PropertyAccessMethods : PropertyAccessMethodsBase
    {
        internal PropertyAccessMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
