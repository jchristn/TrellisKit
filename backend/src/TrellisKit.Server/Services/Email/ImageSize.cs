namespace TrellisKit.Server.Services.Email
{
    using System;
    using System.IO;

    /// <summary>
    /// Pixel size of a PNG, GIF, or JPEG image, read from the file header without decoding the image. Used to give the
    /// email logo explicit dimensions: mail clients that drop or rewrite max-width and max-height otherwise stretch it.
    /// Thread safety: immutable.
    /// </summary>
    public class ImageSize
    {
        #region Public-Members

        /// <summary>
        /// Width in pixels (greater than zero).
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Height in pixels (greater than zero).
        /// </summary>
        public int Height { get; }

        #endregion

        #region Private-Members

        private const int _MaxHeaderBytes = 512 * 1024;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="width">Width in pixels.</param>
        /// <param name="height">Height in pixels.</param>
        /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive.</exception>
        public ImageSize(int width, int height)
        {
            if (width < 1) throw new ArgumentOutOfRangeException(nameof(width));
            if (height < 1) throw new ArgumentOutOfRangeException(nameof(height));
            Width = width;
            Height = height;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read the size of an image file, or null when the file is missing, unreadable, or not a PNG, GIF, or JPEG.
        /// </summary>
        /// <param name="path">File path, or null.</param>
        /// <returns>Size, or null.</returns>
        public static ImageSize? FromFile(string? path)
        {
            if (String.IsNullOrWhiteSpace(path)) return null;
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    int length = (int)Math.Min(stream.Length, _MaxHeaderBytes);
                    byte[] data = new byte[length];
                    int read = 0;
                    while (read < length)
                    {
                        int n = stream.Read(data, read, length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    return FromBytes(data.AsSpan(0, read));
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
            {
                return null;
            }
        }

        /// <summary>
        /// Read the size from the first bytes of a PNG, GIF, or JPEG image, or null when the format is not recognized.
        /// </summary>
        /// <param name="data">Leading bytes of the image.</param>
        /// <returns>Size, or null.</returns>
        public static ImageSize? FromBytes(ReadOnlySpan<byte> data)
        {
            if (data.Length >= 24 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
                return Create(BigEndian32(data, 16), BigEndian32(data, 20));
            if (data.Length >= 10 && data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46)
                return Create(data[6] | (data[7] << 8), data[8] | (data[9] << 8));
            if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8)
            {
                int i = 2;
                while (i + 9 < data.Length)
                {
                    if (data[i] != 0xFF) { i++; continue; }
                    byte marker = data[i + 1];
                    if (marker == 0xFF) { i++; continue; }
                    if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7)) { i += 2; continue; }
                    int segment = (data[i + 2] << 8) | data[i + 3];
                    bool startOfFrame = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
                    if (startOfFrame) return Create((data[i + 7] << 8) | data[i + 8], (data[i + 5] << 8) | data[i + 6]);
                    if (segment < 2) return null;
                    i += 2 + segment;
                }
            }
            return null;
        }

        /// <summary>
        /// The largest size with this aspect ratio that fits inside a box, never larger than the image itself.
        /// </summary>
        /// <param name="maxWidth">Box width in pixels (minimum 1).</param>
        /// <param name="maxHeight">Box height in pixels (minimum 1).</param>
        /// <returns>Fitted size.</returns>
        /// <exception cref="ArgumentOutOfRangeException">A box dimension is not positive.</exception>
        public ImageSize FitWithin(int maxWidth, int maxHeight)
        {
            if (maxWidth < 1) throw new ArgumentOutOfRangeException(nameof(maxWidth));
            if (maxHeight < 1) throw new ArgumentOutOfRangeException(nameof(maxHeight));
            double scale = Math.Min(1.0, Math.Min((double)maxWidth / Width, (double)maxHeight / Height));
            int w = Math.Max(1, (int)Math.Round(Width * scale));
            int h = Math.Max(1, (int)Math.Round(Height * scale));
            return new ImageSize(Math.Min(w, maxWidth), Math.Min(h, maxHeight));
        }

        #endregion

        #region Private-Methods

        private static int BigEndian32(ReadOnlySpan<byte> data, int offset)
        {
            long value = ((long)data[offset] << 24) | ((long)data[offset + 1] << 16) | ((long)data[offset + 2] << 8) | data[offset + 3];
            return value > Int32.MaxValue ? 0 : (int)value;
        }

        private static ImageSize? Create(int width, int height)
        {
            return width > 0 && height > 0 ? new ImageSize(width, height) : null;
        }

        #endregion
    }
}
