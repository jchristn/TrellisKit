namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Database.Shared;

    /// <summary>
    /// PostgreSQL Endpoint methods of the AI feature (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class AiEndpointMethods : AiEndpointMethodsBase
    {
        internal AiEndpointMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
