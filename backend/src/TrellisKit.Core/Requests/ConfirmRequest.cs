namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// A body carrying a typed confirmation.
    /// </summary>
    public class ConfirmRequest
    {
        #region Public-Members

        /// <summary>
        /// Confirmation text (for example the field key).
        /// </summary>
        public string? Confirm { get; set; } = null;

        #endregion
    }
}
