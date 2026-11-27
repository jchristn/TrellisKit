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
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Shared report methods.
    /// </summary>
    internal abstract class ReportMethodsBase : ModerationMethodsBase, IReportMethods
    {
        protected ReportMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<Report?> CreateAsync(Report report, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(report);
            RequireText(report.Id, nameof(report.Id));
            RequireText(report.ReporterUserId, nameof(report.ReporterUserId));
            RequireText(report.TargetId, nameof(report.TargetId));
            string sql = "INSERT INTO reports (propertyid, id, reporteruserid, targettype, targetid, targetauthoruserid, scopetype, scopeid, reason, details, status, dueutc, createdutc) " +
                "SELECT " + Pid + ", " + D.Str(report.Id) + ", " + D.Str(report.ReporterUserId) + ", " + D.Enum(report.TargetType) + ", " + D.Str(report.TargetId) + ", " +
                D.Str(report.TargetAuthorUserId) + ", " + D.Str(report.ScopeType) + ", " + D.Str(report.ScopeId) + ", " + D.Enum(report.Reason) + ", " + D.Str(report.Details) + ", " +
                D.Enum(ReportStatusEnum.Open) + ", " + D.Ts(report.DueUtc) + ", " + D.Ts(report.CreatedUtc) +
                " WHERE NOT EXISTS (SELECT 1 FROM reports e WHERE " + Scope("e") + " AND e.reporteruserid = " + D.Str(report.ReporterUserId) + " AND e.targettype = " + D.Enum(report.TargetType) +
                " AND e.targetid = " + D.Str(report.TargetId) + " AND e.status = " + D.Enum(ReportStatusEnum.Open) + ") ON CONFLICT DO NOTHING RETURNING id;";
            DataTable table = await QueryAsync(sql, token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return await ReadAsync(report.Id, token).ConfigureAwait(false);
        }

        public async Task<Report?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            DataTable table = await QueryAsync("SELECT * FROM reports WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<Report?> ReadOpenByReporterAsync(string reporterUserId, TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(reporterUserId) || String.IsNullOrEmpty(targetId)) return null;
            DataTable table = await QueryAsync("SELECT * FROM reports WHERE " + Scope() + " AND reporteruserid = " + D.Str(reporterUserId) + " AND targettype = " + D.Enum(targetType) +
                " AND targetid = " + D.Str(targetId) + " AND status = " + D.Enum(ReportStatusEnum.Open) + " ORDER BY createdutc, id LIMIT 1;", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<bool> TryAutoHideAsync(ModerationAction action, int threshold, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(action);
            RequireText(action.Id, nameof(action.Id));
            RequireText(action.TargetId, nameof(action.TargetId));
            string table;
            if (action.TargetType == TargetTypeEnum.FeedPost) table = "feedposts";
            else if (action.TargetType == TargetTypeEnum.Comment) table = "comments";
            else if (action.TargetType == TargetTypeEnum.Thread) table = "forumthreads";
            else if (action.TargetType == TargetTypeEnum.Idea) table = "ideas";
            else throw new ArgumentException("Only feed posts, threads, and comments can be auto-hidden.", nameof(action));
            string type = D.Enum(action.TargetType);
            string id = D.Str(action.TargetId);
            string now = D.Ts(action.CreatedUtc);
            string hidden = D.Enum(ModerationStateEnum.Hidden);
            string reporters = "(SELECT COUNT(DISTINCT r.reporteruserid) FROM reports r WHERE " + Scope("r") + " AND r.targettype = " + type + " AND r.targetid = " + id +
                " AND r.status = " + D.Enum(ReportStatusEnum.Open) + " AND r.reporteruserid IS NOT NULL)";
            string activeAutoHide = "EXISTS (SELECT 1 FROM moderationactions ah WHERE " + Scope("ah") + " AND ah.targettype = " + type + " AND ah.targetid = " + id + " AND ah.action = " +
                D.Enum(ModerationActionTypeEnum.AutoHide) + " AND NOT EXISTS (SELECT 1 FROM moderationactions un WHERE " + Scope("un") + " AND un.targettype = " + type + " AND un.targetid = " + id +
                " AND un.action IN " + D.InList(new[] { ModerationActionTypeEnum.Unhide.ToString(), ModerationActionTypeEnum.Restore.ToString() }) + " AND un.createdutc >= ah.createdutc))";
            List<string> statements = new List<string>
            {
                "UPDATE " + table + " SET moderationstate = " + hidden + ", moderatedutc = " + now + ", moderatedbyuserid = NULL, moderationreason = " + D.Str(action.Reason) +
                    (table == "feedposts" || table == "forumthreads" ? ", ispinned = " + D.Bool(false) : String.Empty) + ", updatedutc = " + now +
                    " WHERE " + Scope() + " AND id = " + id + " AND deletedutc IS NULL AND moderationstate = " + D.Enum(ModerationStateEnum.None) + " AND " + reporters + " >= " + D.Num(Math.Max(1, threshold)) + ";",
                "INSERT INTO moderationactions (propertyid, id, moderatoruserid, targettype, targetid, targetauthoruserid, action, reason, note, notifiedauthor, createdutc) SELECT " +
                    Pid + ", " + D.Str(action.Id) + ", NULL, " + type + ", " + id + ", " + D.Str(action.TargetAuthorUserId) + ", " + D.Enum(ModerationActionTypeEnum.AutoHide) + ", " +
                    D.Str(action.Reason) + ", " + D.Str(action.Note) + ", " + D.Bool(false) + ", " + now +
                    " WHERE EXISTS (SELECT 1 FROM " + table + " t WHERE " + Scope("t") + " AND t.id = " + id + " AND t.moderationstate = " + hidden + " AND t.moderatedbyuserid IS NULL AND t.moderatedutc = " + now + ")" +
                    " AND NOT " + activeAutoHide + " RETURNING id;"
            };
            DataTable result = await TransactionAsync(statements, token).ConfigureAwait(false);
            return result.Rows.Count > 0;
        }

        public async Task<int> ResolveOpenAsync(TargetTypeEnum targetType, string targetId, ReportStatusEnum status, string? resolvedByUserId, string? note, string? moderationActionId,
            DateTime nowUtc, List<string>? scopeTypes, CancellationToken token = default)
        {
            RequireText(targetId, nameof(targetId));
            List<string> conditions = new List<string> { Scope(), "targettype = " + D.Enum(targetType), "targetid = " + D.Str(targetId), "status = " + D.Enum(ReportStatusEnum.Open) };
            string? scope = ScopeCondition("reports", scopeTypes);
            if (scope != null) conditions.Add(scope);
            DataTable table = await QueryAsync("UPDATE reports SET status = " + D.Enum(status) + ", resolvedbyuserid = " + D.Str(resolvedByUserId) + ", resolvedutc = " + D.Ts(nowUtc) +
                ", resolutionnote = " + D.Str(note) + ", moderationactionid = " + D.Str(moderationActionId) + Where(conditions) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public Task<EnumerationResult<ModerationQueueRow>> EnumerateQueueAsync(EnumerationRequest request, ModerationQueueFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            string open = D.Enum(ReportStatusEnum.Open);
            List<string> inner = new List<string> { Scope("r") };
            string? scope = ScopeCondition("r", filter.ScopeTypes);
            if (scope != null) inner.Add(scope);
            if (!filter.Resolved) inner.Add("r.status = " + open);
            if (filter.TargetType != null) inner.Add("r.targettype = " + D.Enum(filter.TargetType.Value));
            if (!String.IsNullOrEmpty(filter.ScopeTypeFilter)) inner.Add("r.scopetype = " + D.Str(filter.ScopeTypeFilter));
            if (filter.ForumIds.Count > 0) inner.Add("r.scopetype = 'Forum' AND r.scopeid IN " + D.InList(filter.ForumIds));
            string openCount = "SUM(CASE WHEN r.status = " + open + " THEN 1 ELSE 0 END)";
            string from = "(SELECT r.propertyid AS propertyid, r.targettype AS targettype, r.targetid AS targetid, r.targettype || ':' || r.targetid AS qid, MAX(r.targetauthoruserid) AS targetauthoruserid, " +
                "COUNT(*) AS reportcount, COUNT(DISTINCT r.reporteruserid) AS reportercount, MIN(r.createdutc) AS firstreportedutc, MAX(r.createdutc) AS lastreportedutc, " +
                "MIN(r.dueutc) AS dueutc, MAX(r.scopetype) AS scopetype, MAX(r.scopeid) AS scopeid, " + openCount + " AS opencount FROM reports r" + Where(inner) + " GROUP BY r.propertyid, r.targettype, r.targetid" +
                (filter.Resolved ? " HAVING " + openCount + " = 0" : String.Empty) + ") q";
            List<string> conditions = new List<string>();
            if (filter.Reason != null)
            {
                conditions.Add("EXISTS (SELECT 1 FROM reports rr WHERE " + Scope("rr") + " AND rr.targettype = q.targettype AND rr.targetid = q.targetid AND rr.reason = " + D.Enum(filter.Reason.Value) +
                    (filter.Resolved ? String.Empty : " AND rr.status = " + open) + ")");
            }
            if (filter.Overdue != null) conditions.Add("q.dueutc " + (filter.Overdue.Value ? "<" : ">=") + " " + D.Ts(filter.NowUtc));
            conditions.AddRange(TargetMetadataConditions(request.Query, "q.targettype", "q.targetid"));
            return EnumerateAsync(WithoutMetadata(request), "q.targettype, q.targetid, q.qid, q.targetauthoruserid, q.reportcount, q.reportercount, q.firstreportedutc, q.lastreportedutc, q.dueutc, q.scopetype, q.scopeid, q.opencount",
                from, conditions, MapQueueRow, token);
        }

        public async Task<Dictionary<string, Dictionary<string, long>>> ReasonCountsAsync(IReadOnlyList<ModerationQueueRow> rows, List<string>? scopeTypes, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(rows);
            Dictionary<string, Dictionary<string, long>> result = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);
            if (rows.Count == 0) return result;
            List<string> pairs = rows.Select(r => "(r.targettype = " + D.Enum(r.TargetType) + " AND r.targetid = " + D.Str(r.TargetId) +
                (r.HasOpen ? " AND r.status = " + D.Enum(ReportStatusEnum.Open) : String.Empty) + ")").ToList();
            List<string> conditions = new List<string> { Scope("r"), "(" + String.Join(" OR ", pairs) + ")" };
            string? scope = ScopeCondition("r", scopeTypes);
            if (scope != null) conditions.Add(scope);
            DataTable table = await QueryAsync("SELECT r.targettype, r.targetid, r.reason, COUNT(*) AS cnt FROM reports r" + Where(conditions) +
                " GROUP BY r.targettype, r.targetid, r.reason;", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows)
            {
                string key = RowReader.GetString(row, "targettype") + ":" + RowReader.GetString(row, "targetid");
                if (!result.TryGetValue(key, out Dictionary<string, long>? counts))
                {
                    counts = new Dictionary<string, long>(StringComparer.Ordinal);
                    result[key] = counts;
                }
                counts[RowReader.GetString(row, "reason")] = RowReader.GetLong(row, "cnt");
            }
            return result;
        }

        public Task<EnumerationResult<Report>> EnumerateAsync(EnumerationRequest request, ReportFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string>();
            string? scope = ScopeCondition("r", filter.ScopeTypes);
            if (scope != null) conditions.Add(scope);
            if (filter.TargetType != null) conditions.Add("r.targettype = " + D.Enum(filter.TargetType.Value));
            if (!String.IsNullOrEmpty(filter.TargetId)) conditions.Add("r.targetid = " + D.Str(filter.TargetId));
            if (filter.Status != null) conditions.Add("r.status = " + D.Enum(filter.Status.Value));
            if (filter.Reason != null) conditions.Add("r.reason = " + D.Enum(filter.Reason.Value));
            if (!String.IsNullOrEmpty(filter.ReporterUserId)) conditions.Add("r.reporteruserid = " + D.Str(filter.ReporterUserId));
            return EnumerateAsync(request, "r.*", "reports r", conditions, Map, token);
        }

        public Task<long> CountOpenAsync(List<string>? scopeTypes, DateTime? dueBeforeUtc, CancellationToken token = default)
        {
            List<string> conditions = new List<string> { Scope("r"), "r.status = " + D.Enum(ReportStatusEnum.Open) };
            string? scope = ScopeCondition("r", scopeTypes);
            if (scope != null) conditions.Add(scope);
            if (dueBeforeUtc != null) conditions.Add("r.dueutc < " + D.Ts(dueBeforeUtc.Value));
            return CountAsync("SELECT COUNT(*) AS cnt FROM reports r" + Where(conditions) + ";", token);
        }

        public Task<long> CountOpenTargetsAsync(List<string>? scopeTypes, CancellationToken token = default)
        {
            List<string> conditions = new List<string> { Scope("r"), "r.status = " + D.Enum(ReportStatusEnum.Open) };
            string? scope = ScopeCondition("r", scopeTypes);
            if (scope != null) conditions.Add(scope);
            return CountAsync("SELECT COUNT(*) AS cnt FROM (SELECT r.targettype, r.targetid FROM reports r" + Where(conditions) + " GROUP BY r.targettype, r.targetid) t;", token);
        }

        public Task<long> CountOpenOnTargetAsync(TargetTypeEnum targetType, string targetId, List<string>? scopeTypes, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(targetId)) return Task.FromResult(0L);
            List<string> conditions = new List<string> { Scope("r"), "r.targettype = " + D.Enum(targetType), "r.targetid = " + D.Str(targetId), "r.status = " + D.Enum(ReportStatusEnum.Open) };
            string? scope = ScopeCondition("r", scopeTypes);
            if (scope != null) conditions.Add(scope);
            return CountAsync("SELECT COUNT(*) AS cnt FROM reports r" + Where(conditions) + ";", token);
        }

        public async Task<List<ModerationOverdueClaim>> ClaimOverdueAsync(DateTime nowUtc, int limit, CancellationToken token = default)
        {
            string due = Scope() + " AND status = " + D.Enum(ReportStatusEnum.Open) + " AND dueutc < " + D.Ts(nowUtc) + " AND overduenotifiedutc IS NULL";
            DataTable table = await QueryAsync("UPDATE reports SET overduenotifiedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id IN (SELECT id FROM reports WHERE " + due +
                " ORDER BY dueutc, id LIMIT " + D.Num(Math.Max(1, limit)) + ") AND overduenotifiedutc IS NULL RETURNING id, targettype, targetid, scopetype, scopeid;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => new ModerationOverdueClaim
            {
                ReportId = RowReader.GetString(r, "id"),
                TargetType = RowReader.GetEnum(r, "targettype", TargetTypeEnum.FeedPost),
                TargetId = RowReader.GetString(r, "targetid"),
                ScopeType = RowReader.GetString(r, "scopetype"),
                ScopeId = RowReader.GetNullableString(r, "scopeid")
            }).ToList();
        }

        public Task<long> CountAgainstAuthorAsync(string authorUserId, DateTime sinceUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(authorUserId)) return Task.FromResult(0L);
            return CountAsync("SELECT COUNT(*) AS cnt FROM reports WHERE " + Scope() + " AND targetauthoruserid = " + D.Str(authorUserId) + " AND createdutc >= " + D.Ts(sinceUtc) + ";", token);
        }

        public Task<long> CountByReporterAsync(string reporterUserId, DateTime sinceUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(reporterUserId)) return Task.FromResult(0L);
            return CountAsync("SELECT COUNT(*) AS cnt FROM reports WHERE " + Scope() + " AND reporteruserid = " + D.Str(reporterUserId) + " AND createdutc >= " + D.Ts(sinceUtc) + ";", token);
        }

        public async Task<Dictionary<string, long>> OpenBreakdownOnTargetAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            Dictionary<string, long> counts = new Dictionary<string, long>(StringComparer.Ordinal);
            if (String.IsNullOrEmpty(targetId)) return counts;
            string where = " FROM reports WHERE " + Scope() + " AND targettype = " + D.Enum(targetType) + " AND targetid = " + D.Str(targetId) + " AND status = " + D.Enum(ReportStatusEnum.Open);
            DataTable reasons = await QueryAsync("SELECT reason, COUNT(*) AS cnt" + where + " GROUP BY reason ORDER BY reason;", token).ConfigureAwait(false);
            foreach (DataRow row in reasons.Rows) counts[RowReader.GetString(row, "reason")] = Convert.ToInt64(row["cnt"], System.Globalization.CultureInfo.InvariantCulture);
            counts["reporters"] = await CountAsync("SELECT COUNT(DISTINCT reporteruserid) AS cnt" + where + " AND reporteruserid IS NOT NULL;", token).ConfigureAwait(false);
            return counts;
        }

        public async Task<Dictionary<string, long>> OverdueBreakdownAsync(DateTime nowUtc, CancellationToken token = default)
        {
            Dictionary<string, long> counts = new Dictionary<string, long>(StringComparer.Ordinal);
            string where = " FROM reports WHERE " + Scope() + " AND status = " + D.Enum(ReportStatusEnum.Open) + " AND dueutc < " + D.Ts(nowUtc);
            DataTable scopes = await QueryAsync("SELECT scopetype AS k, COUNT(*) AS cnt" + where + " GROUP BY scopetype ORDER BY scopetype LIMIT 50;", token).ConfigureAwait(false);
            foreach (DataRow row in scopes.Rows) counts["scope:" + RowReader.GetString(row, "k")] = Convert.ToInt64(row["cnt"], System.Globalization.CultureInfo.InvariantCulture);
            DataTable reasons = await QueryAsync("SELECT reason AS k, COUNT(*) AS cnt" + where + " GROUP BY reason ORDER BY reason LIMIT 50;", token).ConfigureAwait(false);
            foreach (DataRow row in reasons.Rows) counts["reason:" + RowReader.GetString(row, "k")] = Convert.ToInt64(row["cnt"], System.Globalization.CultureInfo.InvariantCulture);
            DataTable oldest = await QueryAsync("SELECT MIN(createdutc) AS oldest" + where + ";", token).ConfigureAwait(false);
            if (oldest.Rows.Count > 0 && oldest.Rows[0]["oldest"] != DBNull.Value)
                counts["oldest"] = new DateTimeOffset(DateTime.SpecifyKind(RowReader.GetDateTime(oldest.Rows[0], "oldest"), DateTimeKind.Utc)).ToUnixTimeSeconds();
            return counts;
        }

        public async Task<List<Report>> ListByReporterAsync(string reporterUserId, string? afterId, int limit, CancellationToken token = default)
        {
            RequireText(reporterUserId, nameof(reporterUserId));
            List<string> conditions = new List<string> { Scope(), "reporteruserid = " + D.Str(reporterUserId) };
            if (!String.IsNullOrEmpty(afterId)) conditions.Add("id > " + D.Str(afterId));
            DataTable table = await QueryAsync("SELECT * FROM reports" + Where(conditions) + " ORDER BY id ASC LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        private static ModerationQueueRow MapQueueRow(DataRow row)
        {
            return new ModerationQueueRow
            {
                TargetType = RowReader.GetEnum(row, "targettype", TargetTypeEnum.FeedPost),
                TargetId = RowReader.GetString(row, "targetid"),
                TargetAuthorUserId = RowReader.GetNullableString(row, "targetauthoruserid"),
                ReportCount = RowReader.GetLong(row, "reportcount"),
                ReporterCount = RowReader.GetLong(row, "reportercount"),
                FirstReportedUtc = RowReader.GetDateTime(row, "firstreportedutc"),
                LastReportedUtc = RowReader.GetDateTime(row, "lastreportedutc"),
                DueUtc = RowReader.GetDateTime(row, "dueutc"),
                HasOpen = RowReader.GetLong(row, "opencount") > 0,
                ScopeType = RowReader.GetNullableString(row, "scopetype") ?? "Feed",
                ScopeId = RowReader.GetNullableString(row, "scopeid")
            };
        }

        private static Report Map(DataRow row)
        {
            return new Report
            {
                Id = RowReader.GetString(row, "id"),
                ReporterUserId = RowReader.GetNullableString(row, "reporteruserid"),
                TargetType = RowReader.GetEnum(row, "targettype", TargetTypeEnum.FeedPost),
                TargetId = RowReader.GetString(row, "targetid"),
                TargetAuthorUserId = RowReader.GetNullableString(row, "targetauthoruserid"),
                ScopeType = RowReader.GetString(row, "scopetype"),
                ScopeId = RowReader.GetString(row, "scopeid"),
                Reason = RowReader.GetEnum(row, "reason", ReportReasonEnum.Other),
                Details = RowReader.GetNullableString(row, "details"),
                Status = RowReader.GetEnum(row, "status", ReportStatusEnum.Open),
                ResolvedByUserId = RowReader.GetNullableString(row, "resolvedbyuserid"),
                ResolvedUtc = RowReader.GetNullableDateTime(row, "resolvedutc"),
                ResolutionNote = RowReader.GetNullableString(row, "resolutionnote"),
                ModerationActionId = RowReader.GetNullableString(row, "moderationactionid"),
                DueUtc = RowReader.GetDateTime(row, "dueutc"),
                OverdueNotifiedUtc = RowReader.GetNullableDateTime(row, "overduenotifiedutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }
    }
}
