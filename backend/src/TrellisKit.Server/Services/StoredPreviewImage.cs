namespace TrellisKit.Server.Services
{
    using System;

    /// <summary>
    /// A link preview image re-encoded by the image pipeline and stored in the images location.
    /// </summary>
    public class StoredPreviewImage
    {
        #region Public-Members

        /// <summary>
        /// Object key in the images location, for example link-previews/{urlHash}/card.jpg.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Width in pixels after re-encoding.
        /// </summary>
        public int Width { get; set; } = 0;

        /// <summary>
        /// Height in pixels after re-encoding.
        /// </summary>
        public int Height { get; set; } = 0;

        #endregion
    }
}
