namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Database.Shared;

    /// <summary>
    /// PostgreSQL Usage methods of the AI feature (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class AiUsageMethods : AiUsageMethodsBase
    {
        internal AiUsageMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
