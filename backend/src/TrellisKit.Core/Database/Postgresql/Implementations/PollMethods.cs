namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Shared;
    using TrellisKit.Core.Database.Postgresql.Queries;

    /// <summary>
    /// PostgreSQL poll, option, and vote methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class PollMethods : PollMethodsBase
    {
        internal PollMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
