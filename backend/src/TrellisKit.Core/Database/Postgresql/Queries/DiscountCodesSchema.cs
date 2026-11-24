namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;

    /// <summary>
    /// PostgreSQL statements of migration 95, "Discount codes" (docs/PHASE1_CONTRACT.md sections 6 and 7): sign-up codes and
    /// batches whose grant type is Discount carry the discount (percent or amount off, and its duration) and the Stripe
    /// coupon created for it; a discount redemption has no entitlement, so entitlementid becomes nullable.
    /// </summary>
    internal static class DiscountCodesSchema
    {
        internal const int Version = 95;

        internal const string Description = "Discount codes";

        internal static List<string> Statements()
        {
            return new List<string>
            {
                "ALTER TABLE signupcodes ADD COLUMN IF NOT EXISTS discountpercent INTEGER NULL;",
                "ALTER TABLE signupcodes ADD COLUMN IF NOT EXISTS discountamountcents INTEGER NULL;",
                "ALTER TABLE signupcodes ADD COLUMN IF NOT EXISTS discountduration TEXT NULL;",
                "ALTER TABLE signupcodes ADD COLUMN IF NOT EXISTS stripecouponid TEXT NULL;",
                "ALTER TABLE signupcodebatches ADD COLUMN IF NOT EXISTS discountpercent INTEGER NULL;",
                "ALTER TABLE signupcodebatches ADD COLUMN IF NOT EXISTS discountamountcents INTEGER NULL;",
                "ALTER TABLE signupcodebatches ADD COLUMN IF NOT EXISTS discountduration TEXT NULL;",
                "ALTER TABLE signupcoderedemptions ALTER COLUMN entitlementid DROP NOT NULL;"
            };
        }
    }
}
