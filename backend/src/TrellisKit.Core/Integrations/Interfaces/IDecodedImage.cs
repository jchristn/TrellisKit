namespace TrellisKit.Core.Integrations.Interfaces
{
    using System;

    /// <summary>
    /// A decoded image held by an <see cref="IImageProcessor"/>: oriented, sRGB pixels with no metadata.
    /// Dispose to release the pixels. Thread safety: not safe for concurrent use.
    /// </summary>
    public interface IDecodedImage : IDisposable
    {
        /// <summary>
        /// Width in pixels.
        /// </summary>
        int Width { get; }

        /// <summary>
        /// Height in pixels.
        /// </summary>
        int Height { get; }
    }
}
