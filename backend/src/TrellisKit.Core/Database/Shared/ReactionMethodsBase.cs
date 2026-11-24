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
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Shared reaction and reaction count methods. Query alias r (reactions).
    /// </summary>
    internal abstract class ReactionMethodsBase : MethodsBase, IReactionMethods
    {
        protected ReactionMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<bool> AddAsync(TargetTypeEnum targetType, string targetId, string userId, string key, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(targetId, nameof(targetId));
            RequireText(userId, nameof(userId));
            RequireText(key, nameof(key));
            string id = IdGenerator.Generate(Constants.ReactionIdPrefix);
            List<string> statements = new List<string>();
            AddLock(statements, targetType, targetId);
            statements.Add("INSERT INTO reactions (propertyid, id, targettype, targetid, userid, reactionkey, createdutc) VALUES (" + Pid + ", " + D.Str(id) + ", " + D.Enum(targetType) + ", " +
                D.Str(targetId) + ", " + D.Str(userId) + ", " + D.Str(key) + ", " + D.Ts(nowUtc) + ") ON CONFLICT (propertyid, targettype, targetid, userid, reactionkey) DO NOTHING;");
            AddRecompute(statements, targetType, targetId, new[] { key });
            statements.Add("SELECT id FROM reactions WHERE " + Scope() + " AND id = " + D.Str(id) + ";");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task RemoveAsync(TargetTypeEnum targetType, string targetId, string userId, string key, CancellationToken token = default)
        {
            RequireText(targetId, nameof(targetId));
            RequireText(userId, nameof(userId));
            RequireText(key, nameof(key));
            string where = Scope() + " AND targettype = " + D.Enum(targetType) + " AND targetid = " + D.Str(targetId) + " AND userid = " + D.Str(userId) + " AND reactionkey = " + D.Str(key);
            List<string> statements = new List<string>();
            AddLock(statements, targetType, targetId);
            statements.AddRange(Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.Reaction, "SELECT id FROM reactions WHERE " + where));
            statements.Add("DELETE FROM reactions WHERE " + where + ";");
            AddRecompute(statements, targetType, targetId, new[] { key });
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        public async Task RemoveAllForUserOnTargetAsync(TargetTypeEnum targetType, string targetId, string userId, CancellationToken token = default)
        {
            RequireText(targetId, nameof(targetId));
            RequireText(userId, nameof(userId));
            string where = Scope() + " AND targettype = " + D.Enum(targetType) + " AND targetid = " + D.Str(targetId) + " AND userid = " + D.Str(userId);
            DataTable keys = await QueryAsync("SELECT DISTINCT reactionkey FROM reactions WHERE " + where + ";", token).ConfigureAwait(false);
            List<string> keyList = keys.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "reactionkey")).ToList();
            List<string> statements = new List<string>();
            AddLock(statements, targetType, targetId);
            statements.AddRange(Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.Reaction, "SELECT id FROM reactions WHERE " + where));
            statements.Add("DELETE FROM reactions WHERE " + where + ";");
            AddRecompute(statements, targetType, targetId, keyList);
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        public async Task<Dictionary<string, Dictionary<string, long>>> CountsAsync(TargetTypeEnum targetType, IEnumerable<string> targetIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(targetIds);
            Dictionary<string, Dictionary<string, long>> result = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);
            List<string> ids = targetIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT targetid, reactionkey, count FROM reactioncounts WHERE " + Scope() + " AND targettype = " + D.Enum(targetType) + " AND targetid IN " + D.InList(ids) +
                " AND count > 0;", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows)
            {
                string target = RowReader.GetString(row, "targetid");
                if (!result.TryGetValue(target, out Dictionary<string, long>? counts))
                {
                    counts = new Dictionary<string, long>(StringComparer.Ordinal);
                    result[target] = counts;
                }
                counts[RowReader.GetString(row, "reactionkey")] = RowReader.GetLong(row, "count");
            }
            return result;
        }

        public async Task<Dictionary<string, List<string>>> MineAsync(TargetTypeEnum targetType, IEnumerable<string> targetIds, string userId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(targetIds);
            Dictionary<string, List<string>> result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            List<string> ids = targetIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0 || String.IsNullOrEmpty(userId)) return result;
            DataTable table = await QueryAsync("SELECT targetid, reactionkey FROM reactions WHERE " + Scope() + " AND targettype = " + D.Enum(targetType) + " AND targetid IN " + D.InList(ids) +
                " AND userid = " + D.Str(userId) + " ORDER BY createdutc, id;", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows)
            {
                string target = RowReader.GetString(row, "targetid");
                if (!result.TryGetValue(target, out List<string>? keys))
                {
                    keys = new List<string>();
                    result[target] = keys;
                }
                keys.Add(RowReader.GetString(row, "reactionkey"));
            }
            return result;
        }

        public Task<EnumerationResult<Reaction>> EnumerateAsync(TargetTypeEnum targetType, string targetId, EnumerationRequest request, string? key, IReadOnlyCollection<string> allowedKeys, IReadOnlyCollection<string> excludedUserIds, CancellationToken token = default)
        {
            RequireText(targetId, nameof(targetId));
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(allowedKeys);
            ArgumentNullException.ThrowIfNull(excludedUserIds);
            List<string> conditions = new List<string>
            {
                "r.targettype = " + D.Enum(targetType),
                "r.targetid = " + D.Str(targetId),
                allowedKeys.Count == 0 ? "1 = 0" : "r.reactionkey IN " + D.InList(allowedKeys)
            };
            if (!String.IsNullOrEmpty(key)) conditions.Add("r.reactionkey = " + D.Str(key));
            if (excludedUserIds.Count > 0) conditions.Add("r.userid NOT IN " + D.InList(excludedUserIds));
            return EnumerateAsync(request, "r.*", "reactions r", conditions, Map, token);
        }

        public async Task<List<Reaction>> ListByUserAsync(string userId, string? afterId, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            List<string> conditions = new List<string> { Scope("r"), "r.userid = " + D.Str(userId) };
            if (!String.IsNullOrEmpty(afterId)) conditions.Add("r.id > " + D.Str(afterId));
            DataTable table = await QueryAsync("SELECT r.* FROM reactions r" + Where(conditions) + " ORDER BY r.id ASC LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public Task<long> CountRowsAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            RequireText(targetId, nameof(targetId));
            return CountAsync("SELECT COUNT(*) AS cnt FROM reactions WHERE " + Scope() + " AND targettype = " + D.Enum(targetType) + " AND targetid = " + D.Str(targetId) + ";", token);
        }

        private void AddLock(List<string> statements, TargetTypeEnum targetType, string targetId)
        {
            string? table = TableFor(targetType);
            if (table != null) statements.Add("UPDATE " + table + " SET reactiontotal = reactiontotal WHERE " + Scope() + " AND id = " + D.Str(targetId) + ";");
        }

        private void AddRecompute(List<string> statements, TargetTypeEnum targetType, string targetId, IEnumerable<string> keys)
        {
            string type = D.Enum(targetType);
            string target = D.Str(targetId);
            foreach (string key in keys.Distinct(StringComparer.Ordinal))
            {
                statements.Add("INSERT INTO reactioncounts (propertyid, targettype, targetid, reactionkey, count) VALUES (" + Pid + ", " + type + ", " + target + ", " + D.Str(key) +
                    ", (SELECT COUNT(*) FROM reactions x WHERE " + Scope("x") + " AND x.targettype = " + type + " AND x.targetid = " + target + " AND x.reactionkey = " + D.Str(key) + "))" +
                    " ON CONFLICT (targettype, targetid, reactionkey) DO UPDATE SET count = excluded.count;");
            }
            string total = "(SELECT COALESCE(SUM(rc.count), 0) FROM reactioncounts rc WHERE " + Scope("rc") + " AND rc.targettype = " + type + " AND rc.targetid = " + target + ")";
            if (targetType == TargetTypeEnum.FeedPost)
            {
                statements.Add("UPDATE feedposts SET reactiontotal = " + total + " WHERE " + Scope() + " AND id = " + target + ";");
                // A feed post's discussion thread shares its reactions (Phase 3 section 8.2).
                statements.Add("UPDATE forumthreads SET reactiontotal = " + total + " WHERE " + Scope() + " AND sourcetype = " + D.Enum(TargetTypeEnum.FeedPost) + " AND sourceid = " + target + ";");
            }
            else if (targetType == TargetTypeEnum.Thread)
                statements.Add("UPDATE forumthreads SET reactiontotal = " + total + " WHERE " + Scope() + " AND id = " + target + ";");
            else if (targetType == TargetTypeEnum.Idea)
                statements.Add("UPDATE ideas SET reactiontotal = " + total + " WHERE " + Scope() + " AND id = " + target + ";");
            else if (targetType == TargetTypeEnum.Comment)
                statements.Add("UPDATE comments SET reactiontotal = " + total + ", score = " + total + " + 2 * childcount WHERE " + Scope() + " AND id = " + target + ";");
        }

        private static string? TableFor(TargetTypeEnum targetType)
        {
            if (targetType == TargetTypeEnum.FeedPost) return "feedposts";
            if (targetType == TargetTypeEnum.Comment) return "comments";
            if (targetType == TargetTypeEnum.Thread) return "forumthreads";
            if (targetType == TargetTypeEnum.Idea) return "ideas";
            return null;
        }

        private static Reaction Map(DataRow row)
        {
            return new Reaction
            {
                Id = RowReader.GetString(row, "id"),
                TargetType = RowReader.GetEnum(row, "targettype", TargetTypeEnum.FeedPost),
                TargetId = RowReader.GetString(row, "targetid"),
                UserId = RowReader.GetString(row, "userid"),
                Key = RowReader.GetString(row, "reactionkey"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }
    }
}
