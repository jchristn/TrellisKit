namespace TrellisKit.Server.Services
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Streaming multipart/form-data reader (RFC 7578). Part bodies are delivered in chunks to a sink and never
    /// buffered whole, so uploads of hundreds of megabytes use a fixed amount of memory.
    /// Thread safety: not thread safe; one reader per request.
    /// </summary>
    public class MultipartFormReader
    {
        #region Public-Members

        /// <summary>
        /// Largest header block of one part in bytes. Default 16384. Minimum 1024, maximum 1048576.
        /// </summary>
        public int MaxHeaderBytes
        {
            get { return _MaxHeaderBytes; }
            set { _MaxHeaderBytes = Math.Clamp(value, 1024, 1048576); }
        }

        #endregion

        #region Private-Members

        private static readonly byte[] _HeaderEnd = Encoding.ASCII.GetBytes("\r\n\r\n");
        private readonly Stream _Body;
        private readonly byte[] _Delimiter;
        private byte[] _Buffer;
        private int _Start = 0;
        private int _End = 0;
        private bool _Eof = false;
        private bool _Started = false;
        private bool _Finished = false;
        private bool _BodyPending = false;
        private int _MaxHeaderBytes = 16384;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the reader.
        /// </summary>
        /// <param name="body">Request body stream.</param>
        /// <param name="boundary">Boundary from the Content-Type header (without leading dashes).</param>
        /// <param name="bufferSize">Read buffer size in bytes. Minimum 4096.</param>
        /// <exception cref="ArgumentNullException">The body or boundary is null or empty.</exception>
        public MultipartFormReader(Stream body, string boundary, int bufferSize = 65536)
        {
            _Body = body ?? throw new ArgumentNullException(nameof(body));
            if (String.IsNullOrEmpty(boundary)) throw new ArgumentNullException(nameof(boundary));
            _Delimiter = Encoding.ASCII.GetBytes("\r\n--" + boundary);
            _Buffer = new byte[Math.Max(4096, bufferSize) + _Delimiter.Length];
            // A virtual CRLF before the body lets the first boundary match the same delimiter as the others.
            _Buffer[0] = (byte)'\r';
            _Buffer[1] = (byte)'\n';
            _End = 2;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The boundary parameter of a multipart Content-Type header.
        /// </summary>
        /// <param name="contentType">Content-Type value.</param>
        /// <returns>Boundary, or null.</returns>
        public static string? BoundaryOf(string? contentType)
        {
            if (String.IsNullOrEmpty(contentType)) return null;
            foreach (string part in contentType.Split(';'))
            {
                string trimmed = part.Trim();
                if (!trimmed.StartsWith("boundary=", StringComparison.OrdinalIgnoreCase)) continue;
                string value = trimmed.Substring(9).Trim().Trim('"');
                return value.Length == 0 || value.Length > 200 ? null : value;
            }
            return null;
        }

        /// <summary>
        /// Advance to the next part. An unread body of the previous part is skipped.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The part's headers, or null after the closing boundary.</returns>
        /// <exception cref="InvalidDataException">The body is not valid multipart data.</exception>
        public async Task<MultipartSection?> NextAsync(CancellationToken token = default)
        {
            if (_Finished) return null;
            if (!_Started)
            {
                _Started = true;
                await SkipToDelimiterAsync(token).ConfigureAwait(false);
            }
            else if (_BodyPending)
            {
                await ReadBodyAsync(null, Int64.MaxValue, token).ConfigureAwait(false);
            }

            await EnsureAsync(2, token).ConfigureAwait(false);
            if (_End - _Start >= 2 && _Buffer[_Start] == '-' && _Buffer[_Start + 1] == '-')
            {
                _Finished = true;
                return null;
            }

            int headerEnd;
            while (true)
            {
                headerEnd = _Buffer.AsSpan(_Start, _End - _Start).IndexOf(_HeaderEnd);
                if (headerEnd >= 0) break;
                if (_End - _Start > _MaxHeaderBytes) throw new InvalidDataException("A multipart part header block is too large.");
                if (!await FillAsync(token).ConfigureAwait(false)) throw new InvalidDataException("The multipart body ended inside a part header.");
            }
            string headerText = Encoding.UTF8.GetString(_Buffer, _Start, headerEnd);
            _Start += headerEnd + _HeaderEnd.Length;
            MultipartSection section = new MultipartSection();
            foreach (string line in headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                section.Headers[line.Substring(0, colon).Trim().ToLowerInvariant()] = line.Substring(colon + 1).Trim();
            }
            if (section.Headers.TryGetValue("content-type", out string? type) && !String.IsNullOrWhiteSpace(type)) section.ContentType = type;
            if (section.Headers.TryGetValue("content-disposition", out string? disposition))
            {
                section.Name = Parameter(disposition, "name") ?? String.Empty;
                section.FileName = Parameter(disposition, "filename");
            }
            _BodyPending = true;
            return section;
        }

        /// <summary>
        /// Read the current part's body in chunks.
        /// </summary>
        /// <param name="sink">Receives each chunk (buffer, offset, count); null discards the body.</param>
        /// <param name="maxBytes">Largest accepted body; larger bodies throw.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Body length in bytes.</returns>
        /// <exception cref="InvalidDataException">The body is truncated.</exception>
        /// <exception cref="PayloadTooLargeException">The body exceeds maxBytes.</exception>
        public async Task<long> ReadBodyAsync(Func<byte[], int, int, Task>? sink, long maxBytes, CancellationToken token = default)
        {
            long total = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                int found = _Buffer.AsSpan(_Start, _End - _Start).IndexOf(_Delimiter);
                if (found >= 0)
                {
                    total += found;
                    if (total > maxBytes) throw new PayloadTooLargeException("A multipart part is larger than " + maxBytes + " bytes.");
                    if (sink != null && found > 0) await sink(_Buffer, _Start, found).ConfigureAwait(false);
                    _Start += found + _Delimiter.Length;
                    _BodyPending = false;
                    return total;
                }
                int safe = (_End - _Start) - (_Delimiter.Length - 1);
                if (safe > 0)
                {
                    total += safe;
                    if (total > maxBytes) throw new PayloadTooLargeException("A multipart part is larger than " + maxBytes + " bytes.");
                    if (sink != null) await sink(_Buffer, _Start, safe).ConfigureAwait(false);
                    _Start += safe;
                }
                if (!await FillAsync(token).ConfigureAwait(false)) throw new InvalidDataException("The multipart body ended inside a part.");
            }
        }

        #endregion

        #region Private-Methods

        private async Task SkipToDelimiterAsync(CancellationToken token)
        {
            while (true)
            {
                int found = _Buffer.AsSpan(_Start, _End - _Start).IndexOf(_Delimiter);
                if (found >= 0)
                {
                    _Start += found + _Delimiter.Length;
                    return;
                }
                int safe = (_End - _Start) - (_Delimiter.Length - 1);
                if (safe > 0) _Start += safe;
                if (!await FillAsync(token).ConfigureAwait(false)) throw new InvalidDataException("The multipart body has no boundary.");
            }
        }

        private async Task EnsureAsync(int count, CancellationToken token)
        {
            while (_End - _Start < count)
            {
                if (!await FillAsync(token).ConfigureAwait(false)) throw new InvalidDataException("The multipart body ended early.");
            }
        }

        private async Task<bool> FillAsync(CancellationToken token)
        {
            if (_Eof) return false;
            if (_Start > 0)
            {
                Buffer.BlockCopy(_Buffer, _Start, _Buffer, 0, _End - _Start);
                _End -= _Start;
                _Start = 0;
            }
            if (_End == _Buffer.Length)
            {
                byte[] larger = new byte[_Buffer.Length * 2];
                Buffer.BlockCopy(_Buffer, 0, larger, 0, _End);
                _Buffer = larger;
            }
            int read = await _Body.ReadAsync(_Buffer.AsMemory(_End, _Buffer.Length - _End), token).ConfigureAwait(false);
            if (read <= 0)
            {
                _Eof = true;
                return false;
            }
            _End += read;
            return true;
        }

        private static string? Parameter(string header, string name)
        {
            foreach (string part in header.Split(';'))
            {
                string trimmed = part.Trim();
                int equals = trimmed.IndexOf('=');
                if (equals <= 0) continue;
                if (!String.Equals(trimmed.Substring(0, equals).Trim(), name, StringComparison.OrdinalIgnoreCase)) continue;
                return trimmed.Substring(equals + 1).Trim().Trim('"');
            }
            return null;
        }

        #endregion
    }
}
