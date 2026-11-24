namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Shared;
    using TrellisKit.Core.Database.Postgresql.Queries;

    /// <summary>
    /// PostgreSQL media attachment and variant methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class MediaMethods : MediaMethodsBase
    {
        internal MediaMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
