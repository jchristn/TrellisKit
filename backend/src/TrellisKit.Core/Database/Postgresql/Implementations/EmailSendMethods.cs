namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Shared;
    using TrellisKit.Core.Database.Postgresql.Queries;

    /// <summary>
    /// PostgreSQL category email send (idempotency) methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class EmailSendMethods : EmailSendMethodsBase
    {
        internal EmailSendMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
