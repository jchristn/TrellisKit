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
    /// Shared account export job methods.
    /// </summary>
    internal abstract class AccountExportMethodsBase : MethodsBase, IAccountExportMethods
    {
        private const string _Columns = "id, userid, status, createdutc, startedutc, completedutc, expiresutc, sizebytes, error, claimedby, claimuntilutc";

        protected AccountExportMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<AccountExport> CreateAsync(AccountExport export, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(export);
            AccountExport e = export;
            await UniqueAsync("INSERT INTO accountexports (propertyid, " + _Columns + ") VALUES (" + Pid + ", " + D.Str(e.Id) + ", " + D.Str(e.UserId) + ", " + D.Enum(e.Status) + ", " +
                D.Ts(e.CreatedUtc) + ", " + D.Ts(e.StartedUtc) + ", " + D.Ts(e.CompletedUtc) + ", " + D.Ts(e.ExpiresUtc) + ", " +
                (e.SizeBytes == null ? "NULL" : D.Num(e.SizeBytes.Value)) + ", " + D.Str(e.Error) + ", " + D.Str(e.ClaimedBy) + ", " + D.Ts(e.ClaimUntilUtc) + ");",
                "A running export for this member", token).ConfigureAwait(false);
            return export;
        }

        public async Task<AccountExport?> ReadAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM accountexports WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<AccountExport?> ReadActiveByUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM accountexports WHERE " + Scope() + " AND userid = " + D.Str(userId) + " AND status IN " +
                D.InList(new[] { AccountExportStatusEnum.Pending.ToString(), AccountExportStatusEnum.Running.ToString() }) + " ORDER BY createdutc DESC, id DESC LIMIT 1;", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<List<AccountExport>> ClaimAsync(string holder, DateTime nowUtc, DateTime claimUntilUtc, int limit, CancellationToken token = default)
        {
            RequireText(holder, nameof(holder));
            string claimable = "(status = " + D.Enum(AccountExportStatusEnum.Pending) + " OR (status = " + D.Enum(AccountExportStatusEnum.Running) +
                " AND (claimuntilutc IS NULL OR claimuntilutc < " + D.Ts(nowUtc) + ")))";
            DataTable table = await QueryAsync("UPDATE accountexports SET status = " + D.Enum(AccountExportStatusEnum.Running) + ", claimedby = " + D.Str(holder) +
                ", claimuntilutc = " + D.Ts(claimUntilUtc) + ", startedutc = " + D.Ts(nowUtc) +
                " WHERE " + Scope() + " AND id IN (SELECT id FROM accountexports WHERE " + Scope() + " AND " + claimable + " ORDER BY createdutc, id LIMIT " + D.Num(Math.Max(1, limit)) + ") AND " + claimable +
                " RETURNING " + _Columns + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<bool> CompleteAsync(string id, string holder, long sizeBytes, DateTime completedUtc, DateTime expiresUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            RequireText(holder, nameof(holder));
            DataTable table = await QueryAsync("UPDATE accountexports SET status = " + D.Enum(AccountExportStatusEnum.Ready) + ", sizebytes = " + D.Num(sizeBytes) +
                ", completedutc = " + D.Ts(completedUtc) + ", expiresutc = " + D.Ts(expiresUtc) + ", claimedby = NULL, claimuntilutc = NULL" +
                " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND claimedby = " + D.Str(holder) + " AND status = " + D.Enum(AccountExportStatusEnum.Running) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task FailAsync(string id, string holder, string error, DateTime completedUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            RequireText(holder, nameof(holder));
            string text = error ?? String.Empty;
            if (text.Length > 1000) text = text.Substring(0, 1000);
            await QueryAsync("UPDATE accountexports SET status = " + D.Enum(AccountExportStatusEnum.Failed) + ", error = " + D.Str(text) + ", completedutc = " + D.Ts(completedUtc) +
                ", claimedby = NULL, claimuntilutc = NULL WHERE " + Scope() + " AND id = " + D.Str(id) + " AND claimedby = " + D.Str(holder) + " AND status = " + D.Enum(AccountExportStatusEnum.Running) + ";", token).ConfigureAwait(false);
        }

        public async Task<List<AccountExport>> EnumerateExpiredAsync(DateTime nowUtc, int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM accountexports WHERE " + Scope() + " AND status = " + D.Enum(AccountExportStatusEnum.Ready) +
                " AND expiresutc <= " + D.Ts(nowUtc) + " ORDER BY expiresutc, id LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<bool> MarkExpiredAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE accountexports SET status = " + D.Enum(AccountExportStatusEnum.Expired) + " WHERE " + Scope() + " AND id = " + D.Str(id) +
                " AND status = " + D.Enum(AccountExportStatusEnum.Ready) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<List<string>> DeleteByUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("DELETE FROM accountexports WHERE " + Scope() + " AND userid = " + D.Str(userId) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "id")).ToList();
        }

        private static AccountExport Map(DataRow row)
        {
            return new AccountExport
            {
                Id = RowReader.GetString(row, "id"),
                UserId = RowReader.GetString(row, "userid"),
                Status = RowReader.GetEnum(row, "status", AccountExportStatusEnum.Failed),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                StartedUtc = RowReader.GetNullableDateTime(row, "startedutc"),
                CompletedUtc = RowReader.GetNullableDateTime(row, "completedutc"),
                ExpiresUtc = RowReader.GetNullableDateTime(row, "expiresutc"),
                SizeBytes = RowReader.GetNullableLong(row, "sizebytes"),
                Error = RowReader.GetNullableString(row, "error"),
                ClaimedBy = RowReader.GetNullableString(row, "claimedby"),
                ClaimUntilUtc = RowReader.GetNullableDateTime(row, "claimuntilutc")
            };
        }
    }
}
