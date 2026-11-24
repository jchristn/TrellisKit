namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// State of one push delivery to one device.
    /// </summary>
    public enum PushDeliveryStatusEnum
    {
        /// <summary>
        /// Not yet sent.
        /// </summary>
        Queued,

        /// <summary>
        /// Accepted by the push service; receipt not yet checked.
        /// </summary>
        Sent,

        /// <summary>
        /// Receipt reported success.
        /// </summary>
        Ok,

        /// <summary>
        /// The send or the receipt reported an error.
        /// </summary>
        Error
    }
}
