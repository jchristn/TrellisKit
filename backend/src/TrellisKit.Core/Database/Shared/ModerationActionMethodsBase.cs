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
    /// Shared moderation action methods.
    /// </summary>
    internal abstract class ModerationActionMethodsBase : ModerationMethodsBase, IModerationActionMethods
    {
        protected ModerationActionMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<ModerationAction> CreateAsync(ModerationAction action, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(action);
            RequireText(action.Id, nameof(action.Id));
            RequireText(action.TargetId, nameof(action.TargetId));
            RequireText(action.Reason, nameof(action.Reason));
            await QueryAsync("INSERT INTO moderationactions (propertyid, id, moderatoruserid, targettype, targetid, targetauthoruserid, action, reason, note, notifiedauthor, breakglass, fromforumid, toforumid, createdutc) VALUES (" +
                Pid + ", " + D.Str(action.Id) + ", " + D.Str(action.ModeratorUserId) + ", " + D.Enum(action.TargetType) + ", " + D.Str(action.TargetId) + ", " + D.Str(action.TargetAuthorUserId) + ", " +
                D.Enum(action.Action) + ", " + D.Str(action.Reason) + ", " + D.Str(action.Note) + ", " + D.Bool(action.NotifiedAuthor) + ", " + D.Bool(action.BreakGlass) + ", " + D.Str(action.FromForumId) + ", " + D.Str(action.ToForumId) + ", " + D.Ts(action.CreatedUtc) + ");", token).ConfigureAwait(false);
            return await ReadAsync(action.Id, token).ConfigureAwait(false) ?? action;
        }

        public async Task<ModerationAction?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            DataTable table = await QueryAsync("SELECT * FROM moderationactions WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public Task<EnumerationResult<ModerationAction>> EnumerateAsync(EnumerationRequest request, ModerationActionFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string>();
            if (filter.SystemOnly) conditions.Add("a.moderatoruserid IS NULL AND a.breakglass = " + D.Bool(false));
            else if (!String.IsNullOrEmpty(filter.ModeratorUserId)) conditions.Add("a.moderatoruserid = " + D.Str(filter.ModeratorUserId));
            if (filter.TargetType != null) conditions.Add("a.targettype = " + D.Enum(filter.TargetType.Value));
            if (!String.IsNullOrEmpty(filter.TargetId)) conditions.Add("a.targetid = " + D.Str(filter.TargetId));
            if (!String.IsNullOrEmpty(filter.TargetAuthorUserId)) conditions.Add("a.targetauthoruserid = " + D.Str(filter.TargetAuthorUserId));
            if (filter.Action != null) conditions.Add("a.action = " + D.Enum(filter.Action.Value));
            return EnumerateAsync(request, "a.*", "moderationactions a", conditions, Map, token);
        }

        public Task<long> CountByAuthorAsync(string authorUserId, IEnumerable<ModerationActionTypeEnum> actions, DateTime sinceUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actions);
            List<string> kinds = actions.Select(a => a.ToString()).Distinct(StringComparer.Ordinal).ToList();
            if (String.IsNullOrEmpty(authorUserId) || kinds.Count == 0) return Task.FromResult(0L);
            return CountAsync("SELECT COUNT(*) AS cnt FROM moderationactions WHERE " + Scope() + " AND targetauthoruserid = " + D.Str(authorUserId) + " AND action IN " + D.InList(kinds) +
                " AND createdutc >= " + D.Ts(sinceUtc) + ";", token);
        }

        public Task<EnumerationResult<ModerationContentRow>> EnumerateContentAsync(EnumerationRequest request, ModerationContentFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            RequireText(filter.AuthorUserId, nameof(filter.AuthorUserId));
            string author = D.Str(filter.AuthorUserId);
            List<string> branches = new List<string>();
            if (filter.TargetType == null || filter.TargetType == TargetTypeEnum.FeedPost)
            {
                branches.Add("SELECT " + D.Enum(TargetTypeEnum.FeedPost) + " AS targettype, f.propertyid AS propertyid, f.id AS id, f.title AS title, f.body AS body, f.createdutc AS createdutc, f.reactiontotal AS reactiontotal, " +
                    "f.moderationstate AS moderationstate, CASE WHEN f.deletedutc IS NULL THEN 0 ELSE 1 END AS isdeleted, " +
                    "(SELECT COUNT(*) FROM reports rc WHERE " + Scope("rc") + " AND rc.targettype = " + D.Enum(TargetTypeEnum.FeedPost) + " AND rc.targetid = f.id) AS reportcount " +
                    "FROM feedposts f WHERE " + Scope("f") + " AND f.authoruserid = " + author);
            }
            if (filter.TargetType == null || filter.TargetType == TargetTypeEnum.Comment)
            {
                branches.Add("SELECT " + D.Enum(TargetTypeEnum.Comment) + " AS targettype, c.propertyid AS propertyid, c.id AS id, NULL AS title, c.body AS body, c.createdutc AS createdutc, c.reactiontotal AS reactiontotal, " +
                    "c.moderationstate AS moderationstate, CASE WHEN c.deletedutc IS NULL THEN 0 ELSE 1 END AS isdeleted, " +
                    "(SELECT COUNT(*) FROM reports rc WHERE " + Scope("rc") + " AND rc.targettype = " + D.Enum(TargetTypeEnum.Comment) + " AND rc.targetid = c.id) AS reportcount " +
                    "FROM comments c WHERE " + Scope("c") + " AND c.authoruserid = " + author);
            }
            string from = "(" + String.Join(" UNION ALL ", branches) + ") x";
            List<string> conditions = new List<string>();
            switch ((filter.State ?? String.Empty).ToLowerInvariant())
            {
                case "visible":
                    conditions.Add("x.isdeleted = " + D.Num(0));
                    conditions.Add("x.moderationstate = " + D.Enum(ModerationStateEnum.None));
                    break;
                case "hidden":
                    conditions.Add("x.isdeleted = " + D.Num(0));
                    conditions.Add("x.moderationstate = " + D.Enum(ModerationStateEnum.Hidden));
                    break;
                case "removed":
                    conditions.Add("x.isdeleted = " + D.Num(0));
                    conditions.Add("x.moderationstate = " + D.Enum(ModerationStateEnum.Removed));
                    break;
                case "deleted":
                    conditions.Add("x.isdeleted = " + D.Num(1));
                    break;
            }
            conditions.AddRange(TargetMetadataConditions(request.Query, "x.targettype", "x.id"));
            return EnumerateAsync(WithoutMetadata(request), "x.targettype, x.id, x.createdutc, x.reportcount", from, conditions, row => new ModerationContentRow
            {
                TargetType = RowReader.GetEnum(row, "targettype", TargetTypeEnum.FeedPost),
                TargetId = RowReader.GetString(row, "id"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                ReportCount = RowReader.GetLong(row, "reportcount")
            }, token);
        }

        public Task<EnumerationResult<string>> EnumerateModeratorsAsync(EnumerationRequest request, ModeratorFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> roleConditions = new List<string> { Scope("ra"), "ra.userid = u.id" };
            if (filter.Role != null) roleConditions.Add("ra.role = " + D.Enum(filter.Role.Value));
            else roleConditions.Add("ra.role IN " + D.InList(new[] { RoleEnum.Moderator.ToString(), RoleEnum.FeedModerator.ToString(), RoleEnum.DirectoryModerator.ToString(), RoleEnum.EventManager.ToString(), RoleEnum.LibraryEditor.ToString(), RoleEnum.RankAwarder.ToString() }));
            if (filter.ResourceType != null) roleConditions.Add("ra.resourcetype = " + D.Enum(filter.ResourceType.Value));
            List<string> conditions = new List<string>
            {
                "u.isdeleted = " + D.Bool(false),
                "EXISTS (SELECT 1 FROM roleassignments ra WHERE " + String.Join(" AND ", roleConditions) + ")"
            };
            return EnumerateAsync(request, "u.id", "users u", conditions, row => RowReader.GetString(row, "id"), token);
        }

        public async Task<Dictionary<string, ModeratorStats>> ModeratorStatsAsync(IEnumerable<string> userIds, DateTime sinceUtc, int maxSamples, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(userIds);
            List<string> ids = userIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            Dictionary<string, ModeratorStats> result = ids.ToDictionary(i => i, i => new ModeratorStats { UserId = i }, StringComparer.Ordinal);
            if (ids.Count == 0) return result;
            string inList = D.InList(ids);
            string since = D.Ts(sinceUtc);
            DataTable actions = await QueryAsync("SELECT moderatoruserid AS uid, COUNT(*) AS cnt FROM moderationactions WHERE " + Scope() + " AND moderatoruserid IN " + inList +
                " AND createdutc >= " + since + " GROUP BY moderatoruserid;", token).ConfigureAwait(false);
            foreach (DataRow row in actions.Rows) result[RowReader.GetString(row, "uid")].Actions = RowReader.GetLong(row, "cnt");
            DataTable overdue = await QueryAsync("SELECT resolvedbyuserid AS uid, COUNT(*) AS cnt FROM reports WHERE " + Scope() + " AND resolvedbyuserid IN " + inList +
                " AND resolvedutc >= " + since + " AND resolvedutc > dueutc GROUP BY resolvedbyuserid;", token).ConfigureAwait(false);
            foreach (DataRow row in overdue.Rows) result[RowReader.GetString(row, "uid")].OverdueHandled = RowReader.GetLong(row, "cnt");
            DataTable samples = await QueryAsync("SELECT s.uid, s.createdutc, s.resolvedutc FROM (SELECT resolvedbyuserid AS uid, createdutc, resolvedutc, " +
                "ROW_NUMBER() OVER (PARTITION BY resolvedbyuserid ORDER BY resolvedutc DESC, id DESC) AS rn FROM reports WHERE " + Scope() + " AND resolvedbyuserid IN " + inList +
                " AND resolvedutc >= " + since + ") s WHERE s.rn <= " + D.Num(Math.Clamp(maxSamples, 1, 10000)) + ";", token).ConfigureAwait(false);
            foreach (IGrouping<string, DataRow> group in samples.Rows.Cast<DataRow>().GroupBy(r => RowReader.GetString(r, "uid")))
            {
                List<double> minutes = group.Select(r => (RowReader.GetDateTime(r, "resolvedutc") - RowReader.GetDateTime(r, "createdutc")).TotalMinutes).OrderBy(m => m).ToList();
                if (minutes.Count == 0) continue;
                double median = minutes.Count % 2 == 1 ? minutes[minutes.Count / 2] : (minutes[minutes.Count / 2 - 1] + minutes[minutes.Count / 2]) / 2.0;
                result[group.Key].MedianResponseMinutes = Math.Round(Math.Max(0, median), 1);
            }
            return result;
        }

        public async Task<List<PurgeCandidate>> ListPurgeCandidatesAsync(DateTime cutoffUtc, int limit, CancellationToken token = default)
        {
            List<PurgeCandidate> result = new List<PurgeCandidate>();
            string cutoff = D.Ts(cutoffUtc);
            string open = D.Enum(ReportStatusEnum.Open);
            string removed = D.Enum(ModerationStateEnum.Removed);
            int size = Math.Max(1, limit);
            foreach (TargetTypeEnum type in new[] { TargetTypeEnum.FeedPost, TargetTypeEnum.Comment, TargetTypeEnum.Thread, TargetTypeEnum.Idea })
            {
                string table = type == TargetTypeEnum.FeedPost ? "feedposts" : type == TargetTypeEnum.Thread ? "forumthreads" : type == TargetTypeEnum.Idea ? "ideas" : "comments";
                DataTable rows = await QueryAsync("SELECT t.id FROM " + table + " t WHERE " + Scope("t") + " AND t.purgedutc IS NULL AND ((t.deletedutc IS NOT NULL AND t.deletedutc < " + cutoff + ") OR " +
                    "(t.deletedutc IS NULL AND t.moderationstate = " + removed + " AND t.moderatedutc < " + cutoff + ")) AND NOT EXISTS (SELECT 1 FROM reports r WHERE " + Scope("r") + " AND r.targettype = " +
                    D.Enum(type) + " AND r.targetid = t.id AND r.status = " + open + ") ORDER BY t.id LIMIT " + D.Num(size) + ";", token).ConfigureAwait(false);
                result.AddRange(rows.Rows.Cast<DataRow>().Select(r => new PurgeCandidate { TargetType = type, TargetId = RowReader.GetString(r, "id") }));
            }
            return result;
        }

        private static ModerationAction Map(DataRow row)
        {
            return new ModerationAction
            {
                Id = RowReader.GetString(row, "id"),
                ModeratorUserId = RowReader.GetNullableString(row, "moderatoruserid"),
                TargetType = RowReader.GetEnum(row, "targettype", TargetTypeEnum.FeedPost),
                TargetId = RowReader.GetString(row, "targetid"),
                TargetAuthorUserId = RowReader.GetNullableString(row, "targetauthoruserid"),
                Action = RowReader.GetEnum(row, "action", ModerationActionTypeEnum.Hide),
                Reason = RowReader.GetString(row, "reason"),
                Note = RowReader.GetNullableString(row, "note"),
                NotifiedAuthor = RowReader.GetBool(row, "notifiedauthor"),
                BreakGlass = RowReader.GetBool(row, "breakglass"),
                FromForumId = row.Table.Columns.Contains("fromforumid") ? RowReader.GetNullableString(row, "fromforumid") : null,
                ToForumId = row.Table.Columns.Contains("toforumid") ? RowReader.GetNullableString(row, "toforumid") : null,
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }
    }
}
