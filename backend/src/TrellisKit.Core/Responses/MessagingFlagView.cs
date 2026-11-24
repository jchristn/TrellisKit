namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Result of the RevokeMessaging and restore actions.
    /// </summary>
    public class MessagingFlagView
    {
        #region Public-Members

        /// <summary>
        /// Member.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// The member flag afterwards.
        /// </summary>
        public bool CanSendDirectMessages { get; set; } = false;

        #endregion
    }
}
