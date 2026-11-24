namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Reads the duration (mvhd) and the first video track's display size (tkhd, after its rotation matrix) from an
    /// ISO BMFF file (MP4 or QuickTime) with ranged reads: it walks the top-level boxes by their headers and reads only
    /// the moov box. Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public static class Mp4HeaderReader
    {
        #region Public-Methods

        /// <summary>
        /// Read the header facts.
        /// </summary>
        /// <param name="readRange">Reads (offset, length) and returns the bytes (fewer at the end of the file).</param>
        /// <param name="fileSize">Total file size in bytes.</param>
        /// <param name="maxMoovBytes">Largest moov box that is read. Default 64 MiB.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Facts, or null when there is no moov box with an mvhd box (or it is too large).</returns>
        /// <exception cref="ArgumentNullException">The reader is null.</exception>
        public static async Task<Mp4Info?> ReadAsync(Func<long, int, CancellationToken, Task<byte[]>> readRange, long fileSize, long maxMoovBytes = 64L * 1024 * 1024, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(readRange);
            long offset = 0;
            for (int box = 0; box < 10000 && offset + 8 <= fileSize; box++)
            {
                token.ThrowIfCancellationRequested();
                byte[] header = await readRange(offset, 16, token).ConfigureAwait(false);
                if (header.Length < 8) return null;
                long size = U32(header, 0);
                string type = Encoding.ASCII.GetString(header, 4, 4);
                int headerLength = 8;
                if (size == 1)
                {
                    if (header.Length < 16) return null;
                    size = (long)U64(header, 8);
                    headerLength = 16;
                }
                else if (size == 0)
                {
                    size = fileSize - offset;
                }
                if (size < headerLength) return null;
                if (type == "moov")
                {
                    if (size > maxMoovBytes || size > Int32.MaxValue) return null;
                    byte[] moov = await readRange(offset, (int)size, token).ConfigureAwait(false);
                    if (moov.Length < size) return null;
                    return ParseMoov(moov, headerLength, moov.Length);
                }
                offset += size;
            }
            return null;
        }

        /// <summary>
        /// Parse the facts from an in-memory file (tests and small files).
        /// </summary>
        /// <param name="data">File bytes.</param>
        /// <returns>Facts, or null.</returns>
        /// <exception cref="ArgumentNullException">The data is null.</exception>
        public static Mp4Info? Read(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            return ReadAsync((o, l, t) =>
            {
                if (o >= data.Length) return Task.FromResult(Array.Empty<byte>());
                int count = (int)Math.Min(l, data.Length - o);
                byte[] slice = new byte[count];
                Array.Copy(data, o, slice, 0, count);
                return Task.FromResult(slice);
            }, data.Length).GetAwaiter().GetResult();
        }

        #endregion

        #region Private-Methods

        private static Mp4Info? ParseMoov(byte[] data, int start, int end)
        {
            Mp4Info? info = null;
            int? width = null;
            int? height = null;
            int offset = start;
            while (offset + 8 <= end)
            {
                long size = U32(data, offset);
                string type = Encoding.ASCII.GetString(data, offset + 4, 4);
                int headerLength = 8;
                if (size == 1)
                {
                    if (offset + 16 > end) break;
                    size = (long)U64(data, offset + 8);
                    headerLength = 16;
                }
                else if (size == 0)
                {
                    size = end - offset;
                }
                if (size < headerLength || offset + size > end) break;
                int payload = offset + headerLength;
                int boxEnd = (int)(offset + size);
                if (type == "mvhd" && payload + 24 <= boxEnd)
                {
                    byte version = data[payload];
                    double timescale;
                    double duration;
                    if (version == 1)
                    {
                        if (payload + 32 > boxEnd) break;
                        timescale = U32(data, payload + 20);
                        duration = U64(data, payload + 24);
                    }
                    else
                    {
                        timescale = U32(data, payload + 12);
                        duration = U32(data, payload + 16);
                    }
                    if (timescale > 0) info = new Mp4Info { DurationSeconds = duration / timescale };
                }
                else if (type == "trak" && width == null)
                {
                    ReadTrack(data, payload, boxEnd, out width, out height);
                }
                offset = boxEnd;
            }
            if (info != null)
            {
                info.Width = width;
                info.Height = height;
            }
            return info;
        }

        private static void ReadTrack(byte[] data, int start, int end, out int? width, out int? height)
        {
            width = null;
            height = null;
            int offset = start;
            while (offset + 8 <= end)
            {
                long size = U32(data, offset);
                string type = Encoding.ASCII.GetString(data, offset + 4, 4);
                if (size < 8 || offset + size > end) return;
                int payload = offset + 8;
                if (type == "tkhd")
                {
                    byte version = data[payload];
                    int matrix = payload + (version == 1 ? 52 : 40);
                    int dims = matrix + 36;
                    if (dims + 8 > offset + size) return;
                    int w = (int)(U32(data, dims) >> 16);
                    int h = (int)(U32(data, dims + 4) >> 16);
                    if (w <= 0 || h <= 0) return;
                    int a = (int)U32(data, matrix);
                    bool rotated = a == 0;
                    width = rotated ? h : w;
                    height = rotated ? w : h;
                    return;
                }
                offset += (int)size;
            }
        }

        private static long U32(byte[] data, int offset)
        {
            return ((long)data[offset] << 24) | ((long)data[offset + 1] << 16) | ((long)data[offset + 2] << 8) | data[offset + 3];
        }

        private static ulong U64(byte[] data, int offset)
        {
            return ((ulong)U32(data, offset) << 32) | (ulong)U32(data, offset + 4);
        }

        #endregion
    }
}
