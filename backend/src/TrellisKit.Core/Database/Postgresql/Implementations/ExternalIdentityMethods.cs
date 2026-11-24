namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Database.Shared;

    /// <summary>
    /// PostgreSQL ExternalIdentity methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class ExternalIdentityMethods : ExternalIdentityMethodsBase
    {
        internal ExternalIdentityMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
