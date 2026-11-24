namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Shared;
    using TrellisKit.Core.Database.Postgresql.Queries;

    /// <summary>
    /// PostgreSQL reaction and reaction count methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class ReactionMethods : ReactionMethodsBase
    {
        internal ReactionMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
