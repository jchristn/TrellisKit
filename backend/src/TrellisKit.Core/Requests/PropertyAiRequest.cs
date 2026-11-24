namespace TrellisKit.Core.Requests
{
    /// <summary>
    /// PUT /api/v1/admin/property/ai body: switch AI on or off for the property.
    /// </summary>
    public class PropertyAiRequest
    {
        #region Public-Members

        /// <summary>
        /// Whether AI is on. Required.
        /// </summary>
        public bool? Enabled { get; set; } = null;

        #endregion
    }
}
