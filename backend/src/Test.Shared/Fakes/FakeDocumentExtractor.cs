namespace Test.Shared.Fakes
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// A document extractor that returns fixed text per page (or throws), for document text tests.
    /// </summary>
    public sealed class FakeDocumentExtractor : IDocumentTextExtractor
    {
        /// <summary>
        /// Pages returned in order (page numbers 1, 2, ...).
        /// </summary>
        public string[] Pages { get; set; } = new[] { "Fake page text." };

        /// <summary>
        /// When true, extraction throws InvalidOperationException.
        /// </summary>
        public bool Fail { get; set; } = false;

        /// <summary>
        /// Calls so far.
        /// </summary>
        public int Calls { get; private set; } = 0;

        /// <inheritdoc />
        public string Name
        {
            get { return "fake-pdf"; }
        }

        /// <inheritdoc />
        public bool Supports(string contentType)
        {
            return String.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        public Task<ExtractedDocument> ExtractAsync(byte[] data, string contentType, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(data);
            Calls++;
            if (Fail) throw new InvalidOperationException("Fake extraction failure.");
            ExtractedDocument document = new ExtractedDocument { Extractor = Name };
            for (int i = 0; i < Pages.Length; i++) document.Segments.Add(new ExtractedSegment { Text = Pages[i], PageNumber = i + 1 });
            return Task.FromResult(document);
        }
    }
}
