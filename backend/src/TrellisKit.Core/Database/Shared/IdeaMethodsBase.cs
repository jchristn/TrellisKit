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

    internal abstract class IdeaMethodsBase : MethodsBase, IIdeaMethods
    {
        private const int _MaxStatuses = 200;

        protected IdeaMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<List<IdeaStatus>> ListStatusesAsync(bool includeRetired, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT s.* FROM ideastatuses s WHERE " + Scope("s") + (includeRetired ? String.Empty : " AND s.isretired = " + D.Bool(false)) +
                " ORDER BY s.sortorder, s.id LIMIT " + D.Num(_MaxStatuses) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapStatus).ToList();
        }

        public Task<EnumerationResult<IdeaStatus>> EnumerateStatusesAsync(EnumerationRequest request, bool includeRetired, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string>();
            if (!includeRetired) conditions.Add("s.isretired = " + D.Bool(false));
            return EnumerateAsync(request, "s.*", "ideastatuses s", conditions, MapStatus, token);
        }

        public async Task<IdeaStatus?> ReadStatusAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            DataTable table = await QueryAsync("SELECT s.* FROM ideastatuses s WHERE " + Scope("s") + " AND s.id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MapStatus(table.Rows[0]);
        }

        public Task<long> CountStatusesAsync(CancellationToken token = default)
        {
            return CountAsync("SELECT COUNT(*) AS cnt FROM ideastatuses WHERE " + Scope() + ";", token);
        }

        public async Task<IdeaStatus> CreateStatusAsync(IdeaStatus status, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(status);
            RequireText(status.Id, nameof(status.Id));
            RequireText(status.Key, nameof(status.Key));
            List<string> statements = new List<string>();
            if (status.IsInitial) statements.Add("UPDATE ideastatuses SET isinitial = " + D.Bool(false) + ", updatedutc = " + D.Ts(status.UpdatedUtc) + " WHERE " + Scope() + " AND isinitial = " + D.Bool(true) + ";");
            statements.Add("INSERT INTO ideastatuses (propertyid, id, key, label, color, isclosed, isinitial, sortorder, isretired, createdutc, updatedutc) VALUES (" + Pid + ", " + D.Str(status.Id) + ", " +
                D.Str(status.Key) + ", " + D.Str(status.Label) + ", " + D.Str(status.Color) + ", " + D.Bool(status.IsClosed) + ", " + D.Bool(status.IsInitial) + ", " + D.Num(status.SortOrder) + ", " +
                D.Bool(status.IsRetired) + ", " + D.Ts(status.CreatedUtc) + ", " + D.Ts(status.UpdatedUtc) + ");");
            await UniqueTransactionAsync(statements, "An idea status with this key", token).ConfigureAwait(false);
            return (await ReadStatusAsync(status.Id, token).ConfigureAwait(false))!;
        }

        public async Task<IdeaStatus?> UpdateStatusAsync(IdeaStatus status, string previousKey, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(status);
            RequireText(status.Id, nameof(status.Id));
            RequireText(status.Key, nameof(status.Key));
            List<string> statements = new List<string>();
            if (status.IsInitial) statements.Add("UPDATE ideastatuses SET isinitial = " + D.Bool(false) + ", updatedutc = " + D.Ts(status.UpdatedUtc) + " WHERE " + Scope() + " AND isinitial = " + D.Bool(true) +
                " AND id <> " + D.Str(status.Id) + ";");
            statements.Add("UPDATE ideastatuses SET key = " + D.Str(status.Key) + ", label = " + D.Str(status.Label) + ", color = " + D.Str(status.Color) + ", isclosed = " + D.Bool(status.IsClosed) +
                ", isinitial = " + D.Bool(status.IsInitial) + ", isretired = " + D.Bool(status.IsRetired) + ", updatedutc = " + D.Ts(status.UpdatedUtc) + " WHERE " + Scope() + " AND id = " + D.Str(status.Id) + ";");
            if (!String.IsNullOrEmpty(previousKey) && !String.Equals(previousKey, status.Key, StringComparison.Ordinal))
                statements.Add("UPDATE ideas SET statuskey = " + D.Str(status.Key) + " WHERE " + Scope() + " AND statuskey = " + D.Str(previousKey) + ";");
            statements.Add("SELECT id FROM ideastatuses WHERE " + Scope() + " AND id = " + D.Str(status.Id) + ";");
            DataTable table = await UniqueTransactionAsync(statements, "An idea status with this key", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return await ReadStatusAsync(status.Id, token).ConfigureAwait(false);
        }

        public async Task<int> ReorderStatusesAsync(List<string> ids, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            if (ids.Count == 0) return 0;
            List<string> statements = new List<string>();
            int order = 10;
            foreach (string id in ids)
            {
                statements.Add("UPDATE ideastatuses SET sortorder = " + D.Num(order) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + ";");
                order += 10;
            }
            statements.Add("SELECT COUNT(*) AS cnt FROM ideastatuses WHERE " + Scope() + " AND id IN " + D.InList(ids) + ";");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? 0 : (int)RowReader.GetLong(table.Rows[0], "cnt");
        }

        public Task<long> CountWithStatusAsync(string key, CancellationToken token = default)
        {
            RequireText(key, nameof(key));
            return CountAsync("SELECT COUNT(*) AS cnt FROM ideas WHERE " + Scope() + " AND statuskey = " + D.Str(key) + " AND deletedutc IS NULL;", token);
        }

        public async Task<Idea> CreateAsync(Idea idea, string? voterUserId, IdeaFollowSourceEnum followSource, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(idea);
            RequireText(idea.Id, nameof(idea.Id));
            RequireText(idea.StatusKey, nameof(idea.StatusKey));
            string id = D.Str(idea.Id);
            List<string> statements = new List<string>
            {
                "INSERT INTO ideas (propertyid, id, title, body, authoruserid, showauthor, statuskey, statusnote, statusutc, votecount, recentvotecount, mergedintoid, sourceticketid, audience, " +
                "commentcount, reactiontotal, openreportcount, moderationstate, createdbyuserid, createdutc, updatedutc) VALUES (" + Pid + ", " + id + ", " + D.Str(idea.Title) + ", " + D.Str(idea.Body) + ", " +
                D.Str(idea.AuthorUserId) + ", " + D.Bool(idea.ShowAuthor) + ", " + D.Str(idea.StatusKey) + ", " + D.Str(idea.StatusNote) + ", " + D.Ts(idea.StatusUtc) + ", 0, 0, NULL, " +
                D.Str(idea.SourceTicketId) + ", " + D.Enum(idea.Audience) + ", 0, 0, 0, " + D.Enum(ModerationStateEnum.None) + ", " + D.Str(idea.CreatedByUserId) + ", " + D.Ts(idea.CreatedUtc) + ", " + D.Ts(idea.UpdatedUtc) + ");"
            };
            if (!String.IsNullOrEmpty(voterUserId))
            {
                statements.Add(VoteInsert(idea.Id, voterUserId, idea.CreatedUtc));
                statements.Add(FollowInsert(idea.Id, voterUserId, followSource, idea.CreatedUtc));
                statements.Add(RecomputeVotes(id));
            }
            await TransactionAsync(statements, token).ConfigureAwait(false);
            return (await ReadAsync(idea.Id, token).ConfigureAwait(false))!;
        }

        public async Task<Idea?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            DataTable table = await QueryAsync("SELECT i.* FROM ideas i WHERE " + Scope("i") + " AND i.id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<List<Idea>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            List<string> list = ids.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (list.Count == 0) return new List<Idea>();
            DataTable table = await QueryAsync("SELECT i.* FROM ideas i WHERE " + Scope("i") + " AND i.id IN " + D.InList(list) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<Idea?> UpdateAsync(Idea idea, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(idea);
            RequireText(idea.Id, nameof(idea.Id));
            DataTable table = await QueryAsync("UPDATE ideas SET title = " + D.Str(idea.Title) + ", body = " + D.Str(idea.Body) + ", audience = " + D.Enum(idea.Audience) + ", showauthor = " + D.Bool(idea.ShowAuthor) +
                ", editedutc = " + D.Ts(idea.EditedUtc) + ", updatedutc = " + D.Ts(idea.UpdatedUtc) + " WHERE " + Scope() + " AND id = " + D.Str(idea.Id) + " RETURNING id;", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return await ReadAsync(idea.Id, token).ConfigureAwait(false);
        }

        public async Task<Idea?> SetStatusAsync(string id, string statusKey, string? statusNote, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            RequireText(statusKey, nameof(statusKey));
            DataTable table = await QueryAsync("UPDATE ideas SET statuskey = " + D.Str(statusKey) + ", statusnote = " + D.Str(statusNote) + ", statusutc = " + D.Ts(nowUtc) + ", updatedutc = " + D.Ts(nowUtc) +
                " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND deletedutc IS NULL RETURNING id;", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return await ReadAsync(id, token).ConfigureAwait(false);
        }

        public Task<EnumerationResult<Idea>> EnumerateAsync(EnumerationRequest request, IdeaFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = Conditions(filter);
            if (String.Equals(request.Ordering.Name, "Relevance", StringComparison.Ordinal) && !String.IsNullOrWhiteSpace(filter.RelevanceText))
            {
                string text = D.Str(filter.RelevanceText.Trim().ToLowerInvariant());
                EnumerationRequest ranked = new EnumerationRequest
                {
                    Collection = request.Collection,
                    Query = request.Query,
                    MaxResults = request.MaxResults,
                    Skip = request.Skip,
                    Ordering = new EnumerationOrdering { Name = "Relevance", Expression = "(CASE WHEN strpos(lower(i.title), " + text + ") > 0 THEN 1000000 ELSE 0 END + i.votecount)", Descending = true, KeyKind = EnumerationKeyKindEnum.Number },
                    AfterKey = request.AfterKey,
                    AfterId = request.AfterId,
                    Fingerprint = request.Fingerprint,
                    CountTotals = request.CountTotals,
                    IncludeStaffMetadata = request.IncludeStaffMetadata
                };
                return EnumerateAsync(ranked, "i.*", "ideas i", conditions, Map, token);
            }
            return EnumerateAsync(request, "i.*", "ideas i", conditions, Map, token);
        }

        public async Task<bool> VoteAsync(string ideaId, string userId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(ideaId, nameof(ideaId));
            RequireText(userId, nameof(userId));
            DataTable added = await QueryAsync(VoteInsert(ideaId, userId, nowUtc).TrimEnd(';') + " RETURNING id;", token).ConfigureAwait(false);
            await TransactionAsync(new List<string> { FollowInsert(ideaId, userId, IdeaFollowSourceEnum.Vote, nowUtc), RecomputeVotes(D.Str(ideaId)) }, token).ConfigureAwait(false);
            return added.Rows.Count > 0;
        }

        public async Task<bool> UnvoteAsync(string ideaId, string userId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(ideaId, nameof(ideaId));
            RequireText(userId, nameof(userId));
            DataTable removed = await QueryAsync("DELETE FROM ideavotes WHERE " + Scope() + " AND ideaid = " + D.Str(ideaId) + " AND userid = " + D.Str(userId) + " RETURNING id;", token).ConfigureAwait(false);
            await QueryAsync(RecomputeVotes(D.Str(ideaId)), token).ConfigureAwait(false);
            return removed.Rows.Count > 0;
        }

        public async Task<HashSet<string>> VotedAmongAsync(string userId, IEnumerable<string> ideaIds, CancellationToken token = default)
        {
            return await AmongAsync("ideavotes", userId, ideaIds, token).ConfigureAwait(false);
        }

        public async Task<bool> FollowAsync(string ideaId, string userId, IdeaFollowSourceEnum source, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(ideaId, nameof(ideaId));
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync(FollowInsert(ideaId, userId, source, nowUtc).TrimEnd(';') + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> UnfollowAsync(string ideaId, string userId, CancellationToken token = default)
        {
            RequireText(ideaId, nameof(ideaId));
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("DELETE FROM ideafollows WHERE " + Scope() + " AND ideaid = " + D.Str(ideaId) + " AND userid = " + D.Str(userId) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public Task<HashSet<string>> FollowedAmongAsync(string userId, IEnumerable<string> ideaIds, CancellationToken token = default)
        {
            return AmongAsync("ideafollows", userId, ideaIds, token);
        }

        public async Task<List<string>> RecipientIdsAsync(string ideaId, string? afterUserId, int limit, CancellationToken token = default)
        {
            RequireText(ideaId, nameof(ideaId));
            string idea = D.Str(ideaId);
            string sql = "SELECT u.userid FROM (SELECT v.userid FROM ideavotes v WHERE " + Scope("v") + " AND v.ideaid = " + idea +
                " UNION SELECT f.userid FROM ideafollows f WHERE " + Scope("f") + " AND f.ideaid = " + idea + ") u" +
                (String.IsNullOrEmpty(afterUserId) ? String.Empty : " WHERE u.userid > " + D.Str(afterUserId)) + " ORDER BY u.userid LIMIT " + D.Num(Math.Max(1, limit)) + ";";
            DataTable table = await QueryAsync(sql, token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "userid")).ToList();
        }

        public async Task<List<string>> VoterIdsAsync(string ideaId, string? afterUserId, int limit, CancellationToken token = default)
        {
            RequireText(ideaId, nameof(ideaId));
            DataTable table = await QueryAsync("SELECT v.userid FROM ideavotes v WHERE " + Scope("v") + " AND v.ideaid = " + D.Str(ideaId) +
                (String.IsNullOrEmpty(afterUserId) ? String.Empty : " AND v.userid > " + D.Str(afterUserId)) + " ORDER BY v.userid LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "userid")).ToList();
        }

        public async Task<HashSet<string>> VotersAmongAsync(string ideaId, IEnumerable<string> userIds, CancellationToken token = default)
        {
            RequireText(ideaId, nameof(ideaId));
            ArgumentNullException.ThrowIfNull(userIds);
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            List<string> ids = userIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return result;
            DataTable rows = await QueryAsync("SELECT v.userid FROM ideavotes v WHERE " + Scope("v") + " AND v.ideaid = " + D.Str(ideaId) + " AND v.userid IN " + D.InList(ids) + ";", token).ConfigureAwait(false);
            foreach (DataRow row in rows.Rows) result.Add(RowReader.GetString(row, "userid"));
            return result;
        }

        public async Task<IdeaVoterCounts> VoterCountsAsync(string ideaId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(ideaId, nameof(ideaId));
            DataTable table = await QueryAsync("SELECT COUNT(*) AS total, COUNT(*) FILTER (WHERE " + ActiveEntitlementExists("v.userid", nowUtc, null) + ") AS members FROM ideavotes v WHERE " +
                Scope("v") + " AND v.ideaid = " + D.Str(ideaId) + ";", token).ConfigureAwait(false);
            long total = table.Rows.Count == 0 ? 0 : RowReader.GetLong(table.Rows[0], "total");
            long members = table.Rows.Count == 0 ? 0 : RowReader.GetLong(table.Rows[0], "members");
            return new IdeaVoterCounts { Total = total, Members = members, NonMembers = total - members };
        }

        public async Task<bool> MergeAsync(string sourceId, string targetId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(sourceId, nameof(sourceId));
            RequireText(targetId, nameof(targetId));
            string source = D.Str(sourceId);
            string target = D.Str(targetId);
            string merged = "EXISTS (SELECT 1 FROM ideas m WHERE " + Scope("m") + " AND m.id = " + source + " AND m.mergedintoid = " + target + ")";
            List<string> statements = new List<string>
            {
                "UPDATE ideas SET mergedintoid = " + target + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + source + " AND mergedintoid IS NULL AND deletedutc IS NULL;",
                "INSERT INTO ideavotes (propertyid, id, ideaid, userid, createdutc) SELECT " + Pid + ", " + D.Str(IdeaIdPrefixes.Vote) + " || substr(md5(v.id || " + target + "), 1, 24), " + target +
                    ", v.userid, v.createdutc FROM ideavotes v WHERE " + Scope("v") + " AND v.ideaid = " + source + " AND " + merged + " ON CONFLICT (propertyid, ideaid, userid) DO NOTHING;",
                "INSERT INTO ideafollows (propertyid, id, ideaid, userid, source, createdutc) SELECT " + Pid + ", " + D.Str(IdeaIdPrefixes.Follow) + " || substr(md5(f.id || " + target + "), 1, 24), " + target +
                    ", f.userid, " + D.Enum(IdeaFollowSourceEnum.Merge) + ", " + D.Ts(nowUtc) + " FROM ideafollows f WHERE " + Scope("f") + " AND f.ideaid = " + source + " AND " + merged +
                    " ON CONFLICT (propertyid, ideaid, userid) DO NOTHING;",
                RecomputeVotes(target),
                "SELECT id FROM ideas WHERE " + Scope() + " AND id = " + source + " AND mergedintoid = " + target + ";"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> UnmergeAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE ideas SET mergedintoid = NULL, updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND mergedintoid IS NOT NULL RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> SoftDeleteAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE ideas SET deletedutc = " + D.Ts(nowUtc) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND deletedutc IS NULL RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> PurgeAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            string iid = D.Str(id);
            List<string> statements = new List<string>();
            statements.AddRange(Driver.Metadata.DeleteStatements(TargetTypeEnum.Idea, id));
            statements.Add("DELETE FROM mentions WHERE " + Scope() + " AND sourcetype = " + D.Enum(TargetTypeEnum.Idea) + " AND sourceid = " + iid + ";");
            statements.Add("UPDATE ideas SET title = " + D.Str(String.Empty) + ", body = " + D.Str(String.Empty) + ", statusnote = NULL, purgedutc = " + D.Ts(nowUtc) + ", updatedutc = " + D.Ts(nowUtc) +
                " WHERE " + Scope() + " AND id = " + iid + " AND purgedutc IS NULL RETURNING id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> SetModerationStateAsync(string id, ModerationStateEnum state, string? moderatorUserId, string? reason, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE ideas SET moderationstate = " + D.Enum(state) + ", moderatedutc = " + (state == ModerationStateEnum.None ? "NULL" : D.Ts(nowUtc)) + ", moderatedbyuserid = " +
                D.Str(moderatorUserId) + ", moderationreason = " + D.Str(reason) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<int> RecomputeOpenReportCountAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE ideas SET openreportcount = (SELECT COUNT(*) FROM reports r WHERE " + Scope("r") + " AND r.targettype = " + D.Enum(TargetTypeEnum.Idea) +
                " AND r.targetid = ideas.id AND r.status = " + D.Enum(ReportStatusEnum.Open) + ") WHERE " + Scope() + " AND id = " + D.Str(id) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public async Task<int> RecomputeTrendingAsync(DateTime windowStartUtc, CancellationToken token = default)
        {
            string recent = "(SELECT COUNT(*) FROM ideavotes v WHERE " + Scope("v") + " AND v.ideaid = ideas.id AND v.createdutc >= " + D.Ts(windowStartUtc) + ")";
            DataTable table = await QueryAsync("UPDATE ideas SET recentvotecount = " + recent + " WHERE " + Scope() + " AND recentvotecount <> " + recent + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public async Task<int> EraseForUserAsync(string userId, string initialStatusKey, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            string user = D.Str(userId);
            List<string> statements = new List<string>
            {
                "UPDATE ideas SET votecount = (SELECT COUNT(*) FROM ideavotes v WHERE " + Scope("v") + " AND v.ideaid = ideas.id AND v.userid <> " + user + "), " +
                    "recentvotecount = GREATEST(0, recentvotecount - 1) WHERE " + Scope() + " AND id IN (SELECT x.ideaid FROM ideavotes x WHERE " + Scope("x") + " AND x.userid = " + user + ");",
                "DELETE FROM ideavotes WHERE " + Scope() + " AND userid = " + user + ";",
                "DELETE FROM ideafollows WHERE " + Scope() + " AND userid = " + user + ";",
                "UPDATE ideas SET deletedutc = " + D.Ts(nowUtc) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND authoruserid = " + user + " AND deletedutc IS NULL AND statuskey = " + D.Str(initialStatusKey) + ";",
                "SELECT COUNT(*) AS cnt FROM ideas WHERE " + Scope() + " AND authoruserid = " + user + " AND deletedutc = " + D.Ts(nowUtc) + ";"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            int deleted = table.Rows.Count == 0 ? 0 : (int)RowReader.GetLong(table.Rows[0], "cnt");
            await QueryAsync("UPDATE ideas SET authoruserid = NULL, updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND authoruserid = " + user + ";", token).ConfigureAwait(false);
            return deleted;
        }

        public async Task<List<Idea>> ListByAuthorAsync(string authorUserId, string? afterId, int limit, CancellationToken token = default)
        {
            RequireText(authorUserId, nameof(authorUserId));
            DataTable table = await QueryAsync("SELECT i.* FROM ideas i WHERE " + Scope("i") + " AND i.authoruserid = " + D.Str(authorUserId) + " AND i.deletedutc IS NULL" +
                (String.IsNullOrEmpty(afterId) ? String.Empty : " AND i.id > " + D.Str(afterId)) + " ORDER BY i.id LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<List<IdeaVoteRecord>> ListVotesByUserAsync(string userId, string? afterId, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT v.id, v.ideaid, v.createdutc, COALESCE(i.title, '') AS title FROM ideavotes v LEFT JOIN ideas i ON " + Scope("i") + " AND i.id = v.ideaid WHERE " + Scope("v") +
                " AND v.userid = " + D.Str(userId) + (String.IsNullOrEmpty(afterId) ? String.Empty : " AND v.id > " + D.Str(afterId)) + " ORDER BY v.id LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => new IdeaVoteRecord
            {
                Id = RowReader.GetString(r, "id"),
                IdeaId = RowReader.GetString(r, "ideaid"),
                IdeaTitle = RowReader.GetString(r, "title"),
                CreatedUtc = RowReader.GetDateTime(r, "createdutc")
            }).ToList();
        }

        public async Task<IdeaTicket?> ReadTicketAsync(string ticketId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(ticketId)) return null;
            DataTable table = await QueryAsync("SELECT t.id, t.typekey, t.title, t.body, t.requesteruserid, t.ideaid FROM tickets t WHERE " + Scope("t") + " AND t.id = " + D.Str(ticketId) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            DataRow row = table.Rows[0];
            return new IdeaTicket
            {
                Id = RowReader.GetString(row, "id"),
                TypeKey = RowReader.GetString(row, "typekey"),
                Title = RowReader.GetString(row, "title"),
                Body = RowReader.GetString(row, "body"),
                RequesterUserId = RowReader.GetNullableString(row, "requesteruserid"),
                IdeaId = RowReader.GetNullableString(row, "ideaid")
            };
        }

        public async Task<bool> LinkTicketAsync(string ticketId, string ideaId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(ticketId, nameof(ticketId));
            RequireText(ideaId, nameof(ideaId));
            DataTable table = await QueryAsync("UPDATE tickets SET ideaid = " + D.Str(ideaId) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(ticketId) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<HashSet<string>> RefreshStatusNotificationsAsync(string ideaId, IEnumerable<string> recipientUserIds, string title, string? body, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(ideaId, nameof(ideaId));
            ArgumentNullException.ThrowIfNull(recipientUserIds);
            List<string> recipients = recipientUserIds.Where(r => !String.IsNullOrEmpty(r)).Distinct(StringComparer.Ordinal).ToList();
            HashSet<string> refreshed = new HashSet<string>(StringComparer.Ordinal);
            if (recipients.Count == 0) return refreshed;
            DataTable table = await QueryAsync("UPDATE notifications SET title = " + D.Str(title) + ", body = " + D.Str(body) + ", createdutc = " + D.Ts(nowUtc) + ", updatedutc = " + D.Ts(nowUtc) +
                ", pushstate = " + D.Enum(PushStateEnum.Pending) + " WHERE " + Scope() + " AND type = " + D.Enum(NotificationTypeEnum.IdeaStatusChanged) + " AND targetid = " + D.Str(ideaId) +
                " AND readutc IS NULL AND recipientuserid IN " + D.InList(recipients) + " RETURNING recipientuserid;", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) refreshed.Add(RowReader.GetString(row, "recipientuserid"));
            return refreshed;
        }

        private List<string> Conditions(IdeaFilter filter)
        {
            List<string> conditions = new List<string> { "i.deletedutc IS NULL" };
            string? viewer = String.IsNullOrEmpty(filter.ViewerUserId) ? null : D.Str(filter.ViewerUserId);
            if (!filter.IsStaff)
            {
                List<string> audiences = filter.Audiences.Select(a => a.ToString()).Distinct(StringComparer.Ordinal).ToList();
                string audience = audiences.Count == 0 ? "1 = 0" : "i.audience IN " + D.InList(audiences);
                conditions.Add(viewer == null ? audience : "(" + audience + " OR i.authoruserid = " + viewer + ")");
                string unmoderated = "i.moderationstate = " + D.Enum(ModerationStateEnum.None);
                conditions.Add(viewer == null ? unmoderated : "(" + unmoderated + " OR i.authoruserid = " + viewer + ")");
            }
            if (filter.AudienceFilter != null) conditions.Add("i.audience = " + D.Enum(filter.AudienceFilter.Value));
            if (filter.Hidden == true) conditions.Add("i.moderationstate <> " + D.Enum(ModerationStateEnum.None));
            else if (filter.Hidden == false) conditions.Add("i.moderationstate = " + D.Enum(ModerationStateEnum.None));
            if (filter.Merged == true) conditions.Add("i.mergedintoid IS NOT NULL");
            else if (filter.Merged == false || !filter.IncludeMerged) conditions.Add("i.mergedintoid IS NULL");
            if (filter.StatusKeys.Count > 0) conditions.Add("i.statuskey IN " + D.InList(filter.StatusKeys));
            if (filter.ExcludedStatusKeys.Count > 0) conditions.Add("i.statuskey NOT IN " + D.InList(filter.ExcludedStatusKeys));
            if (!String.IsNullOrEmpty(filter.AuthorUserId)) conditions.Add("i.authoruserid = " + D.Str(filter.AuthorUserId) + " AND i.showauthor = " + D.Bool(true));
            if (!String.IsNullOrEmpty(filter.VotedByUserId))
                conditions.Add("EXISTS (SELECT 1 FROM ideavotes mv WHERE " + Scope("mv") + " AND mv.ideaid = i.id AND mv.userid = " + D.Str(filter.VotedByUserId) + ")");
            return conditions;
        }

        private string VoteInsert(string ideaId, string userId, DateTime nowUtc)
        {
            return "INSERT INTO ideavotes (propertyid, id, ideaid, userid, createdutc) VALUES (" + Pid + ", " + D.Str(IdGenerator.Generate(IdeaIdPrefixes.Vote)) + ", " + D.Str(ideaId) + ", " + D.Str(userId) + ", " +
                D.Ts(nowUtc) + ") ON CONFLICT (propertyid, ideaid, userid) DO NOTHING;";
        }

        private string FollowInsert(string ideaId, string userId, IdeaFollowSourceEnum source, DateTime nowUtc)
        {
            return "INSERT INTO ideafollows (propertyid, id, ideaid, userid, source, createdutc) VALUES (" + Pid + ", " + D.Str(IdGenerator.Generate(IdeaIdPrefixes.Follow)) + ", " + D.Str(ideaId) + ", " + D.Str(userId) + ", " +
                D.Enum(source) + ", " + D.Ts(nowUtc) + ") ON CONFLICT (propertyid, ideaid, userid) DO NOTHING;";
        }

        private string RecomputeVotes(string ideaLiteral)
        {
            return "UPDATE ideas SET votecount = (SELECT COUNT(*) FROM ideavotes v WHERE " + Scope("v") + " AND v.ideaid = ideas.id) WHERE " + Scope() + " AND id = " + ideaLiteral + ";";
        }

        private async Task<HashSet<string>> AmongAsync(string table, string userId, IEnumerable<string> ideaIds, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(ideaIds);
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            List<string> ids = ideaIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (String.IsNullOrEmpty(userId) || ids.Count == 0) return result;
            DataTable rows = await QueryAsync("SELECT x.ideaid FROM " + table + " x WHERE " + Scope("x") + " AND x.userid = " + D.Str(userId) + " AND x.ideaid IN " + D.InList(ids) + ";", token).ConfigureAwait(false);
            foreach (DataRow row in rows.Rows) result.Add(RowReader.GetString(row, "ideaid"));
            return result;
        }

        private static IdeaStatus MapStatus(DataRow row)
        {
            return new IdeaStatus
            {
                Id = RowReader.GetString(row, "id"),
                Key = RowReader.GetString(row, "key"),
                Label = RowReader.GetString(row, "label"),
                Color = RowReader.GetNullableString(row, "color"),
                IsClosed = RowReader.GetBool(row, "isclosed"),
                IsInitial = RowReader.GetBool(row, "isinitial"),
                SortOrder = RowReader.GetInt(row, "sortorder"),
                IsRetired = RowReader.GetBool(row, "isretired"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        private static Idea Map(DataRow row)
        {
            return new Idea
            {
                Id = RowReader.GetString(row, "id"),
                Title = RowReader.GetString(row, "title"),
                Body = RowReader.GetString(row, "body"),
                AuthorUserId = RowReader.GetNullableString(row, "authoruserid"),
                ShowAuthor = RowReader.GetBool(row, "showauthor"),
                StatusKey = RowReader.GetString(row, "statuskey"),
                StatusNote = RowReader.GetNullableString(row, "statusnote"),
                StatusUtc = RowReader.GetNullableDateTime(row, "statusutc"),
                VoteCount = RowReader.GetInt(row, "votecount"),
                RecentVoteCount = RowReader.GetInt(row, "recentvotecount"),
                MergedIntoId = RowReader.GetNullableString(row, "mergedintoid"),
                SourceTicketId = RowReader.GetNullableString(row, "sourceticketid"),
                Audience = RowReader.GetEnum(row, "audience", ContentAudienceEnum.Members),
                CommentCount = RowReader.GetInt(row, "commentcount"),
                ReactionTotal = RowReader.GetInt(row, "reactiontotal"),
                OpenReportCount = RowReader.GetInt(row, "openreportcount"),
                ModerationState = RowReader.GetEnum(row, "moderationstate", ModerationStateEnum.None),
                ModeratedUtc = RowReader.GetNullableDateTime(row, "moderatedutc"),
                ModeratedByUserId = RowReader.GetNullableString(row, "moderatedbyuserid"),
                ModerationReason = RowReader.GetNullableString(row, "moderationreason"),
                CreatedByUserId = RowReader.GetNullableString(row, "createdbyuserid"),
                EditedUtc = RowReader.GetNullableDateTime(row, "editedutc"),
                DeletedUtc = RowReader.GetNullableDateTime(row, "deletedutc"),
                PurgedUtc = row.Table.Columns.Contains("purgedutc") ? RowReader.GetNullableDateTime(row, "purgedutc") : null,
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }
    }
}
