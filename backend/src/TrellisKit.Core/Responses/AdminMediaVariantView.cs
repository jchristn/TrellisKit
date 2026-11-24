namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// A stored variant in the admin media detail.
    /// </summary>
    public class AdminMediaVariantView
    {
        #region Public-Members

        /// <summary>
        /// thumb, medium, or large.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// webp or jpeg.
        /// </summary>
        public string Format { get; set; } = String.Empty;

        /// <summary>
        /// Width.
        /// </summary>
        public int Width { get; set; } = 0;

        /// <summary>
        /// Height.
        /// </summary>
        public int Height { get; set; } = 0;

        /// <summary>
        /// Stored bytes.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        #endregion
    }
}
