namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared.Fakes;
    using Touchstone.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Document media and document text (docs/PHASE4_CONTRACT.md sections 5.3 and 5.4, resolved decision 18): PDF and
    /// WebVTT validation, serving headers, lesson resources and captions, extraction and chunking with fakes, the local
    /// fallback chunker, a real DocumentAtom extraction of a generated PDF, and caption text in search documents.
    /// </summary>
    public static class DocumentMediaSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "DocumentMedia";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Document media (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Http(Id, "Documents.ValidateAndServe", "PDF needs %PDF- and WebVTT needs WEBVTT and well-formed timings; Ready documents carry media.document.url; PDFs are served as attachments with nosniff and CSP sandbox", ValidateAsync),
                SuiteCases.Http(Id, "Documents.ExtractAndChunk", "Lesson PDF resources and captions are queued and extracted; the remote chunker is used when on, the local chunker when it fails; failures retry then fail; lesson search documents include caption and PDF text", ExtractAsync),
                SuiteCases.Unit(Id, "Documents.RealPdf", "DocumentAtom extracts the text of a generated one-page PDF through a temporary file", RealPdfAsync),
                SuiteCases.Unit(Id, "Documents.ParsersAndChunker", "WebVTT validation and text extraction drop headers, notes, ids, timings, and tags; the local chunker packs paragraphs and splits long ones on sentences within the budget", ParsersAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static async Task ValidateAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            string pdf = await MediaTestKit.UploadSingleAsync(f, admin.AccessToken, "Document", "application/pdf", DocumentKit.Pdf("Lesson notes"), ct).ConfigureAwait(false);
            await MediaTestKit.ProcessAsync(f, ct).ConfigureAwait(false);
            JsonElement media = await MediaTestKit.GetMediaAsync(f, admin.AccessToken, pdf, ct).ConfigureAwait(false);
            Check.Equal("Ready", Api.Str(media, "status"), "PDF Ready");
            Check.Equal("Document", Api.Str(media, "kind"), "Document kind");
            string url = Api.Str(media.GetProperty("document"), "url");
            Check.True(url.EndsWith("/file.pdf", StringComparison.Ordinal), "Served as file.pdf (" + url + ")");
            MediaHttpResult file = await MediaTestKit.SendAsync(f, HttpMethod.Get, url, admin.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, file.Status, "PDF served");
            Check.True((file.Header("Content-Disposition") ?? String.Empty).StartsWith("attachment", StringComparison.Ordinal), "Content-Disposition attachment");
            Check.Equal("sandbox", file.Header("Content-Security-Policy"), "CSP sandbox");
            Check.Equal("nosniff", file.Header("X-Content-Type-Options"), "nosniff");
            Check.True((file.Header("Content-Type") ?? String.Empty).StartsWith("application/pdf", StringComparison.Ordinal), "application/pdf");

            string vtt = await MediaTestKit.UploadSingleAsync(f, admin.AccessToken, "Document", "text/vtt", DocumentKit.Vtt("Hello there"), ct).ConfigureAwait(false);
            string badVtt = await MediaTestKit.UploadSingleAsync(f, admin.AccessToken, "Document", "text/vtt", Encoding.UTF8.GetBytes("WEBVTT\n\n00:00 --> broken\nText\n"), ct).ConfigureAwait(false);
            await MediaTestKit.ProcessAsync(f, ct).ConfigureAwait(false);
            Check.Equal("Ready", Api.Str(await MediaTestKit.GetMediaAsync(f, admin.AccessToken, vtt, ct).ConfigureAwait(false), "status"), "WebVTT Ready");
            JsonElement failed = await MediaTestKit.GetMediaAsync(f, admin.AccessToken, badVtt, ct).ConfigureAwait(false);
            Check.Equal("Failed", Api.Str(failed, "status"), "Malformed timings fail");
            JsonElement session = await MediaTestKit.CreateSessionAsync(f, admin.AccessToken, "Document", "application/pdf", 12, null, ct).ConfigureAwait(false);
            string id = Api.Str(session.GetProperty("media"), "id");
            await MediaTestKit.PutBytesAsync(f, Api.Str(session.GetProperty("upload").GetProperty("single"), "url"), Encoding.ASCII.GetBytes("not a pdf ok"), "application/pdf", ct).ConfigureAwait(false);
            MediaHttpResult complete = await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/media/" + id + "/complete", admin.AccessToken, new { }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.UnsupportedMediaType, complete.Status, "Bytes that are not a PDF are refused");
            MediaHttpResult wrongType = await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/media/uploads", admin.AccessToken, new { kind = "Document", contentType = "text/html", sizeBytes = 10 }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.UnsupportedMediaType, wrongType.Status, "Only PDF and WebVTT documents");
            f.Settings.Media.MaxDocumentBytes = 1048576;
            MediaHttpResult big = await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/media/uploads", admin.AccessToken, new { kind = "Document", contentType = "application/pdf", sizeBytes = 2000000 }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.RequestEntityTooLarge, big.Status, "media.maxDocumentBytes");
        }

        private static async Task ExtractAsync(HttpFixture f, CancellationToken ct)
        {
            LibraryTestKit.Relax(f);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            FakeDocumentExtractor extractor = new FakeDocumentExtractor { Pages = new[] { "Grip fighting starts with posture and distance.", "Breaking grips needs two hands." } };
            FakeTextChunker chunker = new FakeTextChunker { WordsPerChunk = 4, Summary = "A short summary." };
            DocumentTextService documents = f.Services.Library.Documents;
            documents.PdfExtractor = extractor;
            documents.RemoteChunker = chunker;
            documents.Summarizer = chunker;
            documents.UseRemoteChunker = () => true;
            string pdf = await MediaTestKit.UploadSingleAsync(f, admin.AccessToken, "Document", "application/pdf", DocumentKit.Pdf("notes"), ct).ConfigureAwait(false);
            string vtt = await MediaTestKit.UploadSingleAsync(f, admin.AccessToken, "Document", "text/vtt", DocumentKit.Vtt("Welcome to the <b>gripping</b> lesson", "Keep your elbows in"), ct).ConfigureAwait(false);
            await MediaTestKit.ProcessAsync(f, ct).ConfigureAwait(false);
            string seriesId = Api.Str(await LibraryTestKit.SeriesAsync(f, admin.AccessToken, "Documented", ct).ConfigureAwait(false), "id");
            JsonElement lesson = await LibraryTestKit.LessonAsync(f, admin.AccessToken, seriesId, "Gripping", ct, new
            {
                resources = new object[] { new { kind = "Document", title = "Notes", mediaId = pdf }, new { kind = "Link", title = "Reading", url = "https://example.test/reading" }, new { kind = "Reference", title = "Handbook", text = "Chapter 3" } },
                captions = new[] { new { language = "en", label = "English", mediaId = vtt } }
            }).ConfigureAwait(false);
            string lessonId = Api.Str(lesson, "id");
            JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/lessons/" + lessonId, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(3, detail.GetProperty("resources").GetArrayLength(), "Three resources");
            Check.True(Api.Str(detail.GetProperty("resources")[0].GetProperty("media").GetProperty("document"), "url").Length > 0, "Document resource carries media.document.url");
            Check.Equal("en", Api.Str(detail.GetProperty("captions")[0], "language"), "Caption track");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/lessons/" + lessonId, admin.AccessToken, new { resources = new[] { new { kind = "Link", title = "Plain", url = "http://example.test" } } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/lessons/" + lessonId, admin.AccessToken, new { captions = new[] { new { language = "en", label = "English", mediaId = pdf } } }, HttpStatusCode.Conflict, "MediaNotAttachable", ct).ConfigureAwait(false);

            Check.Equal(2, await documents.RunAsync(10, ct).ConfigureAwait(false), "Both documents processed");
            DataTable rows = await f.Raw.ExecuteQueryAsync("SELECT mediaid, status, chunker, chunkcount, summary FROM documentextractions ORDER BY mediaid;", false, ct).ConfigureAwait(false);
            DataRow pdfRow = rows.Rows.Cast<DataRow>().First(r => RowReader.GetString(r, "mediaid") == pdf);
            Check.Equal("Ready", RowReader.GetString(pdfRow, "status"), "PDF extracted");
            Check.Equal("partio", RowReader.GetString(pdfRow, "chunker"), "Remote chunker used");
            Check.Equal("A short summary.", RowReader.GetString(pdfRow, "summary"), "Summary stored");
            Check.True(await LibraryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM documentchunks WHERE mediaid = '" + pdf + "' AND pagenumber = 2;", ct).ConfigureAwait(false) >= 1, "Page numbers kept");
            SearchDocument? search = await f.Services.Library.Provider.SearchDocumentAsync(TargetTypeEnum.Lesson, lessonId, ct).ConfigureAwait(false);
            Check.True(search != null && search.Content.Contains("gripping", StringComparison.Ordinal) && !search.Content.Contains("<b>", StringComparison.Ordinal), "Caption text without tags in the search document");
            Check.True(search!.Content.Contains("Breaking grips", StringComparison.Ordinal), "PDF text in the search document");
            Check.True(!search.Content.Contains("-->", StringComparison.Ordinal), "No timings");

            chunker.Fail = true;
            await f.Services.Database.Library.Documents.EnqueueAsync(pdf, "application/pdf", DateTime.UtcNow, ct).ConfigureAwait(false);
            await LibraryTestKit.SqlAsync(f, "UPDATE documentextractions SET status = 'Failed' WHERE mediaid = '" + pdf + "';", ct).ConfigureAwait(false);
            await f.Services.Database.Library.Documents.EnqueueAsync(pdf, "application/pdf", DateTime.UtcNow, ct).ConfigureAwait(false);
            await documents.RunAsync(10, ct).ConfigureAwait(false);
            DataTable fallback = await f.Raw.ExecuteQueryAsync("SELECT chunker FROM documentextractions WHERE mediaid = '" + pdf + "';", false, ct).ConfigureAwait(false);
            Check.Equal("local", RowReader.GetString(fallback.Rows[0], "chunker"), "Local chunker when Partio fails");

            extractor.Fail = true;
            f.Settings.Library.DocumentMaxAttempts = 2;
            await LibraryTestKit.SqlAsync(f, "UPDATE documentextractions SET status = 'Failed' WHERE mediaid = '" + pdf + "';", ct).ConfigureAwait(false);
            await f.Services.Database.Library.Documents.EnqueueAsync(pdf, "application/pdf", DateTime.UtcNow, ct).ConfigureAwait(false);
            await documents.RunAsync(10, ct).ConfigureAwait(false);
            Check.Equal(1L, await LibraryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM documentextractions WHERE mediaid = '" + pdf + "' AND status = 'Pending' AND attempts = 1;", ct).ConfigureAwait(false), "First failure retries");
            await documents.RunAsync(10, ct).ConfigureAwait(false);
            Check.Equal(1L, await LibraryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM documentextractions WHERE mediaid = '" + pdf + "' AND status = 'Failed';", ct).ConfigureAwait(false), "Fails after the allowed attempts");
        }

        private static async Task RealPdfAsync(CancellationToken ct)
        {
            DocumentAtomPdfExtractor extractor = new DocumentAtomPdfExtractor();
            ExtractedDocument document = await extractor.ExtractAsync(DocumentKit.Pdf("Hello library notes"), "application/pdf", ct).ConfigureAwait(false);
            string text = String.Join(" ", document.Segments.Select(s => s.Text));
            Check.True(text.Contains("Hello library notes", StringComparison.Ordinal), "DocumentAtom text (" + text + ")");
            Check.True(document.Segments.All(s => s.PageNumber == null || s.PageNumber == 1), "Page numbers");
            bool threw = false;
            try
            {
                await extractor.ExtractAsync(Encoding.ASCII.GetBytes("%PDF-1.4 garbage"), "application/pdf", ct).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }
            Check.True(threw, "Unreadable PDFs throw InvalidOperationException");
        }

        private static async Task ParsersAsync(CancellationToken ct)
        {
            string vtt = Encoding.UTF8.GetString(DocumentKit.Vtt("First <i>line</i>", "Second &amp; last"));
            Check.True(WebVttParser.Validate(vtt, out string? _), "Valid WebVTT");
            Check.True(!WebVttParser.Validate("WEBVTTX\n", out string? e1) && e1 != null, "Header must be WEBVTT");
            Check.True(!WebVttParser.Validate("Hello\n", out string? _), "Missing header");
            Check.Equal("First line\nSecond & last", WebVttParser.ExtractText(vtt), "Spoken text only");
            WebVttTextExtractor captions = new WebVttTextExtractor();
            ExtractedDocument doc = await captions.ExtractAsync(Encoding.UTF8.GetBytes(vtt), "text/vtt", ct).ConfigureAwait(false);
            Check.Equal(1, doc.Segments.Count, "One segment");
            LocalTextChunker chunker = new LocalTextChunker { MaxCharacters = 60 };
            string text = "Short paragraph one.\n\nShort paragraph two.\n\n" + String.Join(" ", Enumerable.Repeat("A sentence that keeps going.", 6));
            TextChunkResult chunks = await chunker.ChunkAsync(text, ct).ConfigureAwait(false);
            Check.True(chunks.Chunks.All(c => c.Length <= 60), "Every chunk within the budget");
            Check.Equal("Short paragraph one.\nShort paragraph two.", chunks.Chunks[0], "Short paragraphs are packed");
            Check.True(chunks.Chunks.Count >= 3, "Long paragraphs split on sentences");
            Check.Equal("local", chunks.Chunker, "Local chunker name");
        }

        #endregion
    }
}
