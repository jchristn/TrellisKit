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

    internal abstract class AttendanceMethodsBase : MethodsBase, IAttendanceMethods
    {
        protected AttendanceMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task CreateAsync(AttendanceView record, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(record);
            await UniqueAsync("INSERT INTO attendance (propertyid, id, userid, targettype, targetid, occurrencedate, checkedinutc, method, checkedinby, createdutc, updatedutc) VALUES (" +
                Pid + ", " + D.Str(record.Id) + ", " + D.Str(record.UserId) + ", " + D.Enum(record.TargetType) + ", " + D.Str(record.TargetId) + ", " + D.Str(record.OccurrenceDate) + ", " +
                D.Ts(record.CheckedInUtc) + ", " + D.Enum(record.Method) + ", " + D.Str(record.CheckedInBy) + ", " + D.Ts(record.CreatedUtc) + ", " + D.Ts(record.CreatedUtc) + ");",
                "A check-in for this member, target, and date", token).ConfigureAwait(false);
        }

        public async Task<AttendanceView?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            DataTable table = await QueryAsync("SELECT * FROM attendance WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            List<string> statements = Driver.Metadata.DeleteStatements(TargetTypeEnum.Attendance, id);
            statements.Add("DELETE FROM attendance WHERE " + Scope() + " AND id = " + D.Str(id) + " RETURNING id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<EnumerationResult<AttendanceView>> EnumerateAsync(EnumerationRequest request, AttendanceFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string>();
            if (!String.IsNullOrEmpty(filter.UserId)) conditions.Add("a.userid = " + D.Str(filter.UserId));
            if (filter.TargetType != null) conditions.Add("a.targettype = " + D.Enum(filter.TargetType.Value));
            if (!String.IsNullOrEmpty(filter.TargetId)) conditions.Add("a.targetid = " + D.Str(filter.TargetId));
            if (!String.IsNullOrEmpty(filter.OccurrenceDate)) conditions.Add("a.occurrencedate = " + D.Str(filter.OccurrenceDate));
            return await EnumerateAsync(request, "a.*", "attendance a", conditions, Map, token).ConfigureAwait(false);
        }

        public Task<long> CountForUserAsync(string userId, DateTime? sinceUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            return CountAsync("SELECT COUNT(*) AS cnt FROM attendance WHERE " + Scope() + " AND userid = " + D.Str(userId) +
                (sinceUtc == null ? String.Empty : " AND checkedinutc >= " + D.Ts(sinceUtc.Value)) + ";", token);
        }

        public async Task<List<AttendanceView>> ListForUserAsync(string userId, string? afterId, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            string after = String.IsNullOrEmpty(afterId) ? String.Empty : " AND id > " + D.Str(afterId);
            DataTable table = await QueryAsync("SELECT * FROM attendance WHERE " + Scope() + " AND userid = " + D.Str(userId) + after + " ORDER BY id LIMIT " + D.Num(Math.Clamp(limit, 1, 1000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task EraseForUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            string match = Scope() + " AND userid = " + D.Str(userId);
            List<string> statements = Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.Attendance, "SELECT id FROM attendance WHERE " + match);
            statements.Add("DELETE FROM attendance WHERE " + match + ";");
            statements.Add("UPDATE attendance SET checkedinby = NULL, updatedutc = " + D.Ts(DateTime.UtcNow) + " WHERE " + Scope() + " AND checkedinby = " + D.Str(userId) + ";");
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        private static AttendanceView Map(DataRow row)
        {
            return new AttendanceView
            {
                Id = RowReader.GetString(row, "id"),
                UserId = RowReader.GetString(row, "userid"),
                TargetType = RowReader.GetEnum(row, "targettype", AttendanceTargetTypeEnum.Event),
                TargetId = RowReader.GetString(row, "targetid"),
                OccurrenceDate = RowReader.GetString(row, "occurrencedate"),
                CheckedInUtc = RowReader.GetDateTime(row, "checkedinutc"),
                Method = RowReader.GetEnum(row, "method", AttendanceMethodEnum.SelfWithCode),
                CheckedInBy = RowReader.GetNullableString(row, "checkedinby"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }
    }
}
