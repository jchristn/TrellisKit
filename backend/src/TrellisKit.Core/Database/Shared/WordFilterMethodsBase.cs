namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Shared word filter methods.
    /// </summary>
    internal abstract class WordFilterMethodsBase : MethodsBase, IWordFilterMethods
    {
        private const string _Columns = "id, term, normalizedterm, matchtype, action, note, createdbyuserid, createdutc, updatedutc";

        protected WordFilterMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<WordFilterTerm> CreateAsync(WordFilterTerm term, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(term);
            RequireText(term.Id, nameof(term.Id));
            RequireText(term.NormalizedTerm, nameof(term.NormalizedTerm));
            await UniqueAsync("INSERT INTO wordfilterterms (propertyid, " + _Columns + ") VALUES " + Values(term) + ";", "Word filter term", token).ConfigureAwait(false);
            return term;
        }

        public async Task<WordFilterTerm?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM wordfilterterms WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<WordFilterTerm> UpdateAsync(WordFilterTerm term, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(term);
            RequireText(term.Id, nameof(term.Id));
            RequireText(term.NormalizedTerm, nameof(term.NormalizedTerm));
            await UniqueAsync("UPDATE wordfilterterms SET term = " + D.Str(term.Term) + ", normalizedterm = " + D.Str(term.NormalizedTerm) +
                ", matchtype = " + D.Enum(term.MatchType) + ", action = " + D.Enum(term.Action) + ", note = " + D.Str(term.Note) +
                ", updatedutc = " + D.Ts(term.UpdatedUtc) + " WHERE " + Scope() + " AND id = " + D.Str(term.Id) + ";", "Word filter term", token).ConfigureAwait(false);
            return term;
        }

        public async Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return false;
            List<string> statements = Driver.Metadata.DeleteStatements(TargetTypeEnum.WordFilterTerm, id);
            statements.Add("DELETE FROM wordfilterterms WHERE " + Scope() + " AND id = " + D.Str(id) + " RETURNING id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<int> ImportAsync(List<WordFilterTerm> terms, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(terms);
            List<WordFilterTerm> distinct = terms.Where(t => t != null && !String.IsNullOrEmpty(t.NormalizedTerm))
                .GroupBy(t => t.NormalizedTerm, StringComparer.Ordinal).Select(g => g.First()).ToList();
            if (distinct.Count == 0) return 0;
            DataTable table = await QueryAsync("INSERT INTO wordfilterterms (propertyid, " + _Columns + ") VALUES " + String.Join(", ", distinct.Select(Values)) +
                " ON CONFLICT (propertyid, normalizedterm) DO NOTHING RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public async Task<List<WordFilterTerm>> EnumerateAllAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM wordfilterterms WHERE " + Scope() + " ORDER BY normalizedterm;", token).ConfigureAwait(false);
            List<WordFilterTerm> result = new List<WordFilterTerm>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        public Task<EnumerationResult<WordFilterTermView>> EnumerateAsync(EnumerationRequest request, WordFilterFilter filter, DateTime hitsSinceUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string>();
            if (filter.Action != null) conditions.Add("t.action = " + D.Enum(filter.Action.Value));
            if (filter.MatchType != null) conditions.Add("t.matchtype = " + D.Enum(filter.MatchType.Value));
            string hits = "(SELECT COUNT(*) FROM wordfilterhits h WHERE " + Scope("h") + " AND h.termid = t.id AND h.createdutc >= " + D.Ts(hitsSinceUtc) + ")";
            return EnumerateAsync(request, "t.id, t.term, t.matchtype, t.action, t.note, t.createdbyuserid, t.createdutc, t.updatedutc, u.displayname AS createdbyname, u.isdeleted AS createdbydeleted, " +
                hits + " AS hitcount", "wordfilterterms t LEFT JOIN users u ON u.id = t.createdbyuserid AND " + Scope("u"), conditions, MapView, token);
        }

        public async Task CreateHitsAsync(List<WordFilterHit> hits, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(hits);
            List<WordFilterHit> valid = hits.Where(h => h != null && !String.IsNullOrEmpty(h.Id) && !String.IsNullOrEmpty(h.TermId)).ToList();
            if (valid.Count == 0) return;
            StringBuilder sb = new StringBuilder("INSERT INTO wordfilterhits (propertyid, id, termid, targettype, targetid, field, action, createdutc) VALUES ");
            sb.Append(String.Join(", ", valid.Select(h => "(" + Pid + ", " + D.Str(h.Id) + ", " + D.Str(h.TermId) + ", " + D.Enum(h.TargetType) + ", " + D.Str(h.TargetId) + ", " +
                D.Str(h.Field) + ", " + D.Enum(h.Action) + ", " + D.Ts(h.CreatedUtc) + ")")));
            sb.Append(" ON CONFLICT (id) DO NOTHING;");
            await QueryAsync(sb.ToString(), token).ConfigureAwait(false);
        }

        public Task<long> CountHitsAsync(string termId, DateTime sinceUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(termId)) return Task.FromResult(0L);
            return CountAsync("SELECT COUNT(*) AS cnt FROM wordfilterhits WHERE " + Scope() + " AND termid = " + D.Str(termId) + " AND createdutc >= " + D.Ts(sinceUtc) + ";", token);
        }

        public async Task<int> PruneHitsAsync(DateTime cutoffUtc, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("DELETE FROM wordfilterhits WHERE " + Scope() + " AND createdutc < " + D.Ts(cutoffUtc) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public Task<bool> CreateSystemReportAsync(string reportId, TargetTypeEnum targetType, string targetId, string? targetAuthorUserId, string? details, DateTime nowUtc, DateTime dueUtc, CancellationToken token = default)
        {
            return CreateSystemReportAsync(reportId, targetType, targetId, targetAuthorUserId, details, "Feed", "*", nowUtc, dueUtc, token);
        }

        public async Task<bool> CreateSystemReportAsync(string reportId, TargetTypeEnum targetType, string targetId, string? targetAuthorUserId, string? details, string scopeType, string scopeId, DateTime nowUtc, DateTime dueUtc, CancellationToken token = default)
        {
            RequireText(reportId, nameof(reportId));
            RequireText(scopeType, nameof(scopeType));
            RequireText(scopeId, nameof(scopeId));
            RequireText(targetId, nameof(targetId));
            string? text = details != null && details.Length > 1000 ? details.Substring(0, 1000) : details;
            DataTable table = await QueryAsync("INSERT INTO reports (propertyid, id, reporteruserid, targettype, targetid, targetauthoruserid, scopetype, scopeid, reason, details, status, dueutc, createdutc) " +
                "SELECT " + Pid + ", " + D.Str(reportId) + ", NULL, " + D.Enum(targetType) + ", " + D.Str(targetId) + ", " + D.Str(targetAuthorUserId) + ", " + D.Str(scopeType) + ", " + D.Str(scopeId) + ", " +
                D.Enum(ReportReasonEnum.Filter) + ", " + D.Str(text) + ", " + D.Enum(ReportStatusEnum.Open) + ", " + D.Ts(dueUtc) + ", " + D.Ts(nowUtc) +
                " WHERE NOT EXISTS (SELECT 1 FROM reports r WHERE " + Scope("r") + " AND r.targettype = " + D.Enum(targetType) + " AND r.targetid = " + D.Str(targetId) +
                " AND r.reporteruserid IS NULL AND r.status = " + D.Enum(ReportStatusEnum.Open) + ") ON CONFLICT DO NOTHING RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        private string Values(WordFilterTerm t)
        {
            return "(" + Pid + ", " + D.Str(t.Id) + ", " + D.Str(t.Term) + ", " + D.Str(t.NormalizedTerm) + ", " + D.Enum(t.MatchType) + ", " + D.Enum(t.Action) + ", " +
                D.Str(t.Note) + ", " + D.Str(t.CreatedByUserId) + ", " + D.Ts(t.CreatedUtc) + ", " + D.Ts(t.UpdatedUtc) + ")";
        }

        private static WordFilterTermView MapView(DataRow row)
        {
            string? createdBy = RowReader.GetNullableString(row, "createdbyuserid");
            string? name = RowReader.GetNullableString(row, "createdbyname");
            if (createdBy == null) name = "admin token";
            else if (name == null || RowReader.GetBool(row, "createdbydeleted")) name = "Deleted member";
            return new WordFilterTermView
            {
                Id = RowReader.GetString(row, "id"),
                Term = RowReader.GetString(row, "term"),
                MatchType = RowReader.GetEnum(row, "matchtype", WordFilterMatchTypeEnum.Word),
                Action = RowReader.GetEnum(row, "action", WordFilterActionEnum.Block),
                Note = RowReader.GetNullableString(row, "note"),
                CreatedByName = name,
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc"),
                HitCount30d = RowReader.GetLong(row, "hitcount")
            };
        }

        private static WordFilterTerm Map(DataRow row)
        {
            return new WordFilterTerm
            {
                Id = RowReader.GetString(row, "id"),
                Term = RowReader.GetString(row, "term"),
                NormalizedTerm = RowReader.GetString(row, "normalizedterm"),
                MatchType = RowReader.GetEnum(row, "matchtype", WordFilterMatchTypeEnum.Word),
                Action = RowReader.GetEnum(row, "action", WordFilterActionEnum.Block),
                Note = RowReader.GetNullableString(row, "note"),
                CreatedByUserId = RowReader.GetNullableString(row, "createdbyuserid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }
    }
}
