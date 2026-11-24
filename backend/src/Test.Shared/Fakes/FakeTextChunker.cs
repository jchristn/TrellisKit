namespace Test.Shared.Fakes
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// A remote chunker stand-in (never a live Partio): splits on words into chunks of a fixed size, or throws, and
    /// summarizes with a fixed text.
    /// </summary>
    public sealed class FakeTextChunker : ITextChunker, ITextSummarizer
    {
        /// <summary>
        /// Words per chunk.
        /// </summary>
        public int WordsPerChunk { get; set; } = 3;

        /// <summary>
        /// When true, chunking throws InvalidOperationException (the service falls back to the local chunker).
        /// </summary>
        public bool Fail { get; set; } = false;

        /// <summary>
        /// Summary returned when enabled.
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <summary>
        /// Calls so far.
        /// </summary>
        public int Calls { get; private set; } = 0;

        /// <inheritdoc />
        public string Name
        {
            get { return "partio"; }
        }

        /// <inheritdoc />
        public bool Enabled
        {
            get { return Summary != null; }
        }

        /// <inheritdoc />
        public Task<TextChunkResult> ChunkAsync(string text, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(text);
            Calls++;
            if (Fail) throw new InvalidOperationException("Fake Partio failure.");
            string[] words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            TextChunkResult result = new TextChunkResult { Chunker = Name };
            for (int i = 0; i < words.Length; i += WordsPerChunk) result.Chunks.Add(String.Join(" ", words.Skip(i).Take(WordsPerChunk)));
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public Task<string?> SummarizeAsync(string text, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(text);
            return Task.FromResult(Summary);
        }
    }
}
