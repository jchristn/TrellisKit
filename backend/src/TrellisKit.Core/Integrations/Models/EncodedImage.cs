namespace TrellisKit.Core.Integrations.Models
{
    using System;

    /// <summary>
    /// Encoded image bytes produced by the image processor (no metadata, sRGB).
    /// </summary>
    public class EncodedImage
    {
        #region Public-Members

        /// <summary>
        /// Encoded bytes. Never null.
        /// </summary>
        public byte[] Data
        {
            get { return _Data; }
            set { _Data = value ?? Array.Empty<byte>(); }
        }

        /// <summary>
        /// Format: webp or jpeg.
        /// </summary>
        public string Format { get; set; } = "jpeg";

        /// <summary>
        /// Content type, for example image/jpeg.
        /// </summary>
        public string ContentType { get; set; } = "image/jpeg";

        /// <summary>
        /// Width in pixels.
        /// </summary>
        public int Width { get; set; } = 0;

        /// <summary>
        /// Height in pixels.
        /// </summary>
        public int Height { get; set; } = 0;

        #endregion

        #region Private-Members

        private byte[] _Data = Array.Empty<byte>();

        #endregion
    }
}
