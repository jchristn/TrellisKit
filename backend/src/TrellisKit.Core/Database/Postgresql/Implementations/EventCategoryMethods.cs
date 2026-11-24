namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Database.Shared;

    /// <summary>
    /// PostgreSQL EventCategory methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class EventCategoryMethods : EventCategoryMethodsBase
    {
        internal EventCategoryMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
