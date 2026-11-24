namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Outcome of a link preview fetch.
    /// </summary>
    public enum LinkPreviewStatusEnum
    {
        /// <summary>
        /// Preview available.
        /// </summary>
        Ok,

        /// <summary>
        /// Fetch refused or failed (cached for linkPreviews.failureCacheMinutes).
        /// </summary>
        Failed
    }
}
