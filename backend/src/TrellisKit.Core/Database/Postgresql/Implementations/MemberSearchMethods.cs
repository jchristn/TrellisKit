namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Shared;
    using TrellisKit.Core.Database.Postgresql.Queries;

    /// <summary>
    /// PostgreSQL member search (display name words and prefix queries) methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class MemberSearchMethods : MemberSearchMethodsBase
    {
        internal MemberSearchMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
