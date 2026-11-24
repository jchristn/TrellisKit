namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// A broadcast delivery channel (docs/PHASE5_CONTRACT.md section 2.5). JSON names are inApp, push, and email.
    /// </summary>
    public enum BroadcastChannelEnum
    {
        /// <summary>
        /// An in-app Broadcast notification and the announcement view.
        /// </summary>
        InApp,

        /// <summary>
        /// A push notification through the push pipeline.
        /// </summary>
        Push,

        /// <summary>
        /// An administrative email (template broadcast).
        /// </summary>
        Email
    }
}
