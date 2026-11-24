namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Shared;
    using TrellisKit.Core.Database.Postgresql.Queries;

    /// <summary>
    /// PostgreSQL ideas board methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class IdeaMethods : IdeaMethodsBase
    {
        internal IdeaMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
