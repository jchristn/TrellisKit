namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body carrying a required reason.
    /// </summary>
    public class ReasonRequest
    {
        #region Public-Members

        /// <summary>
        /// Reason.
        /// </summary>
        public string? Reason { get; set; } = null;

        #endregion
    }
}
