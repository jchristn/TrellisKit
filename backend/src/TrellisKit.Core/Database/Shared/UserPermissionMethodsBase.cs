namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Shared per-user posting permission methods.
    /// </summary>
    internal abstract class UserPermissionMethodsBase : MethodsBase, IUserPermissionMethods
    {
        private const string _Columns = "userid, canpost, canpostmedia, updatedutc, updatedbyuserid";

        protected UserPermissionMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<UserPermission> ReadAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM userpermissions WHERE " + Scope() + " AND userid = " + D.Str(userId) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? new UserPermission { UserId = userId } : Map(table.Rows[0]);
        }

        public async Task<Dictionary<string, UserPermission>> ReadManyAsync(IEnumerable<string> userIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(userIds);
            List<string> ids = userIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            Dictionary<string, UserPermission> result = new Dictionary<string, UserPermission>(StringComparer.Ordinal);
            if (ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM userpermissions WHERE " + Scope() + " AND userid IN " + D.InList(ids) + ";", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows)
            {
                UserPermission permission = Map(row);
                result[permission.UserId] = permission;
            }
            foreach (string id in ids)
            {
                if (!result.ContainsKey(id)) result[id] = new UserPermission { UserId = id };
            }
            return result;
        }

        public async Task<UserPermission> UpsertAsync(UserPermission permission, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(permission);
            RequireText(permission.UserId, nameof(permission.UserId));
            await QueryAsync("INSERT INTO userpermissions (propertyid, " + _Columns + ") VALUES (" + Pid + ", " + D.Str(permission.UserId) + ", " + D.Bool(permission.CanPost) + ", " +
                D.Bool(permission.CanPostMedia) + ", " + D.Ts(permission.UpdatedUtc) + ", " + D.Str(permission.UpdatedByUserId) + ") " +
                "ON CONFLICT (userid) DO UPDATE SET canpost = excluded.canpost, canpostmedia = excluded.canpostmedia, updatedutc = excluded.updatedutc, updatedbyuserid = excluded.updatedbyuserid;", token).ConfigureAwait(false);
            return permission;
        }

        public async Task DeleteAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            await QueryAsync("DELETE FROM userpermissions WHERE " + Scope() + " AND userid = " + D.Str(userId) + ";", token).ConfigureAwait(false);
        }

        private static UserPermission Map(DataRow row)
        {
            return new UserPermission
            {
                UserId = RowReader.GetString(row, "userid"),
                CanPost = RowReader.GetBool(row, "canpost"),
                CanPostMedia = RowReader.GetBool(row, "canpostmedia"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc"),
                UpdatedByUserId = RowReader.GetNullableString(row, "updatedbyuserid")
            };
        }
    }
}
