namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Shared property document methods (tables propertydocumentversions and propertydocumentacceptances).
    /// </summary>
    internal abstract class PropertyDocumentMethodsBase : MethodsBase, IPropertyDocumentMethods
    {
        private const string _VersionColumns = "v.id, v.documentkey, v.sequence, v.version, v.title, v.changesummary, v.requiresreacceptance, v.source, v.seedhash, v.createdbyuserid, v.createdutc";

        protected PropertyDocumentMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<List<PropertyDocumentVersion>> ListCurrentAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT v.* FROM propertydocumentversions v WHERE " + Scope("v") +
                " AND v.sequence = (SELECT MAX(m.sequence) FROM propertydocumentversions m WHERE " + Scope("m") + " AND m.documentkey = v.documentkey) ORDER BY v.documentkey LIMIT 100;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => Map(r, true)).ToList();
        }

        public async Task<Dictionary<string, int>> RequiredSequencesAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT documentkey, MAX(sequence) AS seq FROM propertydocumentversions WHERE " + Scope() + " AND requiresreacceptance = " + D.Bool(true) +
                " GROUP BY documentkey ORDER BY documentkey LIMIT 100;", token).ConfigureAwait(false);
            Dictionary<string, int> result = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (DataRow row in table.Rows) result[RowReader.GetString(row, "documentkey")] = RowReader.GetInt(row, "seq");
            return result;
        }

        public async Task<PropertyDocumentVersion?> ReadCurrentAsync(string documentKey, CancellationToken token = default)
        {
            RequireText(documentKey, nameof(documentKey));
            DataTable table = await QueryAsync("SELECT * FROM propertydocumentversions WHERE " + Scope() + " AND documentkey = " + D.Str(documentKey) + " ORDER BY sequence DESC LIMIT 1;", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0], true);
        }

        public async Task<PropertyDocumentVersion?> ReadVersionAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) return null;
            DataTable table = await QueryAsync("SELECT * FROM propertydocumentversions WHERE " + Scope() + " AND id = " + D.Str(id) + " LIMIT 1;", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0], true);
        }

        public async Task CreateVersionAsync(PropertyDocumentVersion version, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(version);
            string sql = "INSERT INTO propertydocumentversions (propertyid, id, documentkey, sequence, version, title, content, changesummary, requiresreacceptance, source, seedhash, createdbyuserid, createdutc) VALUES (" +
                Pid + ", " + D.Str(version.Id) + ", " + D.Str(version.DocumentKey) + ", " + D.Num(version.Sequence) + ", " + D.Str(version.Version) + ", " + D.Str(version.Title) + ", " +
                D.Str(version.Content) + ", " + D.Str(version.ChangeSummary) + ", " + D.Bool(version.RequiresReacceptance) + ", " + D.Enum(version.Source) + ", " + D.Str(version.SeedHash) + ", " +
                D.Str(version.CreatedByUserId) + ", " + D.Ts(version.CreatedUtc) + ");";
            await UniqueAsync(sql, "Document version " + version.DocumentKey + " " + version.Version, token).ConfigureAwait(false);
        }

        public Task<long> CountVersionsAsync(string documentKey, CancellationToken token = default)
        {
            RequireText(documentKey, nameof(documentKey));
            return CountAsync("SELECT COUNT(*) AS cnt FROM propertydocumentversions WHERE " + Scope() + " AND documentkey = " + D.Str(documentKey) + ";", token);
        }

        public Task<EnumerationResult<PropertyDocumentVersion>> EnumerateVersionsAsync(EnumerationRequest request, string documentKey, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            RequireText(documentKey, nameof(documentKey));
            string count = "(SELECT COUNT(DISTINCT c.userid) FROM propertydocumentacceptances c WHERE " + Scope("c") + " AND c.versionid = v.id) AS acceptancecount";
            string length = "LENGTH(v.content) AS contentlength";
            List<string> conditions = new List<string> { "v.documentkey = " + D.Str(documentKey) };
            return EnumerateAsync(request, _VersionColumns + ", " + count + ", " + length, "propertydocumentversions v", conditions, r => Map(r, false), token);
        }

        public async Task CreateAcceptancesAsync(List<DocumentAcceptance> acceptances, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(acceptances);
            if (acceptances.Count == 0) return;
            if (acceptances.Count > 100) throw new ArgumentOutOfRangeException(nameof(acceptances), "At most 100 acceptances per call.");
            List<string> statements = acceptances.Select(a =>
                "INSERT INTO propertydocumentacceptances (propertyid, id, userid, documentkey, versionid, version, sequence, title, method, client, acceptedutc, createdutc) VALUES (" +
                Pid + ", " + D.Str(a.Id) + ", " + D.Str(a.UserId) + ", " + D.Str(a.DocumentKey) + ", " + D.Str(a.VersionId) + ", " + D.Str(a.Version) + ", " + D.Num(a.Sequence) + ", " +
                D.Str(a.Title) + ", " + D.Enum(a.Method) + ", " + D.Enum(a.Client) + ", " + D.Ts(a.AcceptedUtc) + ", " + D.Ts(a.CreatedUtc) + ") ON CONFLICT DO NOTHING;").ToList();
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        public async Task<Dictionary<string, int>> AcceptedSequencesAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT documentkey, MAX(sequence) AS seq FROM propertydocumentacceptances WHERE " + Scope() + " AND userid = " + D.Str(userId) +
                " GROUP BY documentkey ORDER BY documentkey LIMIT 100;", token).ConfigureAwait(false);
            Dictionary<string, int> result = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (DataRow row in table.Rows) result[RowReader.GetString(row, "documentkey")] = RowReader.GetInt(row, "seq");
            return result;
        }

        public Task<long> CountAcceptedMembersAsync(string documentKey, int? sequence, CancellationToken token = default)
        {
            RequireText(documentKey, nameof(documentKey));
            return CountAsync("SELECT COUNT(DISTINCT userid) AS cnt FROM propertydocumentacceptances WHERE " + Scope() + " AND documentkey = " + D.Str(documentKey) +
                (sequence == null ? String.Empty : " AND sequence = " + D.Num(sequence.Value)) + ";", token);
        }

        public Task<EnumerationResult<DocumentAcceptance>> EnumerateAcceptancesAsync(EnumerationRequest request, DocumentAcceptanceFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string>();
            if (!String.IsNullOrEmpty(filter.DocumentKey)) conditions.Add("a.documentkey = " + D.Str(filter.DocumentKey));
            if (!String.IsNullOrEmpty(filter.UserId)) conditions.Add("a.userid = " + D.Str(filter.UserId));
            if (!String.IsNullOrEmpty(filter.Version)) conditions.Add("lower(a.version) = " + D.Str(filter.Version.ToLowerInvariant()));
            if (filter.Method != null) conditions.Add("a.method = " + D.Enum(filter.Method.Value));
            if (filter.Client != null) conditions.Add("a.client = " + D.Enum(filter.Client.Value));
            string from = "propertydocumentacceptances a LEFT JOIN users u ON u.propertyid = a.propertyid AND u.id = a.userid";
            return EnumerateAsync(request, "a.*", from, conditions, MapAcceptance, token);
        }

        public async Task<List<DocumentAcceptance>> ListByUserAsync(string userId, string? afterId, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            int size = Math.Clamp(limit, 1, 1000);
            DataTable table = await QueryAsync("SELECT * FROM propertydocumentacceptances WHERE " + Scope() + " AND userid = " + D.Str(userId) +
                (String.IsNullOrEmpty(afterId) ? String.Empty : " AND id > " + D.Str(afterId)) + " ORDER BY id LIMIT " + D.Num(size) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapAcceptance).ToList();
        }

        private static PropertyDocumentVersion Map(DataRow row, bool withContent)
        {
            PropertyDocumentVersion version = new PropertyDocumentVersion
            {
                Id = RowReader.GetString(row, "id"),
                DocumentKey = RowReader.GetString(row, "documentkey"),
                Sequence = RowReader.GetInt(row, "sequence"),
                Version = RowReader.GetString(row, "version"),
                Title = RowReader.GetString(row, "title"),
                ChangeSummary = RowReader.GetNullableString(row, "changesummary"),
                RequiresReacceptance = RowReader.GetBool(row, "requiresreacceptance"),
                Source = RowReader.GetEnum(row, "source", DocumentVersionSourceEnum.Seed),
                SeedHash = RowReader.GetNullableString(row, "seedhash"),
                CreatedByUserId = RowReader.GetNullableString(row, "createdbyuserid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
            if (withContent)
            {
                version.Content = RowReader.GetString(row, "content");
                version.ContentLength = version.Content.Length;
            }
            if (row.Table.Columns.Contains("acceptancecount")) version.AcceptanceCount = RowReader.GetLong(row, "acceptancecount");
            if (row.Table.Columns.Contains("contentlength")) version.ContentLength = RowReader.GetInt(row, "contentlength");
            return version;
        }

        private static DocumentAcceptance MapAcceptance(DataRow row)
        {
            return new DocumentAcceptance
            {
                Id = RowReader.GetString(row, "id"),
                UserId = RowReader.GetString(row, "userid"),
                DocumentKey = RowReader.GetString(row, "documentkey"),
                VersionId = RowReader.GetString(row, "versionid"),
                Version = RowReader.GetString(row, "version"),
                Sequence = RowReader.GetInt(row, "sequence"),
                Title = RowReader.GetString(row, "title"),
                Method = RowReader.GetEnum(row, "method", DocumentAcceptanceMethodEnum.Signup),
                Client = RowReader.GetEnum(row, "client", DocumentClientEnum.Other),
                AcceptedUtc = RowReader.GetDateTime(row, "acceptedutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }
    }
}
