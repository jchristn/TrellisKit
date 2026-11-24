namespace TrellisKit.Server.Services
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using WatsonWebserver.Core;

    /// <summary>
    /// A read-only stream over a chunked (Transfer-Encoding: chunked) Watson request body.
    /// Thread safety: not thread safe; one stream per request.
    /// </summary>
    public class ChunkedRequestStream : Stream
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
            get { throw new NotSupportedException("A chunked request body has no length."); }
        }

        /// <inheritdoc />
        public override long Position
        {
            get { throw new NotSupportedException("A chunked request body is not seekable."); }
            set { throw new NotSupportedException("A chunked request body is not seekable."); }
        }

        #endregion

        #region Private-Members

        private readonly HttpRequestBase _Request;
        private byte[] _Current = Array.Empty<byte>();
        private int _Offset = 0;
        private bool _Done = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the stream.
        /// </summary>
        /// <param name="request">Watson request whose ChunkedTransfer is true.</param>
        /// <exception cref="ArgumentNullException">The request is null.</exception>
        public ChunkedRequestStream(HttpRequestBase request)
        {
            _Request = request ?? throw new ArgumentNullException(nameof(request));
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
            return await ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (_Offset >= _Current.Length)
            {
                if (_Done) return 0;
                Chunk chunk = await _Request.ReadChunk(cancellationToken).ConfigureAwait(false);
                _Current = chunk.Data ?? Array.Empty<byte>();
                _Offset = 0;
                if (chunk.IsFinal) _Done = true;
            }
            int count = Math.Min(buffer.Length, _Current.Length - _Offset);
            _Current.AsMemory(_Offset, count).CopyTo(buffer);
            _Offset += count;
            return count;
        }

        /// <inheritdoc />
        public override void Flush()
        {
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException("A chunked request body is not seekable.");
        }

        /// <inheritdoc />
        public override void SetLength(long value)
        {
            throw new NotSupportedException("A chunked request body is read-only.");
        }

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("A chunked request body is read-only.");
        }

        #endregion
    }
}
