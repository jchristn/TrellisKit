namespace TrellisKit.Core.Integrations.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Summarizes text (resolved decision 18: Partio through a configured completion endpoint). Implementations must
    /// be thread safe.
    /// </summary>
    public interface ITextSummarizer
    {
        /// <summary>
        /// Whether summaries are configured and turned on.
        /// </summary>
        bool Enabled { get; }

        /// <summary>
        /// Summarize text.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Summary, or null when nothing was produced.</returns>
        /// <exception cref="ArgumentNullException">The text is null.</exception>
        /// <exception cref="InvalidOperationException">The summarizer failed.</exception>
        Task<string?> SummarizeAsync(string text, CancellationToken token = default);
    }
}
