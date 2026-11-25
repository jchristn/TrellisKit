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

    /// <summary>
    /// Shared member block methods.
    /// </summary>
    internal abstract class BlockMethodsBase : MethodsBase, IBlockMethods
    {
        private static readonly string[] _StaffRoles = new[]
        {
            RoleEnum.SystemAdmin.ToString(), RoleEnum.Moderator.ToString(), RoleEnum.FeedModerator.ToString(), RoleEnum.DirectoryModerator.ToString(), RoleEnum.EventManager.ToString(),
            RoleEnum.LibraryEditor.ToString(), RoleEnum.SupportAgent.ToString(), RoleEnum.Communicator.ToString(), RoleEnum.RankAwarder.ToString()
        };

        protected BlockMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<bool> CreateAsync(string blockerUserId, string blockedUserId, DateTime nowUtc, int maxBlocks, CancellationToken token = default)
        {
            RequireText(blockerUserId, nameof(blockerUserId));
            RequireText(blockedUserId, nameof(blockedUserId));
            maxBlocks = Math.Max(1, maxBlocks);
            DataTable table = await QueryAsync("INSERT INTO blocks (propertyid, blockeruserid, blockeduserid, createdutc) SELECT " +
                Pid + ", " + D.Str(blockerUserId) + ", " + D.Str(blockedUserId) + ", " + D.Ts(nowUtc) +
                " WHERE (SELECT COUNT(*) FROM blocks WHERE " + Scope() + " AND blockeruserid = " + D.Str(blockerUserId) + ") < " + D.Num(maxBlocks) +
                " ON CONFLICT (blockeruserid, blockeduserid) DO NOTHING RETURNING blockeduserid;", token).ConfigureAwait(false);
            if (table.Rows.Count > 0) return true;
            return await ExistsAsync(blockerUserId, blockedUserId, token).ConfigureAwait(false);
        }

        public async Task<bool> DeleteAsync(string blockerUserId, string blockedUserId, CancellationToken token = default)
        {
            RequireText(blockerUserId, nameof(blockerUserId));
            RequireText(blockedUserId, nameof(blockedUserId));
            DataTable table = await QueryAsync("DELETE FROM blocks WHERE " + Scope() + " AND blockeruserid = " + D.Str(blockerUserId) + " AND blockeduserid = " + D.Str(blockedUserId) +
                " RETURNING blockeduserid;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<int> DeleteByUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("DELETE FROM blocks WHERE " + Scope() + " AND (blockeruserid = " + D.Str(userId) + " OR blockeduserid = " + D.Str(userId) + ")" +
                " RETURNING blockeduserid;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public async Task<bool> ExistsAsync(string blockerUserId, string blockedUserId, CancellationToken token = default)
        {
            RequireText(blockerUserId, nameof(blockerUserId));
            RequireText(blockedUserId, nameof(blockedUserId));
            DataTable table = await QueryAsync("SELECT 1 AS found FROM blocks WHERE " + Scope() + " AND blockeruserid = " + D.Str(blockerUserId) + " AND blockeduserid = " + D.Str(blockedUserId) + ";", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> EitherAsync(string firstUserId, string secondUserId, CancellationToken token = default)
        {
            RequireText(firstUserId, nameof(firstUserId));
            RequireText(secondUserId, nameof(secondUserId));
            DataTable table = await QueryAsync("SELECT 1 AS found FROM blocks WHERE " + Scope() + " AND ((blockeruserid = " + D.Str(firstUserId) + " AND blockeduserid = " + D.Str(secondUserId) +
                ") OR (blockeruserid = " + D.Str(secondUserId) + " AND blockeduserid = " + D.Str(firstUserId) + ")) LIMIT 1;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public Task<long> CountByBlockerAsync(string blockerUserId, CancellationToken token = default)
        {
            RequireText(blockerUserId, nameof(blockerUserId));
            return CountAsync("SELECT COUNT(*) AS cnt FROM blocks WHERE " + Scope() + " AND blockeruserid = " + D.Str(blockerUserId) + ";", token);
        }

        public Task<EnumerationResult<Block>> EnumerateByBlockerAsync(string blockerUserId, EnumerationRequest request, CancellationToken token = default)
        {
            RequireText(blockerUserId, nameof(blockerUserId));
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string> { "b.blockeruserid = " + D.Str(blockerUserId) };
            return EnumerateAsync(request, "b.blockeruserid, b.blockeduserid, b.createdutc", "blocks b LEFT JOIN users u ON u.id = b.blockeduserid AND " + Scope("u"), conditions, row => new Block
            {
                BlockerUserId = RowReader.GetString(row, "blockeruserid"),
                BlockedUserId = RowReader.GetString(row, "blockeduserid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            }, token);
        }

        public Task<HashSet<string>> BlockedByAsync(string blockerUserId, CancellationToken token = default)
        {
            RequireText(blockerUserId, nameof(blockerUserId));
            return IdsAsync("SELECT blockeduserid AS uid FROM blocks WHERE " + Scope() + " AND blockeruserid = " + D.Str(blockerUserId) + ";", token);
        }

        public Task<HashSet<string>> BlockersOfAsync(string blockedUserId, CancellationToken token = default)
        {
            RequireText(blockedUserId, nameof(blockedUserId));
            return IdsAsync("SELECT blockeruserid AS uid FROM blocks WHERE " + Scope() + " AND blockeduserid = " + D.Str(blockedUserId) + ";", token);
        }

        public Task<HashSet<string>> RelatedAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            return IdsAsync("SELECT blockeduserid AS uid FROM blocks WHERE " + Scope() + " AND blockeruserid = " + D.Str(userId) +
                " UNION SELECT blockeruserid AS uid FROM blocks WHERE " + Scope() + " AND blockeduserid = " + D.Str(userId) + ";", token);
        }

        public Task<HashSet<string>> RelatedAmongAsync(string userId, IEnumerable<string> otherUserIds, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            ArgumentNullException.ThrowIfNull(otherUserIds);
            List<string> others = otherUserIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (others.Count == 0) return Task.FromResult(new HashSet<string>(StringComparer.Ordinal));
            string list = D.InList(others);
            return IdsAsync("SELECT blockeduserid AS uid FROM blocks WHERE " + Scope() + " AND blockeruserid = " + D.Str(userId) + " AND blockeduserid IN " + list +
                " UNION SELECT blockeruserid AS uid FROM blocks WHERE " + Scope() + " AND blockeduserid = " + D.Str(userId) + " AND blockeruserid IN " + list + ";", token);
        }

        public Task<HashSet<string>> HiddenAuthorsAsync(string viewerUserId, CancellationToken token = default)
        {
            RequireText(viewerUserId, nameof(viewerUserId));
            return IdsAsync("SELECT b.blockeduserid AS uid FROM blocks b WHERE " + Scope("b") + " AND b.blockeruserid = " + D.Str(viewerUserId) +
                " AND NOT EXISTS (SELECT 1 FROM roleassignments r WHERE " + Scope("r") + " AND r.userid = b.blockeduserid AND r.role IN " + D.InList(_StaffRoles) + ");", token);
        }

        private async Task<HashSet<string>> IdsAsync(string sql, CancellationToken token)
        {
            DataTable table = await QueryAsync(sql, token).ConfigureAwait(false);
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            foreach (DataRow row in table.Rows) result.Add(RowReader.GetString(row, "uid"));
            return result;
        }
    }
}
