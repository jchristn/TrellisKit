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
    /// Shared role assignment methods.
    /// </summary>
    internal abstract class RoleAssignmentMethodsBase : MethodsBase, IRoleAssignmentMethods
    {
        protected RoleAssignmentMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<List<RoleAssignment>> EnumerateByUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT * FROM roleassignments WHERE " + Scope() + " AND userid = " + D.Str(userId) + " ORDER BY createdutc, id;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<List<RoleAssignment>> EnumerateByUsersAsync(IEnumerable<string> userIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(userIds);
            List<string> ids = userIds.ToList();
            if (ids.Count == 0) return new List<RoleAssignment>();
            DataTable table = await QueryAsync("SELECT * FROM roleassignments WHERE " + Scope() + " AND userid IN " + D.InList(ids) + " ORDER BY createdutc, id;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<List<RoleAssignment>> EnumerateByRoleAsync(RoleEnum role, int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT * FROM roleassignments WHERE " + Scope() + " AND role = " + D.Enum(role) + " ORDER BY createdutc, id LIMIT " + D.Num(Math.Clamp(limit, 1, 100)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task ReplaceForUserAsync(string userId, List<RoleAssignment> assignments, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            ArgumentNullException.ThrowIfNull(assignments);
            List<string> statements = new List<string> { "DELETE FROM roleassignments WHERE " + Scope() + " AND userid = " + D.Str(userId) + ";" };
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (RoleAssignment assignment in assignments)
            {
                if (!seen.Add(assignment.Role + "|" + assignment.ResourceType + "|" + assignment.ResourceId)) continue;
                assignment.UserId = userId;
                statements.Add(Insert(assignment) + ";");
            }
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        public async Task<bool> AddAsync(RoleAssignment assignment, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(assignment);
            DataTable table = await QueryAsync(Insert(assignment) + " ON CONFLICT (propertyid, userid, role, resourcetype, resourceid) DO NOTHING RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        private string Insert(RoleAssignment a)
        {
            return "INSERT INTO roleassignments (propertyid, id, userid, role, resourcetype, resourceid, grantedbyuserid, createdutc) VALUES (" +
                Pid + ", " + D.Str(a.Id) + ", " + D.Str(a.UserId) + ", " + D.Enum(a.Role) + ", " + D.Enum(a.ResourceType) + ", " + D.Str(a.ResourceId) + ", " +
                D.Str(a.GrantedByUserId) + ", " + D.Ts(a.CreatedUtc) + ")";
        }

        private static RoleAssignment Map(DataRow row)
        {
            return new RoleAssignment
            {
                Id = RowReader.GetString(row, "id"),
                UserId = RowReader.GetString(row, "userid"),
                Role = RowReader.GetEnum(row, "role", RoleEnum.User),
                ResourceType = RowReader.GetEnum(row, "resourcetype", ResourceTypeEnum.Global),
                ResourceId = RowReader.GetString(row, "resourceid"),
                GrantedByUserId = RowReader.GetNullableString(row, "grantedbyuserid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }
    }
}
