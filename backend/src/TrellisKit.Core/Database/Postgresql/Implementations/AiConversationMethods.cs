namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Database.Shared;

    /// <summary>
    /// PostgreSQL Conversation methods of the AI feature (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class AiConversationMethods : AiConversationMethodsBase
    {
        internal AiConversationMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
