namespace TrellisKit.Core.Integrations.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Extracts the text of a document (resolved decision 18: PDF resources through DocumentAtom, WebVTT captions
    /// through a local parser). Implementations must be thread safe.
    /// </summary>
    public interface IDocumentTextExtractor
    {
        /// <summary>
        /// Extractor name (low cardinality, for telemetry and the extraction row).
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Whether the extractor handles a content type.
        /// </summary>
        /// <param name="contentType">Content type (application/pdf or text/vtt).</param>
        /// <returns>True when supported.</returns>
        bool Supports(string contentType);

        /// <summary>
        /// Extract the text of a document.
        /// </summary>
        /// <param name="data">Document bytes.</param>
        /// <param name="contentType">Content type.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Extracted text.</returns>
        /// <exception cref="ArgumentNullException">The data is null.</exception>
        /// <exception cref="InvalidOperationException">The document cannot be read.</exception>
        Task<ExtractedDocument> ExtractAsync(byte[] data, string contentType, CancellationToken token = default);
    }
}
