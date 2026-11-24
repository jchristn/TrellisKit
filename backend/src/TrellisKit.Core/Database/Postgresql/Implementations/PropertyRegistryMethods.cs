namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Database.Shared;

    /// <summary>
    /// PostgreSQL property registry methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class PropertyRegistryMethods : PropertyRegistryMethodsBase
    {
        internal PropertyRegistryMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
