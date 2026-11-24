namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Shared;
    using TrellisKit.Core.Database.Postgresql.Queries;

    /// <summary>
    /// PostgreSQL direct conversation methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class ConversationMethods : ConversationMethodsBase
    {
        internal ConversationMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
