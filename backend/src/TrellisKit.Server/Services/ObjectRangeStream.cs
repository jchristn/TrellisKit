namespace TrellisKit.Server.Services
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A forward-only, read-only stream over a byte range of a stored object, read in bounded chunks so large private files
    /// (video) stream without being loaded into memory. Not thread safe.
    /// </summary>
    public sealed class ObjectRangeStream : Stream
    {
        #region Public-Members

        /// <inheritdoc />
        public override bool CanRead
        {
            get { return true; }
        }

        /// <inheritdoc />
        public override bool CanSeek
        {
            get { return false; }
        }

        /// <inheritdoc />
        public override bool CanWrite
        {
            get { return false; }
        }

        /// <inheritdoc />
        public override long Length
        {
            get { return _End - _Start; }
        }

        /// <inheritdoc />
        public override long Position
        {
            get { return _Position - _Start; }
            set { throw new NotSupportedException("The stream is forward-only."); }
        }

        /// <summary>
        /// Bytes read from storage per chunk. Default 1 MiB. Minimum 64 KiB, maximum 4 MiB.
        /// </summary>
        public int ChunkBytes
        {
            get { return _ChunkBytes; }
            set { _ChunkBytes = Math.Clamp(value, 64 * 1024, 4 * 1024 * 1024); }
        }

        #endregion

        #region Private-Members

        private readonly Func<long, int, CancellationToken, Task<byte[]>> _Reader;
        private readonly long _Start;
        private readonly long _End;
        private long _Position;
        private byte[] _Buffer = Array.Empty<byte>();
        private int _BufferOffset = 0;
        private int _ChunkBytes = 1024 * 1024;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="reader">Reads (offset, length) from the object.</param>
        /// <param name="start">First byte (inclusive).</param>
        /// <param name="endExclusive">Last byte (exclusive).</param>
        /// <exception cref="ArgumentNullException">The reader is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The range is invalid.</exception>
        public ObjectRangeStream(Func<long, int, CancellationToken, Task<byte[]>> reader, long start, long endExclusive)
        {
            _Reader = reader ?? throw new ArgumentNullException(nameof(reader));
            if (start < 0 || endExclusive < start) throw new ArgumentOutOfRangeException(nameof(start), "The range " + start + " to " + endExclusive + " is invalid.");
            _Start = start;
            _End = endExclusive;
            _Position = start;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <inheritdoc />
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            if (count <= 0 || _Position >= _End) return 0;
            if (_BufferOffset >= _Buffer.Length)
            {
                int want = (int)Math.Min(_ChunkBytes, _End - _Position);
                _Buffer = await _Reader(_Position, want, cancellationToken).ConfigureAwait(false);
                _BufferOffset = 0;
                if (_Buffer.Length == 0) return 0;
            }
            int n = Math.Min(count, _Buffer.Length - _BufferOffset);
            n = (int)Math.Min(n, _End - _Position);
            Array.Copy(_Buffer, _BufferOffset, buffer, offset, n);
            _BufferOffset += n;
            _Position += n;
            return n;
        }

        /// <inheritdoc />
        public override void Flush()
        {
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException("The stream is forward-only.");
        }

        /// <inheritdoc />
        public override void SetLength(long value)
        {
            throw new NotSupportedException("The stream is read-only.");
        }

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("The stream is read-only.");
        }

        #endregion
    }
}
