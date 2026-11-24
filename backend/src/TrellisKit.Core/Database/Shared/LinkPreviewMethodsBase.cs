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

    /// <summary>
    /// Shared link preview cache methods.
    /// </summary>
    internal abstract class LinkPreviewMethodsBase : MethodsBase, ILinkPreviewMethods
    {
        private const string _Columns = "id, urlhash, url, canonicalurl, sitename, title, description, imagekey, imagewidth, imageheight, status, failurereason, fetchedutc, expiresutc";
        private const int _MaxRead = 500;

        protected LinkPreviewMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<LinkPreview?> ReadByUrlHashAsync(string urlHash, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(urlHash)) return null;
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM linkpreviews WHERE " + Scope() + " AND urlhash = " + D.Str(urlHash) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<LinkPreview?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM linkpreviews WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<Dictionary<string, LinkPreview>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            List<string> list = ids.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).Take(_MaxRead).ToList();
            Dictionary<string, LinkPreview> result = new Dictionary<string, LinkPreview>(StringComparer.Ordinal);
            if (list.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM linkpreviews WHERE " + Scope() + " AND id IN " + D.InList(list) + ";", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows)
            {
                LinkPreview preview = Map(row);
                result[preview.Id] = preview;
            }
            return result;
        }

        public async Task<LinkPreview> ReplaceAsync(LinkPreview preview, string? previousId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(preview);
            RequireText(preview.Id, nameof(preview.Id));
            RequireText(preview.UrlHash, nameof(preview.UrlHash));
            List<string> statements = new List<string>();
            if (!String.IsNullOrEmpty(previousId))
            {
                statements.Add("DELETE FROM linkpreviews WHERE " + Scope() + " AND id = " + D.Str(previousId) + " AND status = " + D.Enum(LinkPreviewStatusEnum.Failed) + ";");
                statements.Add("UPDATE linkpreviews SET urlhash = " + D.Str("old:") + " || id WHERE " + Scope() + " AND id = " + D.Str(previousId) + " AND urlhash = " + D.Str(preview.UrlHash) + ";");
            }
            statements.Add("INSERT INTO linkpreviews (propertyid, " + _Columns + ") VALUES (" + Pid + ", " + D.Str(preview.Id) + ", " + D.Str(preview.UrlHash) + ", " + D.Str(preview.Url) + ", " +
                D.Str(preview.CanonicalUrl) + ", " + D.Str(preview.SiteName) + ", " + D.Str(preview.Title) + ", " + D.Str(preview.Description) + ", " +
                D.Str(preview.ImageKey) + ", " + D.Num(preview.ImageWidth) + ", " + D.Num(preview.ImageHeight) + ", " + D.Enum(preview.Status) + ", " +
                D.Str(preview.FailureReason) + ", " + D.Ts(preview.FetchedUtc) + ", " + D.Ts(preview.ExpiresUtc) + ") ON CONFLICT (propertyid, urlhash) DO NOTHING RETURNING id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            if (table.Rows.Count > 0) return preview;
            LinkPreview? winner = await ReadByUrlHashAsync(preview.UrlHash, token).ConfigureAwait(false);
            return winner ?? preview;
        }

        public async Task<int> PruneAsync(DateTime nowUtc, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("DELETE FROM linkpreviews WHERE " + Scope() + " AND expiresutc < " + D.Ts(nowUtc) + " AND (status = " + D.Enum(LinkPreviewStatusEnum.Failed) +
                " OR (NOT EXISTS (SELECT 1 FROM feedposts f WHERE " + Scope("f") + " AND f.linkpreviewid = linkpreviews.id) AND NOT EXISTS (SELECT 1 FROM comments c WHERE " + Scope("c") + " AND c.linkpreviewid = linkpreviews.id)))" +
                " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        private static LinkPreview Map(DataRow row)
        {
            return new LinkPreview
            {
                Id = RowReader.GetString(row, "id"),
                UrlHash = RowReader.GetString(row, "urlhash"),
                Url = RowReader.GetString(row, "url"),
                CanonicalUrl = RowReader.GetNullableString(row, "canonicalurl"),
                SiteName = RowReader.GetNullableString(row, "sitename"),
                Title = RowReader.GetNullableString(row, "title"),
                Description = RowReader.GetNullableString(row, "description"),
                ImageKey = RowReader.GetNullableString(row, "imagekey"),
                ImageWidth = RowReader.GetNullableInt(row, "imagewidth"),
                ImageHeight = RowReader.GetNullableInt(row, "imageheight"),
                Status = RowReader.GetEnum(row, "status", LinkPreviewStatusEnum.Failed),
                FailureReason = RowReader.GetNullableString(row, "failurereason"),
                FetchedUtc = RowReader.GetDateTime(row, "fetchedutc"),
                ExpiresUtc = RowReader.GetDateTime(row, "expiresutc")
            };
        }
    }
}
