namespace TrellisKit.Core.Integrations.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Splits text into chunks for search and assistant grounding (resolved decision 18: Partio when configured, a
    /// local paragraph and sentence chunker otherwise). Implementations must be thread safe.
    /// </summary>
    public interface ITextChunker
    {
        /// <summary>
        /// Chunker name (low cardinality).
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Split text into chunks.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Chunks.</returns>
        /// <exception cref="ArgumentNullException">The text is null.</exception>
        /// <exception cref="InvalidOperationException">The chunker failed (callers fall back to the local chunker).</exception>
        Task<TextChunkResult> ChunkAsync(string text, CancellationToken token = default);
    }
}
