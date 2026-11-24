namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One attachment of a direct message. Recipients of a pending request see a placeholder instead of the media.
    /// </summary>
    public class MessageAttachmentView
    {
        #region Public-Members

        /// <summary>
        /// Media identifier.
        /// </summary>
        public string MediaId { get; set; } = String.Empty;

        /// <summary>
        /// Media kind.
        /// </summary>
        public MediaKindEnum Kind { get; set; } = MediaKindEnum.Image;

        /// <summary>
        /// Whether the media is hidden from the caller (pending request).
        /// </summary>
        public bool IsHidden { get; set; } = false;

        /// <summary>
        /// Placeholder text when hidden ("Attachment hidden until you accept"), else null.
        /// </summary>
        public string? Placeholder { get; set; } = null;

        /// <summary>
        /// The media payload with its URLs, or null when hidden or no longer available.
        /// </summary>
        public MediaView? Media { get; set; } = null;

        #endregion
    }
}
