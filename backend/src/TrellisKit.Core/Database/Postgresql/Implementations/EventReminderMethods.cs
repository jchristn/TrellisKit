namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Database.Shared;

    /// <summary>
    /// PostgreSQL EventReminder methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class EventReminderMethods : EventReminderMethodsBase
    {
        internal EventReminderMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
