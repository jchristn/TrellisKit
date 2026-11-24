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

    internal abstract class LessonDownloadMethodsBase : MethodsBase, ILessonDownloadMethods
    {
        private const string _Columns = "id, lessonid, userid, deviceid, platform, licenseexpiresutc, revokedutc, revokereason, lastrenewedutc, createdutc";

        protected LessonDownloadMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<LessonDownload> IssueAsync(LessonDownload download, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(download);
            RequireText(download.Id, nameof(download.Id));
            DataTable table = await QueryAsync("INSERT INTO lessondownloads (propertyid, " + _Columns + ") VALUES (" + Pid + ", " + D.Str(download.Id) + ", " + D.Str(download.LessonId) + ", " + D.Str(download.UserId) +
                ", " + D.Str(download.DeviceId) + ", " + D.Enum(download.Platform) + ", " + D.Ts(download.LicenseExpiresUtc) + ", NULL, NULL, NULL, " + D.Ts(download.CreatedUtc) +
                ") ON CONFLICT (propertyid, userid, deviceid, lessonid) WHERE revokedutc IS NULL DO UPDATE SET licenseexpiresutc = excluded.licenseexpiresutc, platform = excluded.platform, lastrenewedutc = excluded.createdutc RETURNING " +
                _Columns + ";", token).ConfigureAwait(false);
            return Map(table.Rows[0]);
        }

        public async Task<LessonDownload?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM lessondownloads WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<LessonDownload?> RenewAsync(string id, DateTime expiresUtc, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE lessondownloads SET licenseexpiresutc = " + D.Ts(expiresUtc) + ", lastrenewedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) +
                " AND revokedutc IS NULL RETURNING " + _Columns + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<bool> RevokeAsync(string id, string reason, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE lessondownloads SET revokedutc = " + D.Ts(nowUtc) + ", revokereason = " + D.Str(reason) + " WHERE " + Scope() + " AND id = " + D.Str(id) +
                " AND revokedutc IS NULL RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public Task<long> CountActiveAsync(string userId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            return CountAsync("SELECT COUNT(*) AS cnt FROM lessondownloads WHERE " + Scope() + " AND userid = " + D.Str(userId) + " AND revokedutc IS NULL AND licenseexpiresutc > " + D.Ts(nowUtc) + ";", token);
        }

        public async Task<List<string>> ActiveDevicesAsync(string userId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT DISTINCT deviceid FROM lessondownloads WHERE " + Scope() + " AND userid = " + D.Str(userId) + " AND revokedutc IS NULL AND licenseexpiresutc > " + D.Ts(nowUtc) +
                " ORDER BY deviceid LIMIT 100;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "deviceid")).ToList();
        }

        public Task<EnumerationResult<LessonDownload>> EnumerateAsync(EnumerationRequest request, string userId, bool activeOnly, string? deviceId, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            RequireText(userId, nameof(userId));
            List<string> conditions = new List<string> { "d.userid = " + D.Str(userId) };
            if (activeOnly) conditions.Add("d.revokedutc IS NULL AND d.licenseexpiresutc > " + D.Ts(nowUtc));
            if (!String.IsNullOrEmpty(deviceId)) conditions.Add("d.deviceid = " + D.Str(deviceId));
            return EnumerateAsync(request, "d." + _Columns.Replace(", ", ", d."), "lessondownloads d", conditions, Map, token);
        }

        public async Task<int> RevokeAllAsync(string? userId, string? lessonId, string reason, DateTime nowUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId) && String.IsNullOrEmpty(lessonId)) return 0;
            List<string> conditions = new List<string> { Scope(), "revokedutc IS NULL" };
            if (!String.IsNullOrEmpty(userId)) conditions.Add("userid = " + D.Str(userId));
            if (!String.IsNullOrEmpty(lessonId)) conditions.Add("lessonid = " + D.Str(lessonId));
            DataTable table = await QueryAsync("UPDATE lessondownloads SET revokedutc = " + D.Ts(nowUtc) + ", revokereason = " + D.Str(reason) + Where(conditions) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public async Task<List<string>> PruneAsync(DateTime nowUtc, DateTime deleteBeforeUtc, int limit, CancellationToken token = default)
        {
            int max = Math.Max(1, limit);
            DataTable expired = await QueryAsync("UPDATE lessondownloads SET revokedutc = " + D.Ts(nowUtc) + ", revokereason = 'expired' WHERE " + Scope() + " AND id IN (SELECT e.id FROM lessondownloads e WHERE " +
                Scope("e") + " AND e.revokedutc IS NULL AND e.licenseexpiresutc <= " + D.Ts(nowUtc) + " ORDER BY e.licenseexpiresutc LIMIT " + D.Num(max) + ") RETURNING platform;", token).ConfigureAwait(false);
            await QueryAsync("DELETE FROM lessondownloads WHERE " + Scope() + " AND id IN (SELECT o.id FROM lessondownloads o WHERE " + Scope("o") + " AND o.revokedutc IS NOT NULL AND o.revokedutc < " +
                D.Ts(deleteBeforeUtc) + " ORDER BY o.revokedutc LIMIT " + D.Num(max) + ");", token).ConfigureAwait(false);
            return expired.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "platform")).ToList();
        }

        public async Task<List<LessonDownload>> ListForUserAsync(string userId, string? afterId, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM lessondownloads WHERE " + Scope() + " AND userid = " + D.Str(userId) + (afterId == null ? String.Empty : " AND id > " + D.Str(afterId)) +
                " ORDER BY id LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<int> DeleteForUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("DELETE FROM lessondownloads WHERE " + Scope() + " AND userid = " + D.Str(userId) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        private static LessonDownload Map(DataRow row)
        {
            return new LessonDownload
            {
                Id = RowReader.GetString(row, "id"),
                LessonId = RowReader.GetString(row, "lessonid"),
                UserId = RowReader.GetString(row, "userid"),
                DeviceId = RowReader.GetString(row, "deviceid"),
                Platform = RowReader.GetEnum(row, "platform", DownloadPlatformEnum.Ios),
                LicenseExpiresUtc = RowReader.GetDateTime(row, "licenseexpiresutc"),
                RevokedUtc = RowReader.GetNullableDateTime(row, "revokedutc"),
                RevokeReason = RowReader.GetNullableString(row, "revokereason"),
                LastRenewedUtc = RowReader.GetNullableDateTime(row, "lastrenewedutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }
    }
}
