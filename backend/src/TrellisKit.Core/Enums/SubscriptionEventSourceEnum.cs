namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// How a subscription event arrived.
    /// </summary>
    public enum SubscriptionEventSourceEnum
    {
        /// <summary>
        /// A store notification.
        /// </summary>
        Webhook,

        /// <summary>
        /// A client verification call.
        /// </summary>
        Verify,

        /// <summary>
        /// The reconciliation worker.
        /// </summary>
        Reconcile
    }
}
