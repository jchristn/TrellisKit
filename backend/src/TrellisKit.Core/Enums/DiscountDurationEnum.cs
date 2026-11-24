namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// How long a discount code's discount applies to a web subscription.
    /// </summary>
    public enum DiscountDurationEnum
    {
        /// <summary>
        /// The first billing period only (the first year of an annual membership).
        /// </summary>
        Once,

        /// <summary>
        /// Every billing period while the subscription lasts.
        /// </summary>
        Forever
    }
}
