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
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Shared audit methods.
    /// </summary>
    internal abstract class AuditMethodsBase : MethodsBase, IAuditMethods
    {
        protected AuditMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<AuditRecord> CreateAsync(AuditRecord record, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(record);
            AuditRecord a = record;
            await QueryAsync("INSERT INTO auditrecords (propertyid, id, actoruserid, actorname, action, targettype, targetid, summary, sourceip, createdutc) VALUES (" +
                Pid + ", " + D.Str(a.Id) + ", " + D.Str(a.ActorUserId) + ", " + D.Str(a.ActorName) + ", " + D.Str(a.Action) + ", " + D.Str(a.TargetType) + ", " +
                D.Str(a.TargetId) + ", " + D.Str(a.Summary) + ", " + D.Str(a.SourceIp) + ", " + D.Ts(a.CreatedUtc) + ");", token).ConfigureAwait(false);
            return record;
        }

        public Task<EnumerationResult<AuditRecord>> EnumerateAsync(EnumerationRequest request, AuditFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string>();
            if (filter.ActorUserId != null) conditions.Add("ar.actoruserid = " + D.Str(filter.ActorUserId));
            if (filter.TargetType != null) conditions.Add("ar.targettype = " + D.Str(filter.TargetType));
            if (filter.TargetId != null) conditions.Add("ar.targetid = " + D.Str(filter.TargetId));
            if (filter.Action != null) conditions.Add("ar.action = " + D.Str(filter.Action));
            if (filter.FromUtc != null) conditions.Add("ar.createdutc >= " + D.Ts(filter.FromUtc.Value));
            if (filter.ToUtc != null) conditions.Add("ar.createdutc < " + D.Ts(filter.ToUtc.Value));
            return EnumerateAsync(request, "ar.id, ar.actoruserid, ar.actorname, ar.action, ar.targettype, ar.targetid, ar.summary, ar.sourceip, ar.createdutc", "auditrecords ar", conditions, Map, token);
        }

        public async Task<List<AuditRecord>> EnumerateByUserAsync(string userId, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable rows = await QueryAsync("SELECT * FROM auditrecords WHERE " + Scope() + " AND (actoruserid = " + D.Str(userId) + " OR targetid = " + D.Str(userId) + ")" +
                " ORDER BY createdutc DESC, id DESC LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return rows.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<List<AuditRecord>> EnumerateByUserAfterAsync(string userId, string? afterId, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            string after = afterId == null ? String.Empty : " AND id > " + D.Str(afterId);
            DataTable table = await QueryAsync("SELECT * FROM auditrecords WHERE " + Scope() + " AND (actoruserid = " + D.Str(userId) + " OR targetid = " + D.Str(userId) + ")" + after + " ORDER BY id LIMIT " + D.Num(Math.Clamp(limit, 1, 1000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<int> CountByActionAndIpSinceAsync(string action, string sourceIp, DateTime sinceUtc, int cap, CancellationToken token = default)
        {
            RequireText(action, nameof(action));
            RequireText(sourceIp, nameof(sourceIp));
            DataTable table = await QueryAsync("SELECT COUNT(*) AS n FROM (SELECT 1 FROM auditrecords WHERE " + Scope() + " AND action = " + D.Str(action) +
                " AND createdutc >= " + D.Ts(sinceUtc) + " AND sourceip = " + D.Str(sourceIp) + " LIMIT " + D.Num(Math.Clamp(cap, 1, 1000)) + ") capped;", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? 0 : Convert.ToInt32(table.Rows[0]["n"], System.Globalization.CultureInfo.InvariantCulture);
        }

        private static AuditRecord Map(DataRow row)
        {
            return new AuditRecord
            {
                Id = RowReader.GetString(row, "id"),
                ActorUserId = RowReader.GetNullableString(row, "actoruserid"),
                ActorName = RowReader.GetNullableString(row, "actorname"),
                Action = RowReader.GetString(row, "action"),
                TargetType = RowReader.GetNullableString(row, "targettype"),
                TargetId = RowReader.GetNullableString(row, "targetid"),
                Summary = RowReader.GetNullableString(row, "summary"),
                SourceIp = RowReader.GetNullableString(row, "sourceip"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }
    }
}
