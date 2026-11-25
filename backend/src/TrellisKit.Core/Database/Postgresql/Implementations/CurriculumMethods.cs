namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Database.Shared;

    /// <summary>
    /// PostgreSQL curriculum methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class CurriculumMethods : CurriculumMethodsBase
    {
        internal CurriculumMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
