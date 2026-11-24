namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;

    /// <summary>
    /// Document text for search and assistants (resolved decision 18): Document media used by lessons (PDF resources
    /// and WebVTT captions) are queued when they become Ready or are attached; the document_text worker extracts their
    /// text (DocumentAtom for PDF, the local WebVTT parser for captions), chunks it with Partio when configured (the
    /// local paragraph and sentence chunker otherwise, or when Partio fails), optionally summarizes it, and stores the
    /// chunks in documentchunks. Lesson search documents and assistant knowledge read the chunks back.
    /// Thread safety: safe to call concurrently; the worker lease serializes passes per property.
    /// </summary>
    public class DocumentTextService : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// PDF extractor (replaceable in tests). Default: DocumentAtom.
        /// </summary>
        public IDocumentTextExtractor PdfExtractor
        {
            get { return _PdfExtractor; }
            set { _PdfExtractor = value ?? throw new ArgumentNullException(nameof(PdfExtractor)); }
        }

        /// <summary>
        /// Caption extractor (replaceable in tests). Default: the local WebVTT parser.
        /// </summary>
        public IDocumentTextExtractor CaptionExtractor
        {
            get { return _CaptionExtractor; }
            set { _CaptionExtractor = value ?? throw new ArgumentNullException(nameof(CaptionExtractor)); }
        }

        /// <summary>
        /// Remote chunker used when documents.partio.enabled (replaceable in tests). Default: Partio over HTTP.
        /// </summary>
        public ITextChunker RemoteChunker
        {
            get { return _RemoteChunker; }
            set { _RemoteChunker = value ?? throw new ArgumentNullException(nameof(RemoteChunker)); }
        }

        /// <summary>
        /// Summarizer used when documents.partio.summarize (replaceable in tests). Default: Partio over HTTP.
        /// </summary>
        public ITextSummarizer Summarizer
        {
            get { return _Summarizer; }
            set { _Summarizer = value ?? throw new ArgumentNullException(nameof(Summarizer)); }
        }

        /// <summary>
        /// Whether the remote chunker is used (default: documents.partio.enabled with a base URL). Tests set this to force
        /// the remote path with a fake.
        /// </summary>
        public Func<bool> UseRemoteChunker { get; set; }

        /// <summary>
        /// Most characters of document text one lesson contributes to search and assistant knowledge. Default 20000.
        /// Minimum 0, maximum 1000000.
        /// </summary>
        public int MaxCharactersPerLesson
        {
            get { return _MaxCharactersPerLesson; }
            set { _MaxCharactersPerLesson = Math.Clamp(value, 0, 1000000); }
        }

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private readonly PartioTextService _Partio;
        private IDocumentTextExtractor _PdfExtractor;
        private IDocumentTextExtractor _CaptionExtractor = new WebVttTextExtractor();
        private ITextChunker _RemoteChunker;
        private ITextSummarizer _Summarizer;
        private int _MaxCharactersPerLesson = 20000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public DocumentTextService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            _Partio = new PartioTextService(() => _Services.Settings.Library);
            _RemoteChunker = _Partio;
            _Summarizer = _Partio;
            _PdfExtractor = new DocumentAtomPdfExtractor();
            UseRemoteChunker = () => _Partio.Configured;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Queue a Ready Document media for extraction (no effect when documents.textEnabled is off).
        /// </summary>
        /// <param name="media">Media.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when queued.</returns>
        public async Task<bool> EnqueueAsync(MediaAttachment media, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(media);
            if (!_Services.Settings.Library.DocumentTextEnabled || media.Kind != MediaKindEnum.Document) return false;
            return await _Services.Database.Library.Documents.EnqueueAsync(media.Id, media.ContentType, DateTime.UtcNow, token).ConfigureAwait(false);
        }

        /// <summary>
        /// One worker pass over pending documents.
        /// </summary>
        /// <param name="limit">Most documents (1 to 100).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Documents processed.</returns>
        public async Task<int> RunAsync(int limit, CancellationToken token = default)
        {
            if (!_Services.Settings.Library.DocumentTextEnabled) return 0;
            List<DocumentExtraction> pending = await _Services.Database.Library.Documents.PendingAsync(Math.Clamp(limit, 1, 100), token).ConfigureAwait(false);
            int processed = 0;
            foreach (DocumentExtraction row in pending)
            {
                token.ThrowIfCancellationRequested();
                await ProcessAsync(row, token).ConfigureAwait(false);
                processed++;
            }
            return processed;
        }

        /// <summary>
        /// The extracted text of a lesson's captions and PDF resources (for its search document and assistant
        /// knowledge), at most <see cref="MaxCharactersPerLesson"/> characters.
        /// </summary>
        /// <param name="lesson">Lesson (with child lists).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Text (empty when nothing was extracted).</returns>
        public async Task<string> LessonTextAsync(Lesson lesson, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(lesson);
            List<string> media = lesson.Captions.Select(c => c.MediaId).Concat(lesson.Resources.Where(r => r.MediaId != null).Select(r => r.MediaId!)).Distinct(StringComparer.Ordinal).ToList();
            if (media.Count == 0 || _MaxCharactersPerLesson == 0) return String.Empty;
            List<DocumentChunk> chunks = await _Services.Database.Library.Documents.ChunksAsync(media, 2000, token).ConfigureAwait(false);
            StringBuilder sb = new StringBuilder();
            foreach (DocumentChunk chunk in chunks)
            {
                if (sb.Length + chunk.Text.Length + 1 > _MaxCharactersPerLesson) break;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(chunk.Text);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Dispose the Partio client.
        /// </summary>
        public void Dispose()
        {
            _Partio.Dispose();
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        private async Task ProcessAsync(DocumentExtraction row, CancellationToken token)
        {
            DateTime now = DateTime.UtcNow;
            row.Attempts++;
            row.UpdatedUtc = now;
            string kind = row.ContentType == "text/vtt" ? "vtt" : "pdf";
            MediaAttachment? media = await _Services.Database.Media.ReadAsync(row.MediaId, token).ConfigureAwait(false);
            if (media == null || media.IsDeleted || media.Status != MediaStatusEnum.Ready || media.Kind != MediaKindEnum.Document)
            {
                row.Status = DocumentExtractionStatusEnum.Skipped;
                row.Error = "The media is not a Ready document.";
                await _Services.Database.Library.Documents.StoreAsync(row, new List<DocumentChunk>(), token).ConfigureAwait(false);
                LibraryTelemetry.Count(LibraryTelemetry.DocumentExtractions, "content_type", kind, "chunker", "none", "outcome", "skipped");
                return;
            }
            IDocumentTextExtractor extractor = _CaptionExtractor.Supports(media.ContentType) ? _CaptionExtractor : _PdfExtractor;
            try
            {
                if (extractor is DocumentAtomPdfExtractor atom) atom.TempDirectory = _Services.Settings.Library.DocumentTempDirectory;
                byte[] data = await ReadAsync(media, token).ConfigureAwait(false);
                ExtractedDocument document = await extractor.ExtractAsync(data, media.ContentType, token).ConfigureAwait(false);
                List<DocumentChunk> chunks = new List<DocumentChunk>();
                string chunker = "local";
                int budget = _Services.Settings.Library.DocumentMaxCharacters;
                int characters = 0;
                StringBuilder all = new StringBuilder();
                foreach (IGrouping<int?, ExtractedSegment> page in document.Segments.GroupBy(s => s.PageNumber))
                {
                    string text = String.Join("\n\n", page.Select(s => s.Text));
                    if (characters + text.Length > budget) text = text.Substring(0, Math.Max(0, budget - characters));
                    if (text.Trim().Length == 0) continue;
                    characters += text.Length;
                    all.Append(text).Append("\n\n");
                    TextChunkResult result = await ChunkAsync(text, token).ConfigureAwait(false);
                    chunker = result.Chunker;
                    foreach (string chunk in result.Chunks)
                        chunks.Add(new DocumentChunk { Id = IdGenerator.Generate(LibraryIdPrefixes.Chunk), MediaId = media.Id, Position = chunks.Count, PageNumber = page.Key, Text = chunk, CreatedUtc = now });
                    if (characters >= budget) break;
                }
                row.Summary = null;
                if (all.Length > 0 && _Summarizer.Enabled)
                {
                    try
                    {
                        row.Summary = await _Summarizer.SummarizeAsync(all.ToString(), token).ConfigureAwait(false);
                    }
                    catch (InvalidOperationException e)
                    {
                        _Services.Logger.Warn("[DocumentTextService] summary of " + media.Id + " failed: " + e.Message);
                    }
                }
                row.Status = chunks.Count == 0 ? DocumentExtractionStatusEnum.Skipped : DocumentExtractionStatusEnum.Ready;
                row.Extractor = document.Extractor;
                row.Chunker = chunks.Count == 0 ? null : chunker;
                row.CharacterCount = characters;
                row.Error = chunks.Count == 0 ? "No text found." : null;
                row.ExtractedUtc = now;
                await _Services.Database.Library.Documents.StoreAsync(row, chunks, token).ConfigureAwait(false);
                LibraryTelemetry.Count(LibraryTelemetry.DocumentExtractions, "content_type", kind, "chunker", chunks.Count == 0 ? "none" : chunker, "outcome", chunks.Count == 0 ? "skipped" : "ready");
                foreach (string lessonId in await _Services.Database.Library.Catalog.LessonIdsUsingMediaAsync(media.Id, token).ConfigureAwait(false))
                    await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Lesson, lessonId, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is InvalidOperationException || e is IOException || e is FileNotFoundException || e is UnauthorizedAccessException)
            {
                bool final = row.Attempts >= _Services.Settings.Library.DocumentMaxAttempts;
                row.Status = final ? DocumentExtractionStatusEnum.Failed : DocumentExtractionStatusEnum.Pending;
                row.Error = e.Message.Length > 500 ? e.Message.Substring(0, 500) : e.Message;
                await _Services.Database.Library.Documents.StoreAsync(row, new List<DocumentChunk>(), token).ConfigureAwait(false);
                LibraryTelemetry.Count(LibraryTelemetry.DocumentExtractions, "content_type", kind, "chunker", "none", "outcome", final ? "failed" : "retry");
                _Services.Logger.Warn("[DocumentTextService] extraction of " + media.Id + " failed (attempt " + row.Attempts + "): " + e.Message);
            }
        }

        private async Task<TextChunkResult> ChunkAsync(string text, CancellationToken token)
        {
            if (UseRemoteChunker())
            {
                try
                {
                    TextChunkResult remote = await _RemoteChunker.ChunkAsync(text, token).ConfigureAwait(false);
                    if (remote.Chunks.Count > 0) return remote;
                }
                catch (InvalidOperationException e)
                {
                    _Services.Logger.Warn("[DocumentTextService] remote chunking failed, using the local chunker: " + e.Message);
                }
            }
            LocalTextChunker local = new LocalTextChunker { MaxCharacters = _Services.Settings.Library.DocumentChunkCharacters };
            return await local.ChunkAsync(text, token).ConfigureAwait(false);
        }

        private async Task<byte[]> ReadAsync(MediaAttachment media, CancellationToken token)
        {
            List<MediaVariant> variants = await _Services.Database.Media.ReadVariantsAsync(media.Id, token).ConfigureAwait(false);
            MediaVariant? file = variants.FirstOrDefault(v => v.Name == MediaUrlBuilder.DocumentVariantName);
            if (file != null)
            {
                try
                {
                    return await _Services.Integrations.ObjectStore.ReadAllAsync(MediaLocationEnum.Images, MediaKeys.ImagePrefix(_Services.PropertyId, media.Id) + file.FileName, token).ConfigureAwait(false);
                }
                catch (FileNotFoundException)
                {
                    // Fall back to the original below.
                }
            }
            return await _Services.Integrations.ObjectStore.ReadAllAsync(media.SourceLocation, media.SourceKey, token).ConfigureAwait(false);
        }

        #endregion
    }
}
