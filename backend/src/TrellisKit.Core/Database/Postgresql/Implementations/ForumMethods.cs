namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Shared;
    using TrellisKit.Core.Database.Postgresql.Queries;

    /// <summary>
    /// PostgreSQL forum, follow, read state, mute, and moderator methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class ForumMethods : ForumMethodsBase
    {
        internal ForumMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
