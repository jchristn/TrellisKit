namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Database.Shared;

    /// <summary>
    /// PostgreSQL saves and collections methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class FavoriteMethods : FavoriteMethodsBase
    {
        internal FavoriteMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
