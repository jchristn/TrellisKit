namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Database.Shared;

    /// <summary>
    /// PostgreSQL RefreshToken methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class RefreshTokenMethods : RefreshTokenMethodsBase
    {
        internal RefreshTokenMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
