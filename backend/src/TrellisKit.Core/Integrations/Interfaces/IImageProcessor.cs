namespace TrellisKit.Core.Integrations.Interfaces
{
    using System;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Image decoding, resizing, and re-encoding (contract section 4.6). Steps are separate so the worker can time
    /// each stage. Every output is encoded from pixels only, so no EXIF, GPS, XMP, IPTC, or maker notes survive.
    /// Thread safety: implementations are safe to call concurrently (each decoded image is used by one caller).
    /// </summary>
    public interface IImageProcessor
    {
        /// <summary>
        /// Decode an image, apply its EXIF orientation to the pixels, and convert it to sRGB.
        /// </summary>
        /// <param name="data">Encoded bytes (JPEG, PNG, or WebP).</param>
        /// <param name="maxPixels">Largest accepted width times height.</param>
        /// <returns>Decoded image.</returns>
        /// <exception cref="ArgumentNullException">The data is null.</exception>
        /// <exception cref="MediaProcessingException">Corrupt (undecodable) or ImageTooLarge (pixel count).</exception>
        IDecodedImage Decode(byte[] data, long maxPixels);

        /// <summary>
        /// Resize so the long edge is at most the given size. Never upscales (a smaller image is copied as is).
        /// </summary>
        /// <param name="image">Decoded image.</param>
        /// <param name="maxLongEdge">Largest long edge in pixels. Minimum 1.</param>
        /// <returns>New decoded image.</returns>
        /// <exception cref="ArgumentNullException">The image is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The size is less than 1.</exception>
        IDecodedImage Resize(IDecodedImage image, int maxLongEdge);

        /// <summary>
        /// Encode as WebP.
        /// </summary>
        /// <param name="image">Decoded image.</param>
        /// <param name="quality">Quality 1 to 100 (contract: 82).</param>
        /// <returns>Encoded image.</returns>
        /// <exception cref="ArgumentNullException">The image is null.</exception>
        EncodedImage EncodeWebp(IDecodedImage image, int quality);

        /// <summary>
        /// Encode as JPEG (transparent pixels are flattened onto white).
        /// </summary>
        /// <param name="image">Decoded image.</param>
        /// <param name="quality">Quality 1 to 100 (contract: 85).</param>
        /// <returns>Encoded image.</returns>
        /// <exception cref="ArgumentNullException">The image is null.</exception>
        EncodedImage EncodeJpeg(IDecodedImage image, int quality);

        /// <summary>
        /// Blurhash of an image (4x3 components).
        /// </summary>
        /// <param name="image">Decoded image.</param>
        /// <returns>Blurhash string.</returns>
        /// <exception cref="ArgumentNullException">The image is null.</exception>
        string Blurhash(IDecodedImage image);
    }
}
