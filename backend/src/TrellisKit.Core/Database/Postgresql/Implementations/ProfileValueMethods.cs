namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Database.Shared;

    /// <summary>
    /// PostgreSQL profile value methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class ProfileValueMethods : ProfileValueMethodsBase
    {
        internal ProfileValueMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
