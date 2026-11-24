namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Database.Shared;

    /// <summary>
    /// PostgreSQL Venue methods of the directory (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class VenueMethods : VenueMethodsBase
    {
        internal VenueMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
