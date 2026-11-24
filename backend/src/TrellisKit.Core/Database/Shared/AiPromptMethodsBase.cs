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
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    internal abstract class AiPromptMethodsBase : MethodsBase, IAiPromptMethods
    {
        protected AiPromptMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<Dictionary<string, AiPromptTemplate>> ReadAllAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT * FROM aiprompttemplates WHERE " + Scope() + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToDictionary(t => t.Key, StringComparer.Ordinal);
        }

        public async Task<AiPromptTemplate> SaveAsync(string key, string content, string versionId, string? userId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(key, nameof(key));
            RequireText(content, nameof(content));
            RequireText(versionId, nameof(versionId));
            string next = "(SELECT COALESCE(MAX(v.version), 0) + 1 FROM aiprompttemplateversions v WHERE " + Scope("v") + " AND v.key = " + D.Str(key) + ")";
            List<string> statements = new List<string>
            {
                "INSERT INTO aiprompttemplateversions (propertyid, id, key, version, content, isreset, createdbyuserid, createdutc, publishedutc) VALUES (" + Pid + ", " + D.Str(versionId) + ", " + D.Str(key) + ", " +
                    next + ", " + D.Str(content) + ", " + D.Bool(false) + ", " + D.Str(userId) + ", " + D.Ts(nowUtc) + ", " + D.Ts(nowUtc) + ");",
                "INSERT INTO aiprompttemplates (propertyid, key, content, version, updatedbyuserid, updatedutc) SELECT " + Pid + ", " + D.Str(key) + ", " + D.Str(content) + ", v.version, " +
                    D.Str(userId) + ", " + D.Ts(nowUtc) + " FROM aiprompttemplateversions v WHERE " + Scope("v") + " AND v.id = " + D.Str(versionId) +
                    " ON CONFLICT (propertyid, key) DO UPDATE SET content = EXCLUDED.content, version = EXCLUDED.version, updatedbyuserid = EXCLUDED.updatedbyuserid, updatedutc = EXCLUDED.updatedutc;",
                "SELECT * FROM aiprompttemplates WHERE " + Scope() + " AND key = " + D.Str(key) + ";"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return Map(table.Rows[0]);
        }

        public async Task ResetAsync(string key, string defaultContent, string versionId, string? userId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(key, nameof(key));
            RequireText(versionId, nameof(versionId));
            string next = "(SELECT COALESCE(MAX(v.version), 0) + 1 FROM aiprompttemplateversions v WHERE " + Scope("v") + " AND v.key = " + D.Str(key) + ")";
            List<string> statements = new List<string>
            {
                "INSERT INTO aiprompttemplateversions (propertyid, id, key, version, content, isreset, createdbyuserid, createdutc, publishedutc) VALUES (" + Pid + ", " + D.Str(versionId) + ", " + D.Str(key) + ", " +
                    next + ", " + D.Str(defaultContent ?? String.Empty) + ", " + D.Bool(true) + ", " + D.Str(userId) + ", " + D.Ts(nowUtc) + ", " + D.Ts(nowUtc) + ");",
                "DELETE FROM aiprompttemplates WHERE " + Scope() + " AND key = " + D.Str(key) + ";"
            };
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        public async Task<AiPromptTemplateVersion> SaveDraftAsync(string key, string content, string versionId, string? userId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(key, nameof(key));
            RequireText(content, nameof(content));
            RequireText(versionId, nameof(versionId));
            string next = "(SELECT COALESCE(MAX(v.version), 0) + 1 FROM aiprompttemplateversions v WHERE " + Scope("v") + " AND v.key = " + D.Str(key) + ")";
            List<string> statements = new List<string>
            {
                "INSERT INTO aiprompttemplateversions (propertyid, id, key, version, content, isreset, createdbyuserid, createdutc, publishedutc) VALUES (" + Pid + ", " + D.Str(versionId) + ", " + D.Str(key) + ", " +
                    next + ", " + D.Str(content) + ", " + D.Bool(false) + ", " + D.Str(userId) + ", " + D.Ts(nowUtc) + ", NULL);",
                "SELECT * FROM aiprompttemplateversions WHERE " + Scope() + " AND id = " + D.Str(versionId) + ";"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return MapVersion(table.Rows[0]);
        }

        public async Task<AiPromptTemplate?> PublishAsync(string key, string versionId, string? userId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(key, nameof(key));
            RequireText(versionId, nameof(versionId));
            string draft = Scope("v") + " AND v.id = " + D.Str(versionId) + " AND v.key = " + D.Str(key) + " AND v.publishedutc IS NULL AND NOT v.isreset";
            List<string> statements = new List<string>
            {
                "INSERT INTO aiprompttemplates (propertyid, key, content, version, updatedbyuserid, updatedutc) SELECT " + Pid + ", v.key, v.content, v.version, " + D.Str(userId) + ", " + D.Ts(nowUtc) +
                    " FROM aiprompttemplateversions v WHERE " + draft +
                    " ON CONFLICT (propertyid, key) DO UPDATE SET content = EXCLUDED.content, version = EXCLUDED.version, updatedbyuserid = EXCLUDED.updatedbyuserid, updatedutc = EXCLUDED.updatedutc;",
                "UPDATE aiprompttemplateversions v SET publishedutc = " + D.Ts(nowUtc) + " WHERE " + draft + " RETURNING v.id;"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            DataTable current = await QueryAsync("SELECT * FROM aiprompttemplates WHERE " + Scope() + " AND key = " + D.Str(key) + ";", token).ConfigureAwait(false);
            return current.Rows.Count == 0 ? null : Map(current.Rows[0]);
        }

        public async Task<Dictionary<string, AiPromptTemplateVersion>> ReadNewestVersionsAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT DISTINCT ON (key) * FROM aiprompttemplateversions WHERE " + Scope() + " ORDER BY key, version DESC, createdutc DESC;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapVersion).ToDictionary(v => v.Key, StringComparer.Ordinal);
        }

        public async Task<AiPromptTemplateVersion?> ReadVersionAsync(string versionId, CancellationToken token = default)
        {
            RequireText(versionId, nameof(versionId));
            DataTable table = await QueryAsync("SELECT * FROM aiprompttemplateversions WHERE " + Scope() + " AND id = " + D.Str(versionId) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MapVersion(table.Rows[0]);
        }

        public Task<EnumerationResult<AiPromptTemplateVersion>> EnumerateVersionsAsync(EnumerationRequest request, string key, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            RequireText(key, nameof(key));
            return EnumerateAsync(request, "v.*", "aiprompttemplateversions v", new List<string> { "v.key = " + D.Str(key) }, MapVersion, token);
        }

        private static AiPromptTemplate Map(DataRow row)
        {
            return new AiPromptTemplate
            {
                Key = RowReader.GetString(row, "key"),
                Content = RowReader.GetString(row, "content"),
                Version = RowReader.GetInt(row, "version"),
                UpdatedByUserId = RowReader.GetNullableString(row, "updatedbyuserid"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        private static AiPromptTemplateVersion MapVersion(DataRow row)
        {
            return new AiPromptTemplateVersion
            {
                Id = RowReader.GetString(row, "id"),
                Key = RowReader.GetString(row, "key"),
                Version = RowReader.GetInt(row, "version"),
                Content = RowReader.GetString(row, "content"),
                IsReset = RowReader.GetBool(row, "isreset"),
                CreatedByUserId = RowReader.GetNullableString(row, "createdbyuserid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                PublishedUtc = RowReader.GetNullableDateTime(row, "publishedutc")
            };
        }
    }
}
