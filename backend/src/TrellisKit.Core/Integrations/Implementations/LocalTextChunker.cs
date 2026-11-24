namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// The local chunker (used when Partio is not configured or fails): paragraphs are packed into chunks up to
    /// <see cref="MaxCharacters"/>; a paragraph longer than the budget is split on sentence boundaries, and a sentence
    /// longer than the budget is cut at the budget.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class LocalTextChunker : ITextChunker
    {
        #region Public-Members

        /// <summary>
        /// Chunker name.
        /// </summary>
        public string Name
        {
            get { return "local"; }
        }

        /// <summary>
        /// Character budget of one chunk. Default 1500. Minimum 50, maximum 100000.
        /// </summary>
        public int MaxCharacters
        {
            get { return _MaxCharacters; }
            set { _MaxCharacters = Math.Clamp(value, 50, 100000); }
        }

        #endregion

        #region Private-Members

        private static readonly Regex _Paragraphs = new Regex(@"\n\s*\n", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _Sentences = new Regex(@"(?<=[.!?])\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private int _MaxCharacters = 1500;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<TextChunkResult> ChunkAsync(string text, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(text);
            return Task.FromResult(new TextChunkResult { Chunker = Name, Chunks = Chunk(text, token) });
        }

        /// <summary>
        /// Split text into chunks (synchronous form).
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Chunks.</returns>
        /// <exception cref="ArgumentNullException">The text is null.</exception>
        public List<string> Chunk(string text, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(text);
            List<string> pieces = new List<string>();
            foreach (string paragraph in _Paragraphs.Split(text.Replace("\r\n", "\n")).Select(p => Collapse(p)).Where(p => p.Length > 0))
            {
                token.ThrowIfCancellationRequested();
                if (paragraph.Length <= _MaxCharacters)
                {
                    pieces.Add(paragraph);
                    continue;
                }
                foreach (string sentence in _Sentences.Split(paragraph).Where(s => s.Length > 0))
                {
                    for (int i = 0; i < sentence.Length; i += _MaxCharacters) pieces.Add(sentence.Substring(i, Math.Min(_MaxCharacters, sentence.Length - i)));
                }
            }
            List<string> chunks = new List<string>();
            StringBuilder current = new StringBuilder();
            foreach (string piece in pieces)
            {
                if (current.Length > 0 && current.Length + 1 + piece.Length > _MaxCharacters)
                {
                    chunks.Add(current.ToString());
                    current.Clear();
                }
                if (current.Length > 0) current.Append('\n');
                current.Append(piece);
            }
            if (current.Length > 0) chunks.Add(current.ToString());
            return chunks;
        }

        #endregion

        #region Private-Methods

        private static string Collapse(string paragraph)
        {
            return Regex.Replace(paragraph, @"[ \t\f\v]+", " ").Trim();
        }

        #endregion
    }
}
