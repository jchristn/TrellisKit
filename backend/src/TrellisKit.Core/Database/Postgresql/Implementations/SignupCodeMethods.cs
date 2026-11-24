namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Database.Shared;
    using TrellisKit.Core.Models;

    /// <summary>
    /// PostgreSQL sign-up code methods. Redemption is one statement: a data-modifying CTE whose guarded UPDATE takes the
    /// code's row lock (a concurrent redeemer waits and re-checks the count after it commits), and whose inserts run only
    /// when the UPDATE returned a row. The unique (signupcodeid, userid) index rejects a concurrent second redemption by
    /// the same member.
    /// </summary>
    internal sealed class SignupCodeMethods : SignupCodeMethodsBase
    {
        internal SignupCodeMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }

        protected override List<string> BuildRedeemStatements(SignupCodeRedemption redemption, Entitlement entitlement, DateTime nowUtc)
        {
            return new List<string>
            {
                "WITH u AS (" + RedeemUpdateSql(redemption, nowUtc) + " RETURNING id), " +
                "e AS (INSERT INTO entitlements (" + EntitlementColumns + ") SELECT " + EntitlementValues(entitlement) + " FROM u RETURNING id) " +
                "INSERT INTO signupcoderedemptions (" + RedemptionColumns + ") SELECT " + RedemptionValues(redemption) + " FROM e RETURNING id;"
            };
        }

        protected override List<string> BuildDiscountStatements(SignupCodeRedemption redemption, DateTime nowUtc)
        {
            return new List<string>
            {
                "WITH u AS (" + RedeemUpdateSql(redemption, nowUtc) + " RETURNING id) " +
                "INSERT INTO signupcoderedemptions (" + RedemptionColumns + ") SELECT " + RedemptionValues(redemption) + " FROM u RETURNING id;"
            };
        }
    }
}
