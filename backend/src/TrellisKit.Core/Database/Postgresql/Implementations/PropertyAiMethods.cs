namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Database.Shared;

    /// <summary>
    /// PostgreSQL AI switch methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class PropertyAiMethods : PropertyAiMethodsBase
    {
        internal PropertyAiMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
