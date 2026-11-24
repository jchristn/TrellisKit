namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// How long a free membership lasts, or (sign-up codes only) <see cref="Discount"/>: the code gives a discount at web
    /// checkout instead of a free membership.
    /// </summary>
    public enum GrantTypeEnum
    {
        /// <summary>
        /// A number of months from the start.
        /// </summary>
        DurationMonths,

        /// <summary>
        /// Until a fixed date.
        /// </summary>
        FixedEndDate,

        /// <summary>
        /// No end date.
        /// </summary>
        Lifetime,

        /// <summary>
        /// A discount on the paid membership at web (Stripe) checkout; no free membership. Sign-up codes and batches only.
        /// </summary>
        Discount
    }
}
