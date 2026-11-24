namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Database.Shared;

    /// <summary>
    /// PostgreSQL lesson progress, play, follow, and metrics methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class LessonProgressMethods : LessonProgressMethodsBase
    {
        internal LessonProgressMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
