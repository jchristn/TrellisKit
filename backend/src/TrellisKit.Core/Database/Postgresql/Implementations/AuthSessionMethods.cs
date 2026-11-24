namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Database.Shared;

    /// <summary>
    /// PostgreSQL AuthSession methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class AuthSessionMethods : AuthSessionMethodsBase
    {
        internal AuthSessionMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
