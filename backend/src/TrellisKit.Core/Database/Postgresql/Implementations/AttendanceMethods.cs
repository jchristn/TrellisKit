namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Database.Shared;

    /// <summary>
    /// PostgreSQL attendance methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class AttendanceMethods : AttendanceMethodsBase
    {
        internal AttendanceMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
