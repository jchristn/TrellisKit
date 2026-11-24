namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Database.Shared;

    /// <summary>
    /// PostgreSQL Assistant methods of the AI feature (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class AiAssistantMethods : AiAssistantMethodsBase
    {
        internal AiAssistantMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
