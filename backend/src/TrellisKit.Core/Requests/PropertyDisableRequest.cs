namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// POST /api/v1/platform/properties/{id}/disable body.
    /// </summary>
    public class PropertyDisableRequest
    {
        #region Public-Members

        /// <summary>
        /// Reason (recorded and shown in the console).
        /// </summary>
        public string? Reason { get; set; } = null;

        #endregion
    }
}
