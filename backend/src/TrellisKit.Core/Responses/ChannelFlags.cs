namespace TrellisKit.Core.Responses
{
    /// <summary>
    /// A true or false per notification channel.
    /// </summary>
    public class ChannelFlags
    {
        /// <summary>
        /// In-app.
        /// </summary>
        public bool InApp { get; set; } = false;

        /// <summary>
        /// Push.
        /// </summary>
        public bool Push { get; set; } = false;

        /// <summary>
        /// Email.
        /// </summary>
        public bool Email { get; set; } = false;
    }
}
