namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Shared;
    using TrellisKit.Core.Database.Postgresql.Queries;

    /// <summary>
    /// PostgreSQL comment and comment revision methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class CommentMethods : CommentMethodsBase
    {
        internal CommentMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
