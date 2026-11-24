namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    internal abstract class DocumentTextMethodsBase : MethodsBase, IDocumentTextMethods
    {
        protected DocumentTextMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<bool> EnqueueAsync(string mediaId, string contentType, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(mediaId, nameof(mediaId));
            string now = D.Ts(nowUtc);
            DataTable table = await QueryAsync("INSERT INTO documentextractions (propertyid, mediaid, status, contenttype, attempts, createdutc, updatedutc) VALUES (" + Pid + ", " + D.Str(mediaId) + ", " +
                D.Enum(DocumentExtractionStatusEnum.Pending) + ", " + D.Str(contentType ?? String.Empty) + ", 0, " + now + ", " + now + ") ON CONFLICT (propertyid, mediaid) DO UPDATE SET status = " +
                D.Enum(DocumentExtractionStatusEnum.Pending) + ", attempts = 0, error = NULL, updatedutc = " + now + " WHERE documentextractions.status IN " +
                D.InList(new[] { DocumentExtractionStatusEnum.Failed.ToString(), DocumentExtractionStatusEnum.Skipped.ToString() }) + " RETURNING mediaid;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<DocumentExtraction?> ReadAsync(string mediaId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(mediaId)) return null;
            DataTable table = await QueryAsync("SELECT * FROM documentextractions WHERE " + Scope() + " AND mediaid = " + D.Str(mediaId) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<List<DocumentExtraction>> PendingAsync(int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT * FROM documentextractions WHERE " + Scope() + " AND status = " + D.Enum(DocumentExtractionStatusEnum.Pending) + " ORDER BY updatedutc, mediaid LIMIT " +
                D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task StoreAsync(DocumentExtraction extraction, List<DocumentChunk> chunks, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(extraction);
            ArgumentNullException.ThrowIfNull(chunks);
            string media = D.Str(extraction.MediaId);
            List<string> statements = new List<string>
            {
                "DELETE FROM documentchunks WHERE " + Scope() + " AND mediaid = " + media + ";",
                "UPDATE documentextractions SET status = " + D.Enum(extraction.Status) + ", extractor = " + D.Str(extraction.Extractor) + ", chunker = " + D.Str(extraction.Chunker) + ", chunkcount = " +
                    D.Num(chunks.Count) + ", charactercount = " + D.Num(extraction.CharacterCount) + ", summary = " + D.Str(extraction.Summary) + ", error = " + D.Str(extraction.Error) + ", attempts = " +
                    D.Num(extraction.Attempts) + ", extractedutc = " + D.Ts(extraction.ExtractedUtc) + ", updatedutc = " + D.Ts(extraction.UpdatedUtc) + " WHERE " + Scope() + " AND mediaid = " + media + ";"
            };
            foreach (IEnumerable<DocumentChunk> batch in chunks.Chunk(200))
            {
                statements.Add("INSERT INTO documentchunks (propertyid, id, mediaid, position, pagenumber, text, createdutc) VALUES " + String.Join(", ", batch.Select(c => "(" + Pid + ", " + D.Str(c.Id) + ", " +
                    media + ", " + D.Num(c.Position) + ", " + D.Num(c.PageNumber) + ", " + D.Str(c.Text) + ", " + D.Ts(c.CreatedUtc) + ")")) + ";");
            }
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        public async Task<List<DocumentChunk>> ChunksAsync(IEnumerable<string> mediaIds, int limit, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(mediaIds);
            List<string> ids = mediaIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return new List<DocumentChunk>();
            DataTable table = await QueryAsync("SELECT id, mediaid, position, pagenumber, text, createdutc FROM documentchunks WHERE " + Scope() + " AND mediaid IN " + D.InList(ids) +
                " ORDER BY mediaid, position LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => new DocumentChunk
            {
                Id = RowReader.GetString(r, "id"),
                MediaId = RowReader.GetString(r, "mediaid"),
                Position = RowReader.GetInt(r, "position"),
                PageNumber = RowReader.GetNullableInt(r, "pagenumber"),
                Text = RowReader.GetString(r, "text"),
                CreatedUtc = RowReader.GetDateTime(r, "createdutc")
            }).ToList();
        }

        public async Task DeleteAsync(string mediaId, CancellationToken token = default)
        {
            RequireText(mediaId, nameof(mediaId));
            await TransactionAsync(new List<string>
            {
                "DELETE FROM documentchunks WHERE " + Scope() + " AND mediaid = " + D.Str(mediaId) + ";",
                "DELETE FROM documentextractions WHERE " + Scope() + " AND mediaid = " + D.Str(mediaId) + ";"
            }, token).ConfigureAwait(false);
        }

        private static DocumentExtraction Map(DataRow row)
        {
            return new DocumentExtraction
            {
                MediaId = RowReader.GetString(row, "mediaid"),
                Status = RowReader.GetEnum(row, "status", DocumentExtractionStatusEnum.Pending),
                ContentType = RowReader.GetString(row, "contenttype"),
                Extractor = RowReader.GetNullableString(row, "extractor"),
                Chunker = RowReader.GetNullableString(row, "chunker"),
                ChunkCount = RowReader.GetInt(row, "chunkcount"),
                CharacterCount = RowReader.GetInt(row, "charactercount"),
                Summary = RowReader.GetNullableString(row, "summary"),
                Error = RowReader.GetNullableString(row, "error"),
                Attempts = RowReader.GetInt(row, "attempts"),
                ExtractedUtc = RowReader.GetNullableDateTime(row, "extractedutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }
    }
}
