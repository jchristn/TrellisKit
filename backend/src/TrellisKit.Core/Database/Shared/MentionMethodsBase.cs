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
    /// Shared mention methods.
    /// </summary>
    internal abstract class MentionMethodsBase : MethodsBase, IMentionMethods
    {
        protected MentionMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<Dictionary<string, string>> ResolveHandlesAsync(IEnumerable<string> handles, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(handles);
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
            List<string> list = handles.Where(h => !String.IsNullOrWhiteSpace(h)).Select(h => h.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToList();
            if (list.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT id, handle FROM users WHERE " + Scope() + " AND lower(handle) IN " + D.InList(list) + " AND isdeleted = " + D.Bool(false) +
                " AND isbanned = " + D.Bool(false) + ";", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result[RowReader.GetString(row, "handle").ToLowerInvariant()] = RowReader.GetString(row, "id");
            return result;
        }

        public async Task ReplaceAsync(TargetTypeEnum sourceType, string sourceId, IReadOnlyList<Mention> mentions, CancellationToken token = default)
        {
            RequireText(sourceId, nameof(sourceId));
            ArgumentNullException.ThrowIfNull(mentions);
            string where = Scope() + " AND sourcetype = " + D.Enum(sourceType) + " AND sourceid = " + D.Str(sourceId);
            List<string> keep = mentions.Select(m => m.MentionedUserId).Distinct(StringComparer.Ordinal).ToList();
            List<string> statements = new List<string>
            {
                "DELETE FROM mentions WHERE " + where + (keep.Count == 0 ? String.Empty : " AND mentioneduserid NOT IN " + D.InList(keep)) + ";"
            };
            foreach (Mention m in mentions)
            {
                statements.Add("INSERT INTO mentions (propertyid, id, sourcetype, sourceid, mentioneduserid, handle, authoruserid, notifiedutc, createdutc) VALUES (" + Pid + ", " + D.Str(m.Id) + ", " +
                    D.Enum(sourceType) + ", " + D.Str(sourceId) + ", " + D.Str(m.MentionedUserId) + ", " + D.Str(m.Handle) + ", " + D.Str(m.AuthorUserId) + ", " +
                    D.Ts(m.NotifiedUtc) + ", " + D.Ts(m.CreatedUtc) + ") ON CONFLICT (propertyid, sourcetype, sourceid, mentioneduserid) DO UPDATE SET handle = excluded.handle;");
            }
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        public async Task<List<Mention>> ClaimUnnotifiedAsync(TargetTypeEnum sourceType, string sourceId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(sourceId, nameof(sourceId));
            DataTable table = await QueryAsync("UPDATE mentions SET notifiedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND sourcetype = " + D.Enum(sourceType) + " AND sourceid = " + D.Str(sourceId) +
                " AND notifiedutc IS NULL RETURNING *;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<Dictionary<string, List<Mention>>> ListBySourcesAsync(TargetTypeEnum sourceType, IEnumerable<string> sourceIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(sourceIds);
            Dictionary<string, List<Mention>> result = new Dictionary<string, List<Mention>>(StringComparer.Ordinal);
            List<string> ids = sourceIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT * FROM mentions WHERE " + Scope() + " AND sourcetype = " + D.Enum(sourceType) + " AND sourceid IN " + D.InList(ids) + " ORDER BY createdutc, id;", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows)
            {
                Mention m = Map(row);
                if (!result.TryGetValue(m.SourceId, out List<Mention>? list))
                {
                    list = new List<Mention>();
                    result[m.SourceId] = list;
                }
                list.Add(m);
            }
            return result;
        }

        public async Task<int> DeleteForMentionedUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("DELETE FROM mentions WHERE " + Scope() + " AND mentioneduserid = " + D.Str(userId) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public async Task<int> DeleteBySourceAsync(TargetTypeEnum sourceType, string sourceId, CancellationToken token = default)
        {
            RequireText(sourceId, nameof(sourceId));
            DataTable table = await QueryAsync("DELETE FROM mentions WHERE " + Scope() + " AND sourcetype = " + D.Enum(sourceType) + " AND sourceid = " + D.Str(sourceId) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        private static Mention Map(DataRow row)
        {
            return new Mention
            {
                Id = RowReader.GetString(row, "id"),
                SourceType = RowReader.GetEnum(row, "sourcetype", TargetTypeEnum.FeedPost),
                SourceId = RowReader.GetString(row, "sourceid"),
                MentionedUserId = RowReader.GetString(row, "mentioneduserid"),
                Handle = RowReader.GetString(row, "handle"),
                AuthorUserId = RowReader.GetString(row, "authoruserid"),
                NotifiedUtc = RowReader.GetNullableDateTime(row, "notifiedutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }
    }
}
