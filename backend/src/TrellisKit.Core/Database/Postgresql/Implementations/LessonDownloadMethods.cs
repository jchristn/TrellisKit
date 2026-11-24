namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Database.Shared;

    /// <summary>
    /// PostgreSQL lesson download license methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class LessonDownloadMethods : LessonDownloadMethodsBase
    {
        internal LessonDownloadMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
