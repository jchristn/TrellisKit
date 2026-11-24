namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Globalization;
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
    /// Shared feed post and feed post revision methods. Query alias f (feedposts) and fr (feedpostrevisions).
    /// </summary>
    internal abstract class FeedPostMethodsBase : MethodsBase, IFeedPostMethods
    {
        protected FeedPostMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<FeedPost> CreateAsync(FeedPost post, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(post);
            RequireText(post.Id, nameof(post.Id));
            RequireText(post.AuthorUserId, nameof(post.AuthorUserId));
            await QueryAsync("INSERT INTO feedposts (propertyid, id, authoruserid, kind, sourcetype, sourceid, title, body, excerpt, visibility, audience, linkpreviewid, showlinkpreview, " +
                "commentsenabled, publishatutc, publishedutc, ispinned, pinnedutc, pinnedbyuserid, editedutc, revisioncount, attachmentcount, hasvideo, commentcount, reactiontotal, " +
                "openreportcount, moderationstate, createdutc, updatedutc) VALUES (" +
                Pid + ", " + D.Str(post.Id) + ", " + D.Str(post.AuthorUserId) + ", " + D.Enum(post.Kind) + ", " + D.Str(post.SourceType) + ", " + D.Str(post.SourceId) + ", " +
                D.Str(post.Title) + ", " + D.Str(post.Body) + ", " + D.Str(post.Excerpt) + ", " + D.Enum(post.Visibility) + ", " + D.Enum(post.Audience) + ", " +
                D.Str(post.LinkPreviewId) + ", " + D.Bool(post.ShowLinkPreview) + ", " + D.Bool(post.CommentsEnabled) + ", " + D.Ts(post.PublishAtUtc) + ", " +
                D.Ts(post.PublishedUtc) + ", " + D.Bool(post.IsPinned) + ", " + D.Ts(post.PinnedUtc) + ", " + D.Str(post.PinnedByUserId) + ", " + D.Ts(post.EditedUtc) + ", " +
                D.Num(0) + ", " + D.Num(post.AttachmentCount) + ", " + D.Bool(post.HasVideo) + ", " + D.Num(0) + ", " + D.Num(0) + ", " + D.Num(0) + ", " +
                D.Enum(ModerationStateEnum.None) + ", " + D.Ts(post.CreatedUtc) + ", " + D.Ts(post.UpdatedUtc) + ");", token).ConfigureAwait(false);
            return (await ReadAsync(post.Id, token).ConfigureAwait(false))!;
        }

        public async Task<FeedPost?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            DataTable table = await QueryAsync("SELECT f.* FROM feedposts f WHERE " + Scope("f") + " AND f.id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<List<FeedPost>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            List<string> list = ids.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (list.Count == 0) return new List<FeedPost>();
            DataTable table = await QueryAsync("SELECT f.* FROM feedposts f WHERE " + Scope("f") + " AND f.id IN " + D.InList(list) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<FeedPost> UpdateAsync(FeedPost post, FeedPostRevision? revision, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(post);
            RequireText(post.Id, nameof(post.Id));
            string id = D.Str(post.Id);
            List<string> statements = new List<string>();
            if (revision != null)
            {
                statements.Add("INSERT INTO feedpostrevisions (propertyid, id, feedpostid, revision, title, body, attachmentsummary, editedbyuserid, createdutc) SELECT " +
                    Pid + ", " + D.Str(revision.Id) + ", " + id + ", COALESCE(MAX(revision), 0) + 1, " + D.Str(revision.Title) + ", " + D.Str(revision.Body) + ", " +
                    D.Str(revision.AttachmentSummary) + ", " + D.Str(revision.EditedByUserId) + ", " + D.Ts(revision.CreatedUtc) +
                    " FROM feedpostrevisions WHERE " + Scope() + " AND feedpostid = " + id + ";");
            }
            statements.Add("UPDATE feedposts SET title = " + D.Str(post.Title) + ", body = " + D.Str(post.Body) + ", excerpt = " + D.Str(post.Excerpt) +
                ", visibility = " + D.Enum(post.Visibility) + ", audience = " + D.Enum(post.Audience) + ", linkpreviewid = " + D.Str(post.LinkPreviewId) +
                ", showlinkpreview = " + D.Bool(post.ShowLinkPreview) + ", commentsenabled = " + D.Bool(post.CommentsEnabled) + ", publishatutc = " + D.Ts(post.PublishAtUtc) +
                ", editedutc = " + D.Ts(post.EditedUtc) + ", revisioncount = (SELECT COUNT(*) FROM feedpostrevisions r WHERE " + Scope("r") + " AND r.feedpostid = " + id + ")" +
                ", updatedutc = " + D.Ts(post.UpdatedUtc) + " WHERE " + Scope() + " AND id = " + id + ";");
            await TransactionAsync(statements, token).ConfigureAwait(false);
            await RecomputeAttachmentsAsync(post.Id, token).ConfigureAwait(false);
            return (await ReadAsync(post.Id, token).ConfigureAwait(false)) ?? post;
        }

        public async Task<bool> RecomputeAttachmentsAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            string media = "FROM mediaattachments m WHERE " + Scope("m") + " AND m.attachedtargettype = " + D.Enum(TargetTypeEnum.FeedPost) + " AND m.attachedtargetid = feedposts.id AND m.deletedutc IS NULL";
            DataTable table = await QueryAsync("UPDATE feedposts SET attachmentcount = (SELECT COUNT(*) " + media + "), hasvideo = (CASE WHEN EXISTS (SELECT 1 " + media +
                " AND m.kind = " + D.Enum(MediaKindEnum.Video) + ") THEN " + D.Bool(true) + " ELSE " + D.Bool(false) + " END) WHERE " + Scope() + " AND id = " + D.Str(id) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> TryPublishAsync(string id, DateTime publishedUtc, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE feedposts SET publishedutc = " + D.Ts(publishedUtc) + ", updatedutc = " + D.Ts(nowUtc) +
                " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND publishedutc IS NULL AND deletedutc IS NULL AND moderationstate = " + D.Enum(ModerationStateEnum.None) +
                " AND (publishatutc IS NULL OR publishatutc <= " + D.Ts(nowUtc) + ") RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> PinAsync(string id, string? byUserId, DateTime nowUtc, int maxPinned, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE feedposts SET ispinned = " + D.Bool(true) + ", pinnedutc = COALESCE(pinnedutc, " + D.Ts(nowUtc) + "), pinnedbyuserid = COALESCE(pinnedbyuserid, " +
                D.Str(byUserId) + "), updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND deletedutc IS NULL AND publishedutc IS NOT NULL AND moderationstate = " +
                D.Enum(ModerationStateEnum.None) + " AND (ispinned = " + D.Bool(true) + " OR (SELECT COUNT(*) FROM feedposts p WHERE " + Scope("p") + " AND p.ispinned = " + D.Bool(true) +
                " AND p.deletedutc IS NULL) < " + D.Num(Math.Max(1, maxPinned)) + ") RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> UnpinAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE feedposts SET ispinned = " + D.Bool(false) + ", pinnedutc = NULL, pinnedbyuserid = NULL, updatedutc = " + D.Ts(nowUtc) +
                " WHERE " + Scope() + " AND id = " + D.Str(id) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> SoftDeleteAsync(string id, string? byUserId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            List<string> statements = new List<string>();
            statements.AddRange(Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.FeedPost, "SELECT id FROM feedposts WHERE " + Scope() + " AND id = " + D.Str(id) + " AND deletedutc IS NULL"));
            statements.Add("UPDATE feedposts SET deletedutc = " + D.Ts(nowUtc) + ", deletedbyuserid = " + D.Str(byUserId) + ", ispinned = " + D.Bool(false) +
                ", pinnedutc = NULL, pinnedbyuserid = NULL, updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND deletedutc IS NULL RETURNING id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> SetModerationStateAsync(string id, ModerationStateEnum state, string? moderatorUserId, string? reason, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE feedposts SET moderationstate = " + D.Enum(state) + ", moderatedutc = " + D.Ts(nowUtc) + ", moderatedbyuserid = " + D.Str(moderatorUserId) +
                ", moderationreason = " + D.Str(reason) + ", ispinned = (CASE WHEN " + D.Enum(state) + " = " + D.Enum(ModerationStateEnum.None) + " THEN ispinned ELSE " + D.Bool(false) + " END)" +
                ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND deletedutc IS NULL RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<int> RecomputeOpenReportCountAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE feedposts SET openreportcount = (SELECT COUNT(*) FROM reports r WHERE " + Scope("r") + " AND r.targettype = " + D.Enum(TargetTypeEnum.FeedPost) +
                " AND r.targetid = feedposts.id AND r.status = " + D.Enum(ReportStatusEnum.Open) + ") WHERE " + Scope() + " AND id = " + D.Str(id) + " RETURNING openreportcount;", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? 0 : RowReader.GetInt(table.Rows[0], "openreportcount");
        }

        public Task<EnumerationResult<FeedPost>> EnumerateFeedAsync(EnumerationRequest request, FeedPostFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = FeedConditions(filter);
            if (filter.UnpinnedOnly) conditions.Add("f.ispinned = " + D.Bool(false));
            return EnumerateAsync(request, "f.*", "feedposts f", conditions, Map, token);
        }

        public async Task<List<FeedPost>> ListPinnedAsync(EnumerationRequest request, FeedPostFilter filter, int max, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = FeedConditions(filter);
            conditions.Add("f.ispinned = " + D.Bool(true));
            conditions.AddRange(EnumerationConditions(request));
            DataTable table = await QueryAsync("SELECT f.* FROM feedposts f" + Where(conditions) + " ORDER BY f.pinnedutc DESC, f.id DESC LIMIT " + D.Num(Math.Max(1, max)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public Task<EnumerationResult<FeedPost>> EnumerateByAuthorAsync(string authorUserId, EnumerationRequest request, FeedPostStateEnum? state, ContentAudienceEnum? audience, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(authorUserId, nameof(authorUserId));
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string> { "f.authoruserid = " + D.Str(authorUserId), "f.deletedutc IS NULL" };
            if (state != null) conditions.Add(StateCondition(state.Value, nowUtc));
            if (audience != null) conditions.Add("f.audience = " + D.Enum(audience.Value));
            return EnumerateAsync(request, "f.*", "feedposts f", conditions, Map, token);
        }

        public Task<EnumerationResult<FeedPost>> EnumerateAdminAsync(EnumerationRequest request, AdminFeedFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string>();
            if (filter.State != null) conditions.Add(StateCondition(filter.State.Value, filter.NowUtc));
            if (!String.IsNullOrEmpty(filter.AuthorUserId)) conditions.Add("f.authoruserid = " + D.Str(filter.AuthorUserId));
            if (filter.Pinned != null) conditions.Add("f.ispinned = " + D.Bool(filter.Pinned.Value));
            if (filter.FromUtc != null) conditions.Add("COALESCE(f.publishedutc, f.createdutc) >= " + D.Ts(filter.FromUtc.Value));
            if (filter.ToUtc != null) conditions.Add("COALESCE(f.publishedutc, f.createdutc) < " + D.Ts(filter.ToUtc.Value));
            if (filter.Audience != null) conditions.Add("f.audience = " + D.Enum(filter.Audience.Value));
            return EnumerateAsync(request, "f.*", "feedposts f", conditions, Map, token);
        }

        public async Task<List<FeedPost>> ListPublishCandidatesAsync(DateTime nowUtc, int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT f.* FROM feedposts f WHERE " + Scope("f") + " AND f.publishedutc IS NULL AND f.deletedutc IS NULL AND f.moderationstate = " + D.Enum(ModerationStateEnum.None) +
                " AND (f.publishatutc IS NULL OR f.publishatutc <= " + D.Ts(nowUtc) + ") AND NOT EXISTS (SELECT 1 FROM mediaattachments m WHERE " + Scope("m") + " AND m.attachedtargettype = " +
                D.Enum(TargetTypeEnum.FeedPost) + " AND m.attachedtargetid = f.id AND m.deletedutc IS NULL AND m.status <> " + D.Enum(MediaStatusEnum.Ready) + ")" +
                " ORDER BY f.createdutc ASC, f.id ASC LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public Task<EnumerationResult<FeedPostRevision>> EnumerateRevisionsAsync(string feedPostId, EnumerationRequest request, CancellationToken token = default)
        {
            RequireText(feedPostId, nameof(feedPostId));
            ArgumentNullException.ThrowIfNull(request);
            return EnumerateAsync(request, "fr.*", "feedpostrevisions fr", new List<string> { "fr.feedpostid = " + D.Str(feedPostId) }, MapRevision, token);
        }

        public async Task<List<FeedPostRevision>> ListRevisionsAsync(IEnumerable<string> feedPostIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(feedPostIds);
            List<string> ids = feedPostIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return new List<FeedPostRevision>();
            DataTable table = await QueryAsync("SELECT fr.* FROM feedpostrevisions fr WHERE " + Scope("fr") + " AND fr.feedpostid IN " + D.InList(ids) + " ORDER BY fr.feedpostid, fr.revision;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapRevision).ToList();
        }

        public async Task<ReportSummaryView> ReadReportSummaryAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            RequireText(targetId, nameof(targetId));
            string where = " FROM reports WHERE " + Scope() + " AND targettype = " + D.Enum(targetType) + " AND targetid = " + D.Str(targetId);
            long open = await CountAsync("SELECT COUNT(*) AS cnt" + where + " AND status = " + D.Enum(ReportStatusEnum.Open) + ";", token).ConfigureAwait(false);
            long total = await CountAsync("SELECT COUNT(*) AS cnt" + where + ";", token).ConfigureAwait(false);
            return new ReportSummaryView { Open = open, Total = total };
        }

        public Task<long> CountPinnedAsync(CancellationToken token = default)
        {
            return CountAsync("SELECT COUNT(*) AS cnt FROM feedposts WHERE " + Scope() + " AND ispinned = " + D.Bool(true) + " AND deletedutc IS NULL;", token);
        }

        public Task<long> CountModerationActionsAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            RequireText(targetId, nameof(targetId));
            return CountAsync("SELECT COUNT(*) AS cnt FROM moderationactions WHERE " + Scope() + " AND targettype = " + D.Enum(targetType) + " AND targetid = " + D.Str(targetId) + ";", token);
        }

        public async Task<EngagementMetrics> EngagementAsync(DateTime fromUtc, int days, DateTime nowUtc, CancellationToken token = default)
        {
            string from = D.Ts(fromUtc);
            EngagementMetrics metrics = new EngagementMetrics { Days = days };
            metrics.PostsByDay = await DailyAsync("feedposts", "publishedutc", "deletedutc IS NULL AND publishedutc >= " + from, fromUtc, days, token).ConfigureAwait(false);
            metrics.CommentsByDay = await DailyAsync("comments", "createdutc", "createdutc >= " + from, fromUtc, days, token).ConfigureAwait(false);
            metrics.ReactionsByDay = await DailyAsync("reactions", "createdutc", "createdutc >= " + from, fromUtc, days, token).ConfigureAwait(false);
            metrics.ActiveMembers = await CountAsync("SELECT COUNT(*) AS cnt FROM (SELECT authoruserid AS uid FROM comments WHERE " + Scope() + " AND createdutc >= " + from +
                " UNION SELECT userid AS uid FROM reactions WHERE " + Scope() + " AND createdutc >= " + from + ") a;", token).ConfigureAwait(false);
            metrics.OpenReports = await CountAsync("SELECT COUNT(*) AS cnt FROM reports WHERE " + Scope() + " AND status = " + D.Enum(ReportStatusEnum.Open) + ";", token).ConfigureAwait(false);
            metrics.ScheduledPosts = await CountAsync("SELECT COUNT(*) AS cnt FROM feedposts f WHERE " + Scope("f") + " AND " + StateCondition(FeedPostStateEnum.Scheduled, nowUtc) + ";", token).ConfigureAwait(false);
            return metrics;
        }

        public async Task<List<FeedPost>> ListByAuthorAsync(string authorUserId, string? afterId, int limit, bool includeDeleted, CancellationToken token = default)
        {
            RequireText(authorUserId, nameof(authorUserId));
            List<string> conditions = new List<string> { Scope("f"), "f.authoruserid = " + D.Str(authorUserId) };
            if (!includeDeleted) conditions.Add("f.deletedutc IS NULL");
            if (!String.IsNullOrEmpty(afterId)) conditions.Add("f.id > " + D.Str(afterId));
            DataTable table = await QueryAsync("SELECT f.* FROM feedposts f" + Where(conditions) + " ORDER BY f.id ASC LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<bool> PurgeAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            string pid = D.Str(id);
            List<string> statements = new List<string>();
            statements.AddRange(Driver.Metadata.DeleteStatements(TargetTypeEnum.FeedPost, id));
            statements.AddRange(Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.FeedPostRevision, "SELECT id FROM feedpostrevisions WHERE " + Scope() + " AND feedpostid = " + pid));
            statements.Add("DELETE FROM feedpostrevisions WHERE " + Scope() + " AND feedpostid = " + pid + ";");
            statements.Add("DELETE FROM mentions WHERE " + Scope() + " AND sourcetype = " + D.Enum(TargetTypeEnum.FeedPost) + " AND sourceid = " + pid + ";");
            statements.Add("UPDATE feedposts SET title = NULL, body = " + D.Str(String.Empty) + ", excerpt = " + D.Str(String.Empty) + ", linkpreviewid = NULL, revisioncount = " + D.Num(0) +
                ", ispinned = " + D.Bool(false) + ", purgedutc = " + D.Ts(nowUtc) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + pid + " AND purgedutc IS NULL RETURNING id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        private List<string> FeedConditions(FeedPostFilter filter)
        {
            List<string> conditions = new List<string>
            {
                "f.deletedutc IS NULL",
                "f.moderationstate = " + D.Enum(ModerationStateEnum.None),
                "f.publishedutc IS NOT NULL"
            };
            if (filter.PublicOnly) conditions.Add("f.visibility = " + D.Enum(FeedVisibilityEnum.Public) + " AND f.audience = " + D.Enum(ContentAudienceEnum.Public));
            if (filter.HiddenAuthorIds.Count > 0) conditions.Add("f.authoruserid NOT IN " + D.InList(filter.HiddenAuthorIds));
            if (filter.PublishedAfterUtc != null) conditions.Add("f.publishedutc >= " + D.Ts(filter.PublishedAfterUtc.Value));
            if (filter.PublishedBeforeUtc != null) conditions.Add("f.publishedutc < " + D.Ts(filter.PublishedBeforeUtc.Value));
            if (filter.Audiences != null) conditions.Add(filter.Audiences.Count == 0 ? "1 = 0" : "f.audience IN " + D.InList(filter.Audiences.Select(a => a.ToString())));
            // A promoted thread's card shows only while its thread is live and readable by the caller (Phase 3 section 8.4).
            string thread = "EXISTS (SELECT 1 FROM forumthreads pt WHERE " + Scope("pt") + " AND pt.id = f.sourceid AND pt.deletedutc IS NULL AND pt.moderationstate = " + D.Enum(ModerationStateEnum.None) +
                " AND pt.publishedutc IS NOT NULL AND pt.promotedfeedpostid = f.id" +
                (filter.Audiences == null ? String.Empty : filter.Audiences.Count == 0 ? " AND 1 = 0" : " AND pt.audience IN " + D.InList(filter.Audiences.Select(a => a.ToString()))) + ")";
            conditions.Add("(f.kind <> " + D.Enum(FeedPostKindEnum.PromotedThread) + " OR " + thread + ")");
            // A promoted lesson's or event's card leaves the feed while its source is not live (Phase 4 section 5.8).
            conditions.Add("(f.kind NOT IN " + D.InList(new[] { FeedPostKindEnum.PromotedLesson.ToString(), FeedPostKindEnum.PromotedEvent.ToString() }) + " OR f.sourcehidden = " + D.Bool(false) + ")");
            return conditions;
        }

        public async Task<bool> SetDiscussionAsync(string id, string? threadId, string? byUserId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            string sql = threadId == null
                ? "UPDATE feedposts SET discussionthreadid = NULL, discussionlinkedbyuserid = NULL, updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + " RETURNING id;"
                : "UPDATE feedposts SET discussionthreadid = " + D.Str(threadId) + ", discussionlinkedbyuserid = " + D.Str(byUserId) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " +
                    D.Str(id) + " AND discussionthreadid IS NULL RETURNING id;";
            DataTable table = await QueryAsync(sql, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        /// <inheritdoc />
        public async Task<bool> SetSourceHiddenAsync(string id, bool sourceHidden, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE feedposts SET sourcehidden = " + D.Bool(sourceHidden) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        /// <inheritdoc />
        public async Task<FeedPost?> ReadPromotedBySourceAsync(FeedPostKindEnum kind, string sourceType, string sourceId, CancellationToken token = default)
        {
            RequireText(sourceType, nameof(sourceType));
            RequireText(sourceId, nameof(sourceId));
            DataTable table = await QueryAsync("SELECT * FROM feedposts WHERE " + Scope() + " AND kind = " + D.Enum(kind) + " AND sourcetype = " + D.Str(sourceType) + " AND sourceid = " + D.Str(sourceId) +
                " AND deletedutc IS NULL ORDER BY createdutc DESC, id DESC LIMIT 1;", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        private string StateCondition(FeedPostStateEnum state, DateTime nowUtc)
        {
            string live = "f.deletedutc IS NULL";
            string unmoderated = live + " AND f.moderationstate = " + D.Enum(ModerationStateEnum.None);
            switch (state)
            {
                case FeedPostStateEnum.Deleted:
                    return "f.deletedutc IS NOT NULL";
                case FeedPostStateEnum.Hidden:
                    return "(" + live + " AND f.moderationstate = " + D.Enum(ModerationStateEnum.Hidden) + ")";
                case FeedPostStateEnum.Removed:
                    return "(" + live + " AND f.moderationstate = " + D.Enum(ModerationStateEnum.Removed) + ")";
                case FeedPostStateEnum.Published:
                    return "(" + unmoderated + " AND f.publishedutc IS NOT NULL)";
                case FeedPostStateEnum.Scheduled:
                    return "(" + unmoderated + " AND f.publishedutc IS NULL AND f.publishatutc IS NOT NULL AND f.publishatutc > " + D.Ts(nowUtc) + ")";
                default:
                    return "(" + unmoderated + " AND f.publishedutc IS NULL AND (f.publishatutc IS NULL OR f.publishatutc <= " + D.Ts(nowUtc) + "))";
            }
        }

        private async Task<List<DailyCount>> DailyAsync(string table, string column, string condition, DateTime fromUtc, int days, CancellationToken token)
        {
            string day = D.DayOf(column);
            DataTable rows = await QueryAsync("SELECT " + day + " AS day, COUNT(*) AS cnt FROM " + table + " WHERE " + Scope() + " AND " + condition + " GROUP BY " + day + ";", token).ConfigureAwait(false);
            Dictionary<string, long> counts = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (DataRow row in rows.Rows) counts[RowReader.GetString(row, "day")] = RowReader.GetLong(row, "cnt");
            List<DailyCount> result = new List<DailyCount>();
            for (int i = 0; i < days; i++)
            {
                string key = fromUtc.Date.AddDays(i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                result.Add(new DailyCount { Date = key, Count = counts.TryGetValue(key, out long c) ? c : 0 });
            }
            return result;
        }

        private static FeedPost Map(DataRow row)
        {
            return new FeedPost
            {
                Id = RowReader.GetString(row, "id"),
                AuthorUserId = RowReader.GetString(row, "authoruserid"),
                Kind = RowReader.GetEnum(row, "kind", FeedPostKindEnum.Original),
                SourceType = RowReader.GetNullableString(row, "sourcetype"),
                SourceId = RowReader.GetNullableString(row, "sourceid"),
                Title = RowReader.GetNullableString(row, "title"),
                Body = RowReader.GetString(row, "body"),
                Excerpt = RowReader.GetString(row, "excerpt"),
                Visibility = RowReader.GetEnum(row, "visibility", FeedVisibilityEnum.Members),
                Audience = RowReader.GetEnum(row, "audience", ContentAudienceEnum.Members),
                LinkPreviewId = RowReader.GetNullableString(row, "linkpreviewid"),
                ShowLinkPreview = RowReader.GetBool(row, "showlinkpreview"),
                CommentsEnabled = RowReader.GetBool(row, "commentsenabled"),
                PublishAtUtc = RowReader.GetNullableDateTime(row, "publishatutc"),
                PublishedUtc = RowReader.GetNullableDateTime(row, "publishedutc"),
                IsPinned = RowReader.GetBool(row, "ispinned"),
                PinnedUtc = RowReader.GetNullableDateTime(row, "pinnedutc"),
                PinnedByUserId = RowReader.GetNullableString(row, "pinnedbyuserid"),
                EditedUtc = RowReader.GetNullableDateTime(row, "editedutc"),
                RevisionCount = RowReader.GetInt(row, "revisioncount"),
                AttachmentCount = RowReader.GetInt(row, "attachmentcount"),
                HasVideo = RowReader.GetBool(row, "hasvideo"),
                CommentCount = RowReader.GetInt(row, "commentcount"),
                ReactionTotal = RowReader.GetInt(row, "reactiontotal"),
                OpenReportCount = RowReader.GetInt(row, "openreportcount"),
                ModerationState = RowReader.GetEnum(row, "moderationstate", ModerationStateEnum.None),
                ModeratedUtc = RowReader.GetNullableDateTime(row, "moderatedutc"),
                ModeratedByUserId = RowReader.GetNullableString(row, "moderatedbyuserid"),
                ModerationReason = RowReader.GetNullableString(row, "moderationreason"),
                DeletedUtc = RowReader.GetNullableDateTime(row, "deletedutc"),
                DeletedByUserId = RowReader.GetNullableString(row, "deletedbyuserid"),
                PurgedUtc = RowReader.GetNullableDateTime(row, "purgedutc"),
                DiscussionThreadId = row.Table.Columns.Contains("discussionthreadid") ? RowReader.GetNullableString(row, "discussionthreadid") : null,
                DiscussionLinkedByUserId = row.Table.Columns.Contains("discussionlinkedbyuserid") ? RowReader.GetNullableString(row, "discussionlinkedbyuserid") : null,
                SourceHidden = row.Table.Columns.Contains("sourcehidden") && RowReader.GetBool(row, "sourcehidden"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        private static FeedPostRevision MapRevision(DataRow row)
        {
            return new FeedPostRevision
            {
                Id = RowReader.GetString(row, "id"),
                FeedPostId = RowReader.GetString(row, "feedpostid"),
                Revision = RowReader.GetInt(row, "revision"),
                Title = RowReader.GetNullableString(row, "title"),
                Body = RowReader.GetNullableString(row, "body"),
                AttachmentSummary = RowReader.GetNullableString(row, "attachmentsummary"),
                EditedByUserId = RowReader.GetNullableString(row, "editedbyuserid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }

        public Task<List<ExcerptSource>> ExcerptCandidatesAsync(string? afterId, int max, CancellationToken token = default)
        {
            return ExcerptCandidatesCoreAsync("feedposts", null, afterId, max, token);
        }

        public Task UpdateExcerptAsync(string id, string excerpt, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            return UpdateExcerptCoreAsync("feedposts", id, excerpt ?? String.Empty, token);
        }
    }
}
