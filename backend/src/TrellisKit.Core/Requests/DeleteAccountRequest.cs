namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of DELETE /api/v1/me.
    /// </summary>
    public class DeleteAccountRequest
    {
        #region Public-Members

        /// <summary>
        /// Must be DELETE.
        /// </summary>
        public string? Confirm { get; set; } = null;

        #endregion
    }
}
