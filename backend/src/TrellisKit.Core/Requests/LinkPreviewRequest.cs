namespace TrellisKit.Core.Requests
{
    /// <summary>
    /// Body of POST /api/v1/link-previews.
    /// </summary>
    public class LinkPreviewRequest
    {
        #region Public-Members

        /// <summary>
        /// Absolute http or https URL.
        /// </summary>
        public string? Url { get; set; } = null;

        #endregion
    }
}
