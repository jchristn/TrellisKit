namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Result of DELETE /api/v1/me.
    /// </summary>
    public class DeleteAccountResponse
    {
        #region Public-Members

        /// <summary>
        /// Message.
        /// </summary>
        public string Message { get; set; } = String.Empty;

        /// <summary>
        /// What the member must cancel themselves, or null.
        /// </summary>
        public string? StoreSubscriptionNote { get; set; } = null;

        #endregion
    }
}
