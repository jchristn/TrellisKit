namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Caption text extraction from WebVTT (DocumentAtom has no WebVTT processor): the header, cue identifiers,
    /// timings, NOTE, STYLE, and REGION blocks, and inline tags are dropped (<see cref="WebVttParser"/>).
    /// Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public class WebVttTextExtractor : IDocumentTextExtractor
    {
        #region Public-Members

        /// <summary>
        /// Extractor name.
        /// </summary>
        public string Name
        {
            get { return "webvtt"; }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public bool Supports(string contentType)
        {
            return String.Equals((contentType ?? String.Empty).Trim(), "text/vtt", StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        public Task<ExtractedDocument> ExtractAsync(byte[] data, string contentType, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(data);
            token.ThrowIfCancellationRequested();
            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(data);
            }
            catch (DecoderFallbackException e)
            {
                throw new InvalidOperationException("The caption file is not UTF-8 text.", e);
            }
            if (!WebVttParser.Validate(text, out string? error)) throw new InvalidOperationException(error ?? "Not a WebVTT file.");
            ExtractedDocument document = new ExtractedDocument { Extractor = Name };
            string spoken = WebVttParser.ExtractText(text);
            if (spoken.Length > 0) document.Segments.Add(new ExtractedSegment { Text = spoken });
            return Task.FromResult(document);
        }

        #endregion
    }
}
