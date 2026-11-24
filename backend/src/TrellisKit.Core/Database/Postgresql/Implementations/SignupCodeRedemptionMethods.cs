namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Database.Shared;

    /// <summary>
    /// PostgreSQL SignupCodeRedemption methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class SignupCodeRedemptionMethods : SignupCodeRedemptionMethodsBase
    {
        internal SignupCodeRedemptionMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
