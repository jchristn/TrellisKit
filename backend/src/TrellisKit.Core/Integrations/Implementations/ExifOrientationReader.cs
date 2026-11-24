namespace TrellisKit.Core.Integrations.Implementations
{
    using System;

    /// <summary>
    /// Reads the EXIF orientation tag (0x0112) from a JPEG APP1 segment without decoding the image. Used when the
    /// codec does not report an origin. Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public static class ExifOrientationReader
    {
        #region Public-Methods

        /// <summary>
        /// The EXIF orientation of a JPEG.
        /// </summary>
        /// <param name="jpeg">JPEG bytes.</param>
        /// <returns>1 to 8; 1 (normal) when absent, malformed, or not a JPEG.</returns>
        public static int Read(byte[] jpeg)
        {
            if (jpeg == null || jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8) return 1;
            int offset = 2;
            while (offset + 4 <= jpeg.Length)
            {
                if (jpeg[offset] != 0xFF) return 1;
                byte marker = jpeg[offset + 1];
                if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
                {
                    offset += 2;
                    continue;
                }
                if (marker == 0xDA || marker == 0xD9) return 1;
                int length = (jpeg[offset + 2] << 8) | jpeg[offset + 3];
                if (length < 2 || offset + 2 + length > jpeg.Length) return 1;
                if (marker == 0xE1 && length >= 8)
                {
                    int start = offset + 4;
                    if (jpeg[start] == (byte)'E' && jpeg[start + 1] == (byte)'x' && jpeg[start + 2] == (byte)'i' && jpeg[start + 3] == (byte)'f' && jpeg[start + 4] == 0 && jpeg[start + 5] == 0)
                    {
                        int value = ReadTiff(jpeg, start + 6, offset + 2 + length);
                        if (value >= 1 && value <= 8) return value;
                    }
                }
                offset += 2 + length;
            }
            return 1;
        }

        #endregion

        #region Private-Methods

        private static int ReadTiff(byte[] data, int tiff, int end)
        {
            if (tiff + 8 > end) return 1;
            bool little;
            if (data[tiff] == (byte)'I' && data[tiff + 1] == (byte)'I') little = true;
            else if (data[tiff] == (byte)'M' && data[tiff + 1] == (byte)'M') little = false;
            else return 1;
            if (U16(data, tiff + 2, little) != 42) return 1;
            long ifd = tiff + U32(data, tiff + 4, little);
            if (ifd + 2 > end) return 1;
            int count = U16(data, (int)ifd, little);
            for (int i = 0; i < count; i++)
            {
                long entry = ifd + 2 + i * 12L;
                if (entry + 12 > end) return 1;
                if (U16(data, (int)entry, little) == 0x0112) return U16(data, (int)entry + 8, little);
            }
            return 1;
        }

        private static int U16(byte[] data, int offset, bool little)
        {
            return little ? data[offset] | (data[offset + 1] << 8) : (data[offset] << 8) | data[offset + 1];
        }

        private static long U32(byte[] data, int offset, bool little)
        {
            return little
                ? (long)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16)) | ((long)data[offset + 3] << 24)
                : ((long)data[offset] << 24) | (long)((data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
        }

        #endregion
    }
}
