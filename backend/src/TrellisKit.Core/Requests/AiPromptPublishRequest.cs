namespace TrellisKit.Core.Requests
{
    /// <summary>
    /// Publish the pending draft of an administrator-editable prompt.
    /// </summary>
    public class AiPromptPublishRequest
    {
        #region Public-Members

        /// <summary>
        /// The draft version the administrator reviewed (apv_), or null to publish the pending draft. When set and a newer
        /// draft exists, the publish is refused with 409 DraftChanged.
        /// </summary>
        public string? VersionId { get; set; } = null;

        #endregion
    }
}
