namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Shared;
    using TrellisKit.Core.Database.Postgresql.Queries;

    /// <summary>
    /// PostgreSQL moderation action methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class ModerationActionMethods : ModerationActionMethodsBase
    {
        internal ModerationActionMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
