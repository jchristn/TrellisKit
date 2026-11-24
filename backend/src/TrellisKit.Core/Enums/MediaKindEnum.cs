namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Kind of uploaded media.
    /// </summary>
    public enum MediaKindEnum
    {
        /// <summary>
        /// A still image (JPEG, PNG, or WebP).
        /// </summary>
        Image,

        /// <summary>
        /// A video (MP4 or QuickTime).
        /// </summary>
        Video,

        /// <summary>
        /// A document (PDF or WebVTT captions) served unchanged after validation.
        /// </summary>
        Document
    }
}
