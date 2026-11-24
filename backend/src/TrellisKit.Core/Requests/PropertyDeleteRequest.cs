namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// DELETE /api/v1/platform/properties/{id} body: the typed confirmation.
    /// </summary>
    public class PropertyDeleteRequest
    {
        #region Public-Members

        /// <summary>
        /// Must equal the property id.
        /// </summary>
        public string? Confirm { get; set; } = null;

        #endregion
    }
}
