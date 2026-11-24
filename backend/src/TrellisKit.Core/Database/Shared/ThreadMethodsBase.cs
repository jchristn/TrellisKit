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
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    internal abstract class ThreadMethodsBase : MethodsBase, IThreadMethods
    {
        protected ThreadMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<ForumThread> CreateAsync(ForumThread thread, Poll? poll, ThreadFollow? authorFollow, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(thread);
            RequireText(thread.Id, nameof(thread.Id));
            RequireText(thread.ForumId, nameof(thread.ForumId));
            RequireText(thread.AuthorUserId, nameof(thread.AuthorUserId));
            List<string> statements = new List<string>
            {
                "INSERT INTO forumthreads (propertyid, id, forumid, kind, type, authoruserid, title, body, excerpt, linkpreviewid, showlinkpreview, audience, sourcetype, sourceid, " +
                "committargettype, committargetid, feedbackstatus, feedbackfocus, feedbackstatusutc, attachmentcount, hasvideo, trendingscore, lastactivityutc, moderationstate, publishedutc, createdutc, updatedutc) VALUES (" +
                Pid + ", " + D.Str(thread.Id) + ", " + D.Str(thread.ForumId) + ", " + D.Enum(thread.Kind) + ", " + D.Enum(thread.Type) + ", " + D.Str(thread.AuthorUserId) + ", " +
                D.Str(thread.Title) + ", " + D.Str(thread.Body) + ", " + D.Str(thread.Excerpt) + ", " + D.Str(thread.LinkPreviewId) + ", " + D.Bool(thread.ShowLinkPreview) + ", " +
                D.Enum(thread.Audience) + ", " + D.Str(thread.SourceType) + ", " + D.Str(thread.SourceId) + ", " + D.Enum(thread.CommentTargetType) + ", " + D.Str(thread.CommentTargetId) + ", " +
                (thread.FeedbackStatus == null ? "NULL" : D.Enum(thread.FeedbackStatus.Value)) + ", " + D.Str(thread.FeedbackFocus) + ", " + D.Ts(thread.FeedbackStatusUtc) + ", " +
                D.Num(thread.AttachmentCount) + ", " + D.Bool(thread.HasVideo) + ", " + D.Num(thread.TrendingScore) + ", " + D.Ts(thread.LastActivityUtc) + ", " + D.Enum(ModerationStateEnum.None) + ", " +
                D.Ts(thread.PublishedUtc) + ", " + D.Ts(thread.CreatedUtc) + ", " + D.Ts(thread.UpdatedUtc) + ");"
            };
            if (poll != null) statements.AddRange(PollInsertStatements(poll));
            if (authorFollow != null) statements.Add(FollowUpsert(authorFollow));
            if (thread.Kind == ThreadKindEnum.LinkedFeedPost && thread.SourceId != null)
            {
                statements.Add(ForumCounterSql.RecomputeLinkedThreadComments(Pid, D.Str(thread.SourceId)));
                statements.Add("UPDATE forumthreads SET reactiontotal = (SELECT COALESCE(fp.reactiontotal, 0) FROM feedposts fp WHERE " + Scope("fp") + " AND fp.id = forumthreads.sourceid) WHERE " +
                    Scope() + " AND id = " + D.Str(thread.Id) + ";");
            }
            statements.Add(ForumCounterSql.RecomputeForum(Pid, D.Str(thread.ForumId)));
            await UniqueTransactionAsync(statements, "A discussion thread for this source", token).ConfigureAwait(false);
            return (await ReadAsync(thread.Id, token).ConfigureAwait(false))!;
        }

        public async Task<ForumThread?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            DataTable table = await QueryAsync("SELECT t.* FROM forumthreads t WHERE " + Scope("t") + " AND t.id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<List<ForumThread>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            List<string> list = ids.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (list.Count == 0) return new List<ForumThread>();
            DataTable table = await QueryAsync("SELECT t.* FROM forumthreads t WHERE " + Scope("t") + " AND t.id IN " + D.InList(list) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<ForumThread?> ReadBySourceAsync(string sourceType, string sourceId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(sourceType) || String.IsNullOrEmpty(sourceId)) return null;
            DataTable table = await QueryAsync("SELECT t.* FROM forumthreads t WHERE " + Scope("t") + " AND t.sourcetype = " + D.Str(sourceType) + " AND t.sourceid = " + D.Str(sourceId) +
                " AND t.deletedutc IS NULL ORDER BY t.createdutc DESC LIMIT 1;", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<ForumThread> UpdateAsync(ForumThread thread, ThreadRevision? revision, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(thread);
            RequireText(thread.Id, nameof(thread.Id));
            string id = D.Str(thread.Id);
            List<string> statements = new List<string>();
            if (revision != null)
            {
                statements.Add("INSERT INTO forumthreadrevisions (propertyid, id, threadid, revision, title, body, type, attachmentsummary, editedbyuserid, editedbyrole, createdutc) SELECT " + Pid + ", " +
                    D.Str(revision.Id) + ", " + id + ", COALESCE(MAX(revision), 0) + 1, " + D.Str(revision.Title) + ", " + D.Str(revision.Body) + ", " + D.Enum(revision.Type) + ", " +
                    D.Str(revision.AttachmentSummary) + ", " + D.Str(revision.EditedByUserId) + ", " + D.Enum(revision.EditedByRole) + ", " + D.Ts(revision.CreatedUtc) +
                    " FROM forumthreadrevisions WHERE " + Scope() + " AND threadid = " + id + ";");
            }
            statements.Add("UPDATE forumthreads SET title = " + D.Str(thread.Title) + ", body = " + D.Str(thread.Body) + ", excerpt = " + D.Str(thread.Excerpt) + ", type = " + D.Enum(thread.Type) +
                ", linkpreviewid = " + D.Str(thread.LinkPreviewId) + ", showlinkpreview = " + D.Bool(thread.ShowLinkPreview) + ", audience = " + D.Enum(thread.Audience) +
                ", feedbackfocus = " + D.Str(thread.FeedbackFocus) + ", editedutc = " + D.Ts(thread.EditedUtc) +
                ", revisioncount = (SELECT COUNT(*) FROM forumthreadrevisions r WHERE " + Scope("r") + " AND r.threadid = " + id + "), updatedutc = " + D.Ts(thread.UpdatedUtc) +
                " WHERE " + Scope() + " AND id = " + id + " AND deletedutc IS NULL;");
            await TransactionAsync(statements, token).ConfigureAwait(false);
            await RecomputeAttachmentsAsync(thread.Id, token).ConfigureAwait(false);
            return (await ReadAsync(thread.Id, token).ConfigureAwait(false)) ?? thread;
        }

        public async Task<bool> RecomputeAttachmentsAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            string media = "FROM mediaattachments m WHERE " + Scope("m") + " AND m.attachedtargettype = " + D.Enum(TargetTypeEnum.Thread) + " AND m.attachedtargetid = forumthreads.id AND m.deletedutc IS NULL";
            DataTable table = await QueryAsync("UPDATE forumthreads SET attachmentcount = (SELECT COUNT(*) " + media + "), hasvideo = (CASE WHEN EXISTS (SELECT 1 " + media +
                " AND m.kind = " + D.Enum(MediaKindEnum.Video) + ") THEN " + D.Bool(true) + " ELSE " + D.Bool(false) + " END) WHERE " + Scope() + " AND id = " + D.Str(id) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> TryPublishAsync(string id, DateTime publishedUtc, double initialScore, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            List<string> statements = new List<string>
            {
                "UPDATE forumthreads SET publishedutc = " + D.Ts(publishedUtc) + ", lastactivityutc = GREATEST(lastactivityutc, " + D.Ts(publishedUtc) + "), trendingscore = " + D.Num(initialScore) +
                    ", trendingcomputedutc = " + D.Ts(nowUtc) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) +
                    " AND publishedutc IS NULL AND deletedutc IS NULL AND moderationstate = " + D.Enum(ModerationStateEnum.None) + ";",
                ForumCounterSql.RecomputeForum(Pid, ForumCounterSql.ForumOfThread(Pid, D.Str(id))),
                "SELECT id FROM forumthreads WHERE " + Scope() + " AND id = " + D.Str(id) + " AND publishedutc = " + D.Ts(publishedUtc) + " AND updatedutc = " + D.Ts(nowUtc) + ";"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<List<ForumThread>> ListPublishCandidatesAsync(int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT t.* FROM forumthreads t WHERE " + Scope("t") + " AND t.publishedutc IS NULL AND t.deletedutc IS NULL AND t.moderationstate = " +
                D.Enum(ModerationStateEnum.None) + " AND NOT EXISTS (SELECT 1 FROM mediaattachments m WHERE " + Scope("m") + " AND m.attachedtargettype = " + D.Enum(TargetTypeEnum.Thread) +
                " AND m.attachedtargetid = t.id AND m.deletedutc IS NULL AND m.status <> " + D.Enum(MediaStatusEnum.Ready) + ") ORDER BY t.createdutc ASC, t.id ASC LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<bool> SoftDeleteAsync(string id, string? byUserId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            string tid = D.Str(id);
            List<string> statements = new List<string>();
            statements.AddRange(Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.ThreadFollow, "SELECT id FROM threadfollows WHERE " + Scope() + " AND threadid = " + tid));
            statements.Add("DELETE FROM threadfollows WHERE " + Scope() + " AND threadid = " + tid + ";");
            statements.Add("DELETE FROM threadreads WHERE " + Scope() + " AND threadid = " + tid + ";");
            statements.Add("UPDATE forumthreads SET deletedutc = " + D.Ts(nowUtc) + ", deletedbyuserid = " + D.Str(byUserId) + ", ispinned = " + D.Bool(false) + ", pinnedutc = NULL, pinnedbyuserid = NULL" +
                ", trendingscore = 0, updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + tid + " AND deletedutc IS NULL;");
            statements.Add(ForumCounterSql.RecomputeForum(Pid, ForumCounterSql.ForumOfThread(Pid, tid)));
            statements.Add("SELECT id FROM forumthreads WHERE " + Scope() + " AND id = " + tid + " AND deletedutc = " + D.Ts(nowUtc) + ";");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> SetModerationStateAsync(string id, ModerationStateEnum state, string? moderatorUserId, string? reason, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            string tid = D.Str(id);
            bool live = state == ModerationStateEnum.None;
            List<string> statements = new List<string>
            {
                "UPDATE forumthreads SET moderationstate = " + D.Enum(state) + ", moderatedutc = " + D.Ts(nowUtc) + ", moderatedbyuserid = " + D.Str(moderatorUserId) + ", moderationreason = " + D.Str(reason) +
                    (live ? String.Empty : ", ispinned = " + D.Bool(false) + ", pinnedutc = NULL, pinnedbyuserid = NULL, trendingscore = 0") + ", updatedutc = " + D.Ts(nowUtc) +
                    " WHERE " + Scope() + " AND id = " + tid + " AND deletedutc IS NULL;",
                ForumCounterSql.RecomputeForum(Pid, ForumCounterSql.ForumOfThread(Pid, tid)),
                "SELECT id FROM forumthreads WHERE " + Scope() + " AND id = " + tid + " AND deletedutc IS NULL AND moderationstate = " + D.Enum(state) + ";"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> PinAsync(string id, string? byUserId, DateTime nowUtc, int maxPinned, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            string tid = D.Str(id);
            List<string> statements = new List<string>
            {
                "UPDATE forums SET threadcount = threadcount WHERE " + Scope() + " AND id IN (" + ForumCounterSql.ForumOfThread(Pid, tid) + ");",
                "UPDATE forumthreads SET ispinned = " + D.Bool(true) + ", pinnedutc = COALESCE(pinnedutc, " + D.Ts(nowUtc) + "), pinnedbyuserid = COALESCE(pinnedbyuserid, " + D.Str(byUserId) + "), updatedutc = " +
                    D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + tid + " AND " + ForumCounterSql.Live("forumthreads") + " AND (ispinned = " + D.Bool(true) +
                    " OR (SELECT COUNT(*) FROM forumthreads p WHERE " + Scope("p") + " AND p.forumid = forumthreads.forumid AND p.ispinned = " + D.Bool(true) + " AND " + ForumCounterSql.Live("p") + ") < " +
                    D.Num(Math.Max(1, maxPinned)) + ") RETURNING id;"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> UnpinAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE forumthreads SET ispinned = " + D.Bool(false) + ", pinnedutc = NULL, pinnedbyuserid = NULL, updatedutc = " + D.Ts(nowUtc) +
                " WHERE " + Scope() + " AND id = " + D.Str(id) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> SetLockedAsync(string id, bool locked, string? byUserId, string? reason, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            string set = locked
                ? "islocked = " + D.Bool(true) + ", lockedutc = COALESCE(lockedutc, " + D.Ts(nowUtc) + "), lockedbyuserid = " + D.Str(byUserId) + ", lockreason = " + D.Str(reason)
                : "islocked = " + D.Bool(false) + ", lockedutc = NULL, lockedbyuserid = NULL, lockreason = NULL";
            DataTable table = await QueryAsync("UPDATE forumthreads SET " + set + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND deletedutc IS NULL RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> MoveAsync(string id, string toForumId, ContentAudienceEnum audience, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            RequireText(toForumId, nameof(toForumId));
            string tid = D.Str(id);
            string to = D.Str(toForumId);
            DataTable before = await QueryAsync("SELECT forumid FROM forumthreads WHERE " + Scope() + " AND id = " + tid + ";", token).ConfigureAwait(false);
            if (before.Rows.Count == 0) return false;
            string from = D.Str(RowReader.GetString(before.Rows[0], "forumid"));
            string open = D.Enum(ReportStatusEnum.Open);
            List<string> statements = new List<string>
            {
                "UPDATE forumthreads SET forumid = " + to + ", audience = " + D.Enum(audience) + ", ispinned = " + D.Bool(false) + ", pinnedutc = NULL, pinnedbyuserid = NULL, updatedutc = " + D.Ts(nowUtc) +
                    " WHERE " + Scope() + " AND id = " + tid + " AND deletedutc IS NULL;",
                "UPDATE reports SET scopeid = " + to + " WHERE " + Scope() + " AND status = " + open + " AND scopetype = 'Forum' AND ((targettype = " + D.Enum(TargetTypeEnum.Thread) + " AND targetid = " + tid +
                    ") OR (targettype = " + D.Enum(TargetTypeEnum.Comment) + " AND targetid IN (SELECT c.id FROM comments c WHERE " + Scope("c") + " AND c.targettype = " + D.Enum(TargetTypeEnum.Thread) +
                    " AND c.targetid = " + tid + ")));",
                ForumCounterSql.RecomputeForum(Pid, from),
                ForumCounterSql.RecomputeForum(Pid, to),
                "SELECT id FROM forumthreads WHERE " + Scope() + " AND id = " + tid + " AND forumid = " + to + ";"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> SetAcceptedAnswerAsync(string threadId, string? commentId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(threadId, nameof(threadId));
            string tid = D.Str(threadId);
            string thread = "c.targettype = " + D.Enum(TargetTypeEnum.Thread) + " AND c.targetid = " + tid;
            List<string> statements = new List<string>
            {
                "UPDATE forumthreads SET commentcount = commentcount WHERE " + Scope() + " AND id = " + tid + ";"
            };
            if (commentId == null)
            {
                statements.Add("UPDATE comments SET isacceptedanswer = " + D.Bool(false) + " WHERE " + Scope() + " AND targettype = " + D.Enum(TargetTypeEnum.Thread) + " AND targetid = " + tid + " AND isacceptedanswer = " + D.Bool(true) + ";");
                statements.Add("UPDATE forumthreads SET acceptedcommentid = NULL, hasacceptedanswer = " + D.Bool(false) + ", lastactivityutc = GREATEST(lastactivityutc, " + D.Ts(nowUtc) + "), updatedutc = " +
                    D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + tid + " AND deletedutc IS NULL RETURNING id;");
            }
            else
            {
                string cid = D.Str(commentId);
                string valid = "EXISTS (SELECT 1 FROM comments c WHERE " + Scope("c") + " AND " + thread + " AND c.id = " + cid + " AND c.deletedutc IS NULL AND c.moderationstate = " + D.Enum(ModerationStateEnum.None) + ")";
                statements.Add("UPDATE comments SET isacceptedanswer = (id = " + cid + ") WHERE " + Scope() + " AND targettype = " + D.Enum(TargetTypeEnum.Thread) + " AND targetid = " + tid +
                    " AND (isacceptedanswer = " + D.Bool(true) + " OR id = " + cid + ") AND EXISTS (SELECT 1 FROM comments v WHERE " + Scope("v") + " AND v.id = " + cid + " AND v.targettype = " +
                    D.Enum(TargetTypeEnum.Thread) + " AND v.targetid = " + tid + " AND v.deletedutc IS NULL AND v.moderationstate = " + D.Enum(ModerationStateEnum.None) + ");");
                statements.Add("UPDATE forumthreads SET acceptedcommentid = " + cid + ", hasacceptedanswer = " + D.Bool(true) + ", lastactivityutc = GREATEST(lastactivityutc, " + D.Ts(nowUtc) + "), updatedutc = " +
                    D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + tid + " AND deletedutc IS NULL AND " + valid + " RETURNING id;");
            }
            statements.Add(ForumCounterSql.RecomputeForum(Pid, ForumCounterSql.ForumOfThread(Pid, tid)));
            statements.Add("SELECT id FROM forumthreads WHERE " + Scope() + " AND id = " + tid + " AND " + (commentId == null ? "acceptedcommentid IS NULL" : "acceptedcommentid = " + D.Str(commentId)) + ";");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> SetFeedbackAsync(string threadId, FeedbackStatusEnum status, string? reviewerUserId, string? reviewCommentId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(threadId, nameof(threadId));
            string tid = D.Str(threadId);
            List<string> statements = new List<string>
            {
                "UPDATE forumthreads SET commentcount = commentcount WHERE " + Scope() + " AND id = " + tid + ";"
            };
            string guard = "deletedutc IS NULL AND type = " + D.Enum(ThreadTypeEnum.Feedback);
            if (reviewCommentId != null)
            {
                string cid = D.Str(reviewCommentId);
                string valid = "EXISTS (SELECT 1 FROM comments c WHERE " + Scope("c") + " AND c.targettype = " + D.Enum(TargetTypeEnum.Thread) + " AND c.targetid = " + tid + " AND c.id = " + cid +
                    " AND c.deletedutc IS NULL AND c.moderationstate = " + D.Enum(ModerationStateEnum.None) + ")";
                guard += " AND " + valid;
                statements.Add("UPDATE comments SET isfeedbackreview = (id = " + cid + ") WHERE " + Scope() + " AND targettype = " + D.Enum(TargetTypeEnum.Thread) + " AND targetid = " + tid +
                    " AND (isfeedbackreview = " + D.Bool(true) + " OR id = " + cid + ") AND EXISTS (SELECT 1 FROM comments v WHERE " + Scope("v") + " AND v.id = " + cid + " AND v.targetid = " + tid +
                    " AND v.deletedutc IS NULL AND v.moderationstate = " + D.Enum(ModerationStateEnum.None) + ");");
            }
            statements.Add("UPDATE forumthreads SET feedbackstatus = " + D.Enum(status) + ", feedbackreviewerid = " + D.Str(reviewerUserId) + ", feedbackreviewcommentid = " +
                (reviewCommentId == null ? "feedbackreviewcommentid" : D.Str(reviewCommentId)) + ", feedbackstatusutc = " + D.Ts(nowUtc) + ", lastactivityutc = GREATEST(lastactivityutc, " + D.Ts(nowUtc) +
                "), updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + tid + " AND " + guard + ";");
            statements.Add("SELECT id FROM forumthreads WHERE " + Scope() + " AND id = " + tid + " AND feedbackstatus = " + D.Enum(status) + " AND feedbackstatusutc = " + D.Ts(nowUtc) + ";");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public Task<long> CountOpenFeedbackAsync(string authorUserId, string? forumId, CancellationToken token = default)
        {
            RequireText(authorUserId, nameof(authorUserId));
            return CountAsync("SELECT COUNT(*) AS cnt FROM forumthreads t WHERE " + Scope("t") + " AND t.type = " + D.Enum(ThreadTypeEnum.Feedback) + " AND t.authoruserid = " + D.Str(authorUserId) +
                " AND t.deletedutc IS NULL AND t.feedbackstatus IN " + D.InList(new[] { FeedbackStatusEnum.Open.ToString(), FeedbackStatusEnum.InReview.ToString() }) +
                (forumId == null ? String.Empty : " AND t.forumid = " + D.Str(forumId)) + ";", token);
        }

        public async Task<bool> SetPromotionAsync(string threadId, string? feedPostId, string? byUserId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(threadId, nameof(threadId));
            string sql = feedPostId == null
                ? "UPDATE forumthreads SET promotedfeedpostid = NULL, promotedbyuserid = NULL, promotedutc = NULL, updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(threadId) + " RETURNING id;"
                : "UPDATE forumthreads SET promotedfeedpostid = " + D.Str(feedPostId) + ", promotedbyuserid = " + D.Str(byUserId) + ", promotedutc = " + D.Ts(nowUtc) + ", updatedutc = " + D.Ts(nowUtc) +
                    " WHERE " + Scope() + " AND id = " + D.Str(threadId) + " AND promotedfeedpostid IS NULL AND deletedutc IS NULL RETURNING id;";
            DataTable table = await QueryAsync(sql, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<EnumerationResult<ForumThread>> EnumerateAsync(EnumerationRequest request, ThreadFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = Conditions(filter);
            string name = request.Ordering.Name;
            if (filter.PinnedFirst) return await EnumerateHotAsync(request, conditions, token, true).ConfigureAwait(false);
            if (String.Equals(name, "Hot", StringComparison.Ordinal) || String.Equals(name, "ScoreDescending", StringComparison.Ordinal))
                return await EnumerateHotAsync(request, conditions, token).ConfigureAwait(false);
            if (String.Equals(name, "Relevance", StringComparison.Ordinal))
            {
                List<string> ranked = filter.CandidateIds ?? new List<string>();
                if (ranked.Count == 0) return new EnumerationResult<ForumThread> { MaxResults = request.MaxResults, EndOfResults = true, TotalRecords = request.CountTotals ? 0 : null, RecordsRemaining = request.CountTotals ? 0 : null };
                EnumerationRequest ordered = new EnumerationRequest
                {
                    Collection = request.Collection,
                    Query = request.Query,
                    MaxResults = request.MaxResults,
                    Skip = request.Skip,
                    Ordering = new EnumerationOrdering { Name = name, Expression = "array_position(ARRAY[" + String.Join(", ", ranked.Select(r => D.Str(r))) + "]::text[], t.id)", Descending = false, KeyKind = EnumerationKeyKindEnum.Number },
                    AfterKey = request.AfterKey,
                    AfterId = request.AfterId,
                    Fingerprint = request.Fingerprint,
                    CountTotals = request.CountTotals,
                    IncludeStaffMetadata = request.IncludeStaffMetadata
                };
                return await EnumerateAsync(ordered, "t.*", "forumthreads t", conditions, Map, token).ConfigureAwait(false);
            }
            return await EnumerateAsync(request, "t.*", "forumthreads t", conditions, Map, token).ConfigureAwait(false);
        }

        public async Task<List<ForumThread>> ListPinnedAsync(EnumerationRequest request, ThreadFilter filter, int max, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = Conditions(filter);
            conditions.Add("t.ispinned = " + D.Bool(true));
            conditions.AddRange(EnumerationConditions(request));
            DataTable table = await QueryAsync("SELECT t.* FROM forumthreads t" + Where(conditions) + " ORDER BY t.pinnedutc DESC, t.id DESC LIMIT " + D.Num(Math.Max(1, max)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<long> CountPinnedAsync(string forumId, CancellationToken token = default)
        {
            RequireText(forumId, nameof(forumId));
            return await CountAsync("SELECT COUNT(*) AS cnt FROM forumthreads t WHERE " + Scope("t") + " AND t.forumid = " + D.Str(forumId) + " AND t.ispinned = " + D.Bool(true) + " AND " +
                ForumCounterSql.Live("t") + ";", token).ConfigureAwait(false);
        }

        public Task<EnumerationResult<ThreadRevision>> EnumerateRevisionsAsync(string threadId, EnumerationRequest request, CancellationToken token = default)
        {
            RequireText(threadId, nameof(threadId));
            ArgumentNullException.ThrowIfNull(request);
            return EnumerateAsync(request, "tv.*", "forumthreadrevisions tv", new List<string> { "tv.threadid = " + D.Str(threadId) }, MapRevision, token);
        }

        public async Task<List<ThreadRevision>> ListRevisionsAsync(IEnumerable<string> threadIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(threadIds);
            List<string> ids = threadIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return new List<ThreadRevision>();
            DataTable table = await QueryAsync("SELECT tv.* FROM forumthreadrevisions tv WHERE " + Scope("tv") + " AND tv.threadid IN " + D.InList(ids) + " ORDER BY tv.threadid, tv.revision;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapRevision).ToList();
        }

        public async Task<bool> FollowAsync(ThreadFollow follow, int maxFollows, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(follow);
            RequireText(follow.ThreadId, nameof(follow.ThreadId));
            RequireText(follow.UserId, nameof(follow.UserId));
            string user = D.Str(follow.UserId);
            List<string> statements = new List<string> { FollowUpsert(follow) };
            string active = Scope("f") + " AND f.userid = " + user + " AND f.isoptedout = " + D.Bool(false);
            statements.Add("DELETE FROM threadfollows WHERE " + Scope() + " AND id IN (SELECT f.id FROM threadfollows f JOIN forumthreads t ON " + Scope("t") + " AND t.id = f.threadid WHERE " + active +
                " AND f.threadid <> " + D.Str(follow.ThreadId) + " ORDER BY t.lastactivityutc DESC, f.id DESC OFFSET " + D.Num(Math.Max(0, maxFollows - 1)) + ");");
            statements.Add("SELECT id FROM threadfollows WHERE " + Scope() + " AND threadid = " + D.Str(follow.ThreadId) + " AND userid = " + user + " AND isoptedout = " + D.Bool(false) + ";");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> UnfollowAsync(string threadId, string userId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(threadId, nameof(threadId));
            RequireText(userId, nameof(userId));
            ThreadFollow optOut = new ThreadFollow
            {
                Id = IdGenerator.Generate(ForumIdPrefixes.ThreadFollow),
                ThreadId = threadId,
                UserId = userId,
                Source = FollowSourceEnum.Explicit,
                IsOptedOut = true,
                CreatedUtc = nowUtc
            };
            await QueryAsync("INSERT INTO threadfollows (propertyid, id, threadid, userid, source, isoptedout, createdutc) VALUES (" + Pid + ", " + D.Str(optOut.Id) + ", " + D.Str(threadId) + ", " + D.Str(userId) + ", " +
                D.Enum(FollowSourceEnum.Explicit) + ", " + D.Bool(true) + ", " + D.Ts(nowUtc) + ") ON CONFLICT (propertyid, threadid, userid) DO UPDATE SET isoptedout = " + D.Bool(true) + ";", token).ConfigureAwait(false);
            return true;
        }

        public async Task<ThreadFollow?> ReadFollowAsync(string threadId, string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(threadId) || String.IsNullOrEmpty(userId)) return null;
            DataTable table = await QueryAsync("SELECT * FROM threadfollows WHERE " + Scope() + " AND threadid = " + D.Str(threadId) + " AND userid = " + D.Str(userId) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MapFollow(table.Rows[0]);
        }

        public async Task<HashSet<string>> FollowedAmongAsync(string userId, IEnumerable<string> threadIds, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            ArgumentNullException.ThrowIfNull(threadIds);
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            List<string> ids = threadIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT threadid FROM threadfollows WHERE " + Scope() + " AND userid = " + D.Str(userId) + " AND isoptedout = " + D.Bool(false) + " AND threadid IN " + D.InList(ids) + ";", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result.Add(RowReader.GetString(row, "threadid"));
            return result;
        }

        public async Task<List<string>> FollowerIdsAsync(string threadId, string? afterUserId, int limit, CancellationToken token = default)
        {
            RequireText(threadId, nameof(threadId));
            DataTable table = await QueryAsync("SELECT userid FROM threadfollows WHERE " + Scope() + " AND threadid = " + D.Str(threadId) + " AND isoptedout = " + D.Bool(false) +
                (afterUserId == null ? String.Empty : " AND userid > " + D.Str(afterUserId)) + " ORDER BY userid LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "userid")).ToList();
        }

        public Task<long> CountFollowersAsync(string threadId, CancellationToken token = default)
        {
            RequireText(threadId, nameof(threadId));
            return CountAsync("SELECT COUNT(*) AS cnt FROM threadfollows WHERE " + Scope() + " AND threadid = " + D.Str(threadId) + " AND isoptedout = " + D.Bool(false) + ";", token);
        }

        public async Task<EnumerationResult<FollowRow>> EnumerateFollowsAsync(string userId, EnumerationRequest request, TargetTypeEnum? targetType, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            ArgumentNullException.ThrowIfNull(request);
            string user = D.Str(userId);
            List<string> branches = new List<string>();
            if (targetType == null || targetType == TargetTypeEnum.Forum)
            {
                branches.Add("SELECT ff.propertyid AS propertyid, ff.id AS id, 'Forum' AS targettype, 'ForumFollow' AS metatype, ff.forumid AS targetid, ff.createdutc AS createdutc FROM forumfollows ff JOIN forums fo ON " +
                    Scope("fo") + " AND fo.id = ff.forumid AND fo.deletedutc IS NULL WHERE " + Scope("ff") + " AND ff.userid = " + user);
            }
            if (targetType == null || targetType == TargetTypeEnum.Thread)
            {
                branches.Add("SELECT tf.propertyid AS propertyid, tf.id AS id, 'Thread' AS targettype, 'ThreadFollow' AS metatype, tf.threadid AS targetid, tf.createdutc AS createdutc FROM threadfollows tf JOIN forumthreads th ON " +
                    Scope("th") + " AND th.id = tf.threadid AND th.deletedutc IS NULL WHERE " + Scope("tf") + " AND tf.userid = " + user + " AND tf.isoptedout = " + D.Bool(false));
            }
            EnumerationQuery q = request.Query;
            List<string> conditions = new List<string>();
            List<string> labels = q.Labels.Select(MetadataRules.Normalize).Distinct(StringComparer.Ordinal).ToList();
            string match = Scope("ml") + " AND ml.targettype = x.metatype AND ml.targetid = x.id";
            if (labels.Count > 0)
            {
                if (q.LabelMatch == LabelMatchEnum.Any) conditions.Add("EXISTS (SELECT 1 FROM labels ml WHERE " + match + " AND ml.labelnormalized IN " + D.InList(labels) + ")");
                else foreach (string label in labels) conditions.Add("EXISTS (SELECT 1 FROM labels ml WHERE " + match + " AND ml.labelnormalized = " + D.Str(label) + ")");
            }
            foreach (KeyValuePair<string, string> tag in q.Tags)
            {
                string condition = "EXISTS (SELECT 1 FROM tags mt WHERE " + Scope("mt") + " AND mt.targettype = x.metatype AND mt.targetid = x.id AND mt.tagkeynormalized = " + D.Str(MetadataRules.Normalize(tag.Key));
                if (tag.Value != "*") condition += " AND mt.tagvalue = " + D.Str(tag.Value ?? String.Empty);
                conditions.Add(condition + ")");
            }
            EnumerationRequest stripped = WithoutMetadata(request);
            return await EnumerateAsync(stripped, "x.id, x.targettype, x.targetid, x.createdutc", "(" + String.Join(" UNION ALL ", branches) + ") x", conditions, row => new FollowRow
            {
                Id = RowReader.GetString(row, "id"),
                TargetType = RowReader.GetEnum(row, "targettype", TargetTypeEnum.Thread),
                TargetId = RowReader.GetString(row, "targetid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            }, token).ConfigureAwait(false);
        }

        public async Task MarkReadAsync(string userId, string threadId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            RequireText(threadId, nameof(threadId));
            await QueryAsync("INSERT INTO threadreads (propertyid, userid, threadid, lastreadutc) VALUES (" + Pid + ", " + D.Str(userId) + ", " + D.Str(threadId) + ", " + D.Ts(nowUtc) +
                ") ON CONFLICT (propertyid, userid, threadid) DO UPDATE SET lastreadutc = GREATEST(threadreads.lastreadutc, excluded.lastreadutc);", token).ConfigureAwait(false);
        }

        public async Task<Dictionary<string, DateTime>> ReadTimesAsync(string userId, IEnumerable<string> threadIds, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            ArgumentNullException.ThrowIfNull(threadIds);
            Dictionary<string, DateTime> result = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            List<string> ids = threadIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT threadid, lastreadutc FROM threadreads WHERE " + Scope() + " AND userid = " + D.Str(userId) + " AND threadid IN " + D.InList(ids) + ";", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result[RowReader.GetString(row, "threadid")] = RowReader.GetDateTime(row, "lastreadutc");
            return result;
        }

        public async Task<int> PruneReadsAsync(DateTime beforeUtc, int limit, CancellationToken token = default)
        {
            int max = Math.Max(1, limit);
            string query =
                "WITH f AS (DELETE FROM forumreads WHERE " + Scope() + " AND (userid, forumid) IN (SELECT r.userid, r.forumid FROM forumreads r WHERE " + Scope("r") + " AND r.lastreadutc < " + D.Ts(beforeUtc) + " LIMIT " + D.Num(max) + ") RETURNING forumid), " +
                "t AS (DELETE FROM threadreads WHERE " + Scope() + " AND (userid, threadid) IN (SELECT r.userid, r.threadid FROM threadreads r WHERE " + Scope("r") + " AND r.lastreadutc < " + D.Ts(beforeUtc) + " LIMIT " + D.Num(max) + ") RETURNING threadid) " +
                "SELECT (SELECT COUNT(*) FROM f) AS forums, (SELECT COUNT(*) FROM t) AS threads;";
            DataTable table = await QueryAsync(query, token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return 0;
            return (int)(RowReader.GetLong(table.Rows[0], "forums") + RowReader.GetLong(table.Rows[0], "threads"));
        }

        public async Task<List<TrendingInput>> TrendingBatchAsync(DateTime windowStartUtc, string? afterId, int limit, CancellationToken token = default)
        {
            List<string> conditions = new List<string>
            {
                Scope("t"),
                ForumCounterSql.Live("t"),
                "t.lastactivityutc >= " + D.Ts(windowStartUtc),
                "EXISTS (SELECT 1 FROM forums fo WHERE " + Scope("fo") + " AND fo.id = t.forumid AND fo.isarchived = " + D.Bool(false) + " AND fo.deletedutc IS NULL)"
            };
            if (!String.IsNullOrEmpty(afterId)) conditions.Add("t.id > " + D.Str(afterId));
            DataTable table = await QueryAsync("SELECT t.id, t.publishedutc, t.reactiontotal, t.commentcount, t.uniquecommentercount FROM forumthreads t" + Where(conditions) +
                " ORDER BY t.id LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => new TrendingInput
            {
                ThreadId = RowReader.GetString(r, "id"),
                PublishedUtc = RowReader.GetDateTime(r, "publishedutc"),
                Reactions = RowReader.GetInt(r, "reactiontotal"),
                Comments = RowReader.GetInt(r, "commentcount"),
                UniqueCommenters = RowReader.GetInt(r, "uniquecommentercount")
            }).ToList();
        }

        public async Task<int> SetScoresAsync(Dictionary<string, double> scores, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(scores);
            if (scores.Count == 0) return 0;
            string values = String.Join(", ", scores.Select(s => "(" + D.Str(s.Key) + ", " + D.Num(s.Value) + "::double precision)"));
            DataTable table = await QueryAsync("UPDATE forumthreads SET trendingscore = v.score, trendingcomputedutc = " + D.Ts(nowUtc) + " FROM (VALUES " + values + ") AS v(id, score) WHERE " +
                Scope("forumthreads") + " AND forumthreads.id = v.id RETURNING forumthreads.id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public async Task<int> ZeroOutsideWindowAsync(DateTime windowStartUtc, DateTime nowUtc, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("UPDATE forumthreads SET trendingscore = 0, trendingcomputedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND trendingscore > 0 AND (lastactivityutc < " +
                D.Ts(windowStartUtc) + " OR deletedutc IS NOT NULL OR moderationstate <> " + D.Enum(ModerationStateEnum.None) + " OR publishedutc IS NULL OR NOT EXISTS (SELECT 1 FROM forums fo WHERE " +
                Scope("fo") + " AND fo.id = forumthreads.forumid AND fo.isarchived = " + D.Bool(false) + " AND fo.deletedutc IS NULL)) RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public Task<long> CountScoredAsync(CancellationToken token = default)
        {
            return CountAsync("SELECT COUNT(*) AS cnt FROM forumthreads WHERE " + Scope() + " AND trendingscore > 0;", token);
        }

        public async Task<int> RecomputeOpenReportCountAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE forumthreads SET openreportcount = (SELECT COUNT(*) FROM reports r WHERE " + Scope("r") + " AND r.targettype = " + D.Enum(TargetTypeEnum.Thread) +
                " AND r.targetid = forumthreads.id AND r.status = " + D.Enum(ReportStatusEnum.Open) + ") WHERE " + Scope() + " AND id = " + D.Str(id) + " RETURNING openreportcount;", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? 0 : RowReader.GetInt(table.Rows[0], "openreportcount");
        }

        public async Task RecomputeCommentsAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            string tid = D.Str(id);
            ForumThread? thread = await ReadAsync(id, token).ConfigureAwait(false);
            if (thread == null) return;
            List<string> statements = new List<string>();
            if (thread.Kind == ThreadKindEnum.LinkedFeedPost && thread.SourceId != null) statements.Add(ForumCounterSql.RecomputeLinkedThreadComments(Pid, D.Str(thread.SourceId)));
            else statements.Add(ForumCounterSql.RecomputeThreadComments(Pid, tid));
            statements.Add(ForumCounterSql.RecomputeForum(Pid, ForumCounterSql.ForumOfThread(Pid, tid)));
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        public async Task TouchActivityAsync(string id, DateTime activityUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            string tid = D.Str(id);
            await TransactionAsync(new List<string>
            {
                "UPDATE forumthreads SET lastactivityutc = GREATEST(lastactivityutc, " + D.Ts(activityUtc) + ") WHERE " + Scope() + " AND id = " + tid + ";",
                ForumCounterSql.RecomputeForum(Pid, ForumCounterSql.ForumOfThread(Pid, tid))
            }, token).ConfigureAwait(false);
        }

        public async Task<List<ForumThread>> ListByAuthorAsync(string authorUserId, string? afterId, int limit, bool includeDeleted, CancellationToken token = default)
        {
            RequireText(authorUserId, nameof(authorUserId));
            List<string> conditions = new List<string> { Scope("t"), "t.authoruserid = " + D.Str(authorUserId), "t.kind = " + D.Enum(ThreadKindEnum.Original) };
            if (!includeDeleted) conditions.Add("t.deletedutc IS NULL");
            if (!String.IsNullOrEmpty(afterId)) conditions.Add("t.id > " + D.Str(afterId));
            DataTable table = await QueryAsync("SELECT t.* FROM forumthreads t" + Where(conditions) + " ORDER BY t.id ASC LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<int> DeleteFollowsForUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            string user = D.Str(userId);
            List<string> statements = new List<string>();
            statements.AddRange(Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.ThreadFollow, "SELECT id FROM threadfollows WHERE " + Scope() + " AND userid = " + user));
            statements.AddRange(Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.ForumFollow, "SELECT id FROM forumfollows WHERE " + Scope() + " AND userid = " + user));
            statements.Add("DELETE FROM threadfollows WHERE " + Scope() + " AND userid = " + user + ";");
            statements.Add("DELETE FROM forumfollows WHERE " + Scope() + " AND userid = " + user + ";");
            statements.Add("DELETE FROM threadreads WHERE " + Scope() + " AND userid = " + user + ";");
            statements.Add("DELETE FROM forumreads WHERE " + Scope() + " AND userid = " + user + ";");
            statements.Add("DELETE FROM forummemberprefs WHERE " + Scope() + " AND userid = " + user + " RETURNING userid;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public async Task<bool> PurgeAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            string tid = D.Str(id);
            List<string> statements = new List<string>();
            statements.AddRange(Driver.Metadata.DeleteStatements(TargetTypeEnum.Thread, id));
            statements.AddRange(Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.ThreadRevision, "SELECT id FROM forumthreadrevisions WHERE " + Scope() + " AND threadid = " + tid));
            statements.Add("DELETE FROM forumthreadrevisions WHERE " + Scope() + " AND threadid = " + tid + ";");
            statements.Add("DELETE FROM mentions WHERE " + Scope() + " AND sourcetype = " + D.Enum(TargetTypeEnum.Thread) + " AND sourceid = " + tid + ";");
            statements.Add("UPDATE forumthreads SET body = NULL, excerpt = " + D.Str(String.Empty) + ", linkpreviewid = NULL, revisioncount = " + D.Num(0) + ", ispinned = " + D.Bool(false) +
                ", purgedutc = " + D.Ts(nowUtc) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + tid + " AND purgedutc IS NULL RETURNING id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<ThreadActivityCounts> ActivityCountsAsync(DateTime fromUtc, CancellationToken token = default)
        {
            ThreadActivityCounts counts = new ThreadActivityCounts();
            counts.ActiveThreads = await CountAsync("SELECT COUNT(*) AS cnt FROM forumthreads t WHERE " + Scope("t") + " AND " + ForumCounterSql.Live("t") + " AND t.lastactivityutc >= " + D.Ts(fromUtc) + ";", token).ConfigureAwait(false);
            counts.OpenReviewRequests = await CountAsync("SELECT COUNT(*) AS cnt FROM forumthreads t WHERE " + Scope("t") + " AND t.deletedutc IS NULL AND t.type = " + D.Enum(ThreadTypeEnum.Feedback) +
                " AND t.feedbackstatus IN " + D.InList(new[] { FeedbackStatusEnum.Open.ToString(), FeedbackStatusEnum.InReview.ToString() }) + ";", token).ConfigureAwait(false);
            counts.UnansweredQuestions = await CountAsync("SELECT COUNT(*) AS cnt FROM forumthreads t WHERE " + Scope("t") + " AND " + ForumCounterSql.Live("t") + " AND t.type = " + D.Enum(ThreadTypeEnum.Question) +
                " AND t.hasacceptedanswer = " + D.Bool(false) + ";", token).ConfigureAwait(false);
            return counts;
        }

        public async Task<List<DailyCount>> DailyAsync(string what, DateTime fromUtc, int days, CancellationToken token = default)
        {
            string from = D.Ts(fromUtc);
            string sql;
            switch (what)
            {
                case "threads":
                    sql = "SELECT " + D.DayOf("t.publishedutc") + " AS day, COUNT(*) AS cnt FROM forumthreads t WHERE " + Scope("t") + " AND t.deletedutc IS NULL AND t.publishedutc >= " + from + " GROUP BY 1;";
                    break;
                case "comments":
                    sql = "SELECT " + D.DayOf("c.createdutc") + " AS day, COUNT(*) AS cnt FROM comments c WHERE " + Scope("c") + " AND c.targettype = " + D.Enum(TargetTypeEnum.Thread) + " AND c.createdutc >= " + from + " GROUP BY 1;";
                    break;
                case "votes":
                    sql = "SELECT " + D.DayOf("v.createdutc") + " AS day, COUNT(DISTINCT v.pollid || ':' || v.userid) AS cnt FROM pollvotes v WHERE " + Scope("v") + " AND v.createdutc >= " + from + " GROUP BY 1;";
                    break;
                default:
                    throw new ArgumentException("Unknown daily series " + what + ".", nameof(what));
            }
            DataTable rows = await QueryAsync(sql, token).ConfigureAwait(false);
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

        public async Task<Dictionary<string, long>> ThreadsInWindowByForumAsync(DateTime fromUtc, CancellationToken token = default)
        {
            DataTable rows = await QueryAsync("SELECT t.forumid, COUNT(*) AS cnt FROM forumthreads t WHERE " + Scope("t") + " AND t.deletedutc IS NULL AND t.publishedutc >= " + D.Ts(fromUtc) + " GROUP BY t.forumid;", token).ConfigureAwait(false);
            Dictionary<string, long> result = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (DataRow row in rows.Rows) result[RowReader.GetString(row, "forumid")] = RowReader.GetLong(row, "cnt");
            return result;
        }

        internal List<string> PollInsertStatements(Poll poll)
        {
            List<string> statements = new List<string>
            {
                "INSERT INTO polls (propertyid, id, threadid, multiplechoice, maxchoices, resultsvisibility, votervisibility, allowvotechange, closesutc, totalvoters, createdutc, updatedutc) VALUES (" +
                    Pid + ", " + D.Str(poll.Id) + ", " + D.Str(poll.ThreadId) + ", " + D.Bool(poll.MultipleChoice) + ", " + D.Num(poll.MaxChoices) + ", " + D.Enum(poll.ResultsVisibility) + ", " +
                    D.Enum(poll.VoterVisibility) + ", " + D.Bool(poll.AllowVoteChange) + ", " + D.Ts(poll.ClosesUtc) + ", " + D.Num(0) + ", " + D.Ts(poll.CreatedUtc) + ", " + D.Ts(poll.UpdatedUtc) + ");"
            };
            if (poll.Options.Count > 0)
            {
                statements.Add("INSERT INTO polloptions (propertyid, id, pollid, label, labelnormalized, position, votecount, createdutc) VALUES " +
                    String.Join(", ", poll.Options.Select(o => "(" + Pid + ", " + D.Str(o.Id) + ", " + D.Str(poll.Id) + ", " + D.Str(o.Label) + ", " + D.Str(o.LabelNormalized) + ", " + D.Num(o.Position) + ", " +
                        D.Num(0) + ", " + D.Ts(o.CreatedUtc) + ")")) + ";");
            }
            return statements;
        }

        private string FollowUpsert(ThreadFollow follow)
        {
            string explicitFollow = follow.Source == FollowSourceEnum.Explicit ? "isoptedout = " + D.Bool(false) + ", source = excluded.source" : "isoptedout = threadfollows.isoptedout";
            return "INSERT INTO threadfollows (propertyid, id, threadid, userid, source, isoptedout, createdutc) VALUES (" + Pid + ", " + D.Str(follow.Id) + ", " + D.Str(follow.ThreadId) + ", " +
                D.Str(follow.UserId) + ", " + D.Enum(follow.Source) + ", " + D.Bool(false) + ", " + D.Ts(follow.CreatedUtc) + ") ON CONFLICT (propertyid, threadid, userid) DO UPDATE SET " + explicitFollow + ";";
        }

        private async Task<EnumerationResult<ForumThread>> EnumerateHotAsync(EnumerationRequest request, List<string> conditions, CancellationToken token, bool pinnedFirst = false)
        {
            bool active = request.Ordering.Name == "Active";
            string Position(string? key, string id) => pinnedFirst ? PinnedKeyset(key, id, active) : HotKeyset(key, id);
            string order = pinnedFirst
                ? "t.ispinned DESC, CASE WHEN t.ispinned THEN COALESCE(t.pinnedutc,t.createdutc) END DESC, "
                    + (active ? String.Empty : "CASE WHEN NOT t.ispinned THEN t.trendingscore END DESC, ")
                    + "CASE WHEN NOT t.ispinned THEN t.lastactivityutc END DESC, t.id DESC"
                : "t.trendingscore DESC, t.lastactivityutc DESC, t.id DESC";
            List<string> all = new List<string>(conditions);
            all.AddRange(EnumerationConditions(request));
            List<string> paged = new List<string>(all);
            if (request.HasPosition) paged.Add(Position(request.AfterKey, request.AfterId!));
            string sql = "SELECT t.* FROM forumthreads t" + Where(paged) + " ORDER BY " + order + " LIMIT " + D.Num(request.MaxResults + 1) +
                (!request.HasPosition && request.Skip > 0 ? " OFFSET " + D.Num(request.Skip) : String.Empty) + ";";
            DataTable table = await QueryAsync(sql, token).ConfigureAwait(false);
            List<ForumThread> rows = table.Rows.Cast<DataRow>().Select(Map).ToList();
            bool more = rows.Count > request.MaxResults;
            if (more) rows = rows.Take(request.MaxResults).ToList();
            EnumerationResult<ForumThread> result = new EnumerationResult<ForumThread>
            {
                MaxResults = request.MaxResults,
                Skip = request.HasPosition ? 0 : request.Skip,
                EndOfResults = !more,
                TimestampUtc = DateTime.UtcNow,
                Objects = rows
            };
            string? lastKey = null;
            string? lastId = null;
            if (rows.Count > 0)
            {
                ForumThread last = rows[rows.Count - 1];
                lastKey = last.TrendingScore.ToString("R", CultureInfo.InvariantCulture) + "|" + last.LastActivityUtc.ToString("O", CultureInfo.InvariantCulture);
                if (pinnedFirst)
                    lastKey = last.IsPinned ? "p|" + (last.PinnedUtc ?? last.CreatedUtc).ToString("O", CultureInfo.InvariantCulture)
                        : "u|" + (active ? last.LastActivityUtc.ToString("O", CultureInfo.InvariantCulture) : lastKey);
                lastId = last.Id;
                if (more) result.ContinuationToken = ContinuationTokens.Encode(request.Fingerprint, request.Ordering.Name, lastKey, lastId);
            }
            if (request.CountTotals)
            {
                result.TotalRecords = await CountAsync("SELECT COUNT(*) AS cnt FROM forumthreads t" + Where(all) + ";", token).ConfigureAwait(false);
                if (!more) result.RecordsRemaining = 0;
                else
                {
                    List<string> after = new List<string>(all) { Position(lastKey, lastId!) };
                    result.RecordsRemaining = await CountAsync("SELECT COUNT(*) AS cnt FROM forumthreads t" + Where(after) + ";", token).ConfigureAwait(false);
                }
            }
            return result;
        }

        private string PinnedKeyset(string? key, string id, bool active)
        {
            if (key == null || key.Length < 3 || (key[..2] != "p|" && key[..2] != "u|")) throw ContinuationTokens.Invalid();
            bool pinned = key.StartsWith("p|", StringComparison.Ordinal);
            string remainder = key[2..];
            if (!pinned && !active) return "(NOT t.ispinned AND " + HotKeyset(remainder, id) + ")";
            if (!DateTime.TryParse(remainder, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime time)) throw ContinuationTokens.Invalid();
            string column = pinned ? "COALESCE(t.pinnedutc,t.createdutc)" : "t.lastactivityutc";
            string stamp = D.Ts(DateTime.SpecifyKind(time, DateTimeKind.Utc));
            string after = "(" + column + " < " + stamp + " OR (" + column + " = " + stamp + " AND t.id < " + D.Str(id) + "))";
            return pinned ? "(NOT t.ispinned OR (t.ispinned AND " + after + "))" : "(NOT t.ispinned AND " + after + ")";
        }

        private string HotKeyset(string? key, string id)
        {
            if (key == null) throw ContinuationTokens.Invalid();
            string[] parts = key.Split('|');
            if (parts.Length != 2 || !Double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double score)
                || !Double.IsFinite(score) || !DateTime.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime activity))
                throw ContinuationTokens.Invalid();
            string s = D.Num(score) + "::double precision";
            string a = D.Ts(DateTime.SpecifyKind(activity, DateTimeKind.Utc));
            string i = D.Str(id);
            return "(t.trendingscore < " + s + " OR (t.trendingscore = " + s + " AND (t.lastactivityutc < " + a + " OR (t.lastactivityutc = " + a + " AND t.id < " + i + "))))";
        }

        private List<string> Conditions(ThreadFilter filter)
        {
            List<string> conditions = new List<string>();
            conditions.Add(StatesCondition(filter));
            if (!String.IsNullOrEmpty(filter.ForumId)) conditions.Add("t.forumid = " + D.Str(filter.ForumId));
            if (filter.ForumIds.Count > 0) conditions.Add("t.forumid IN " + D.InList(filter.ForumIds));
            if (filter.ScopeForumIds != null) conditions.Add(filter.ScopeForumIds.Count == 0 ? "1 = 0" : "t.forumid IN " + D.InList(filter.ScopeForumIds));
            if (filter.Types.Count > 0) conditions.Add("t.type IN " + D.InList(filter.Types.Select(x => x.ToString())));
            if (filter.Answered != null) conditions.Add("t.type = " + D.Enum(ThreadTypeEnum.Question) + " AND t.hasacceptedanswer = " + D.Bool(filter.Answered.Value));
            if (filter.Unanswered) conditions.Add("t.type = " + D.Enum(ThreadTypeEnum.Question) + " AND t.hasacceptedanswer = " + D.Bool(false) + " AND t.commentcount = 0");
            if (filter.FeedbackStatuses.Count > 0) conditions.Add("t.type = " + D.Enum(ThreadTypeEnum.Feedback) + " AND t.feedbackstatus IN " + D.InList(filter.FeedbackStatuses.Select(x => x.ToString())));
            if (filter.PollOpen != null)
            {
                string open = "EXISTS (SELECT 1 FROM polls p WHERE " + Scope("p") + " AND p.threadid = t.id AND p.closedutc IS NULL AND (p.closesutc IS NULL OR p.closesutc > " + D.Ts(filter.NowUtc) + "))";
                conditions.Add("t.type = " + D.Enum(ThreadTypeEnum.Poll) + " AND " + (filter.PollOpen.Value ? open : "NOT " + open));
            }
            if (!String.IsNullOrEmpty(filter.AuthorUserId)) conditions.Add("t.authoruserid = " + D.Str(filter.AuthorUserId));
            if (!String.IsNullOrEmpty(filter.FollowedByUserId))
                conditions.Add("EXISTS (SELECT 1 FROM threadfollows tf WHERE " + Scope("tf") + " AND tf.threadid = t.id AND tf.userid = " + D.Str(filter.FollowedByUserId) + " AND tf.isoptedout = " + D.Bool(false) + ")");
            if (!String.IsNullOrEmpty(filter.UnreadForUserId))
            {
                string user = D.Str(filter.UnreadForUserId);
                conditions.Add("t.lastactivityutc > COALESCE(GREATEST((SELECT tr.lastreadutc FROM threadreads tr WHERE " + Scope("tr") + " AND tr.userid = " + user + " AND tr.threadid = t.id), " +
                    "(SELECT fr.lastreadutc FROM forumreads fr WHERE " + Scope("fr") + " AND fr.userid = " + user + " AND fr.forumid = t.forumid)), " + D.Ts(filter.UnreadWindowStartUtc ?? DateTime.MinValue.ToUniversalTime()) + ")");
            }
            if (filter.HasMedia) conditions.Add("t.attachmentcount > 0");
            if (filter.HasVideo) conditions.Add("t.hasvideo = " + D.Bool(true));
            if (filter.Audiences != null) conditions.Add(filter.Audiences.Count == 0 ? "1 = 0" : "t.audience IN " + D.InList(filter.Audiences.Select(a => a.ToString())));
            if (filter.AudienceFilter.Count > 0) conditions.Add("t.audience IN " + D.InList(filter.AudienceFilter.Select(a => a.ToString())));
            if (filter.HiddenAuthorIds.Count > 0) conditions.Add("t.authoruserid NOT IN " + D.InList(filter.HiddenAuthorIds));
            if (filter.Pinned != null) conditions.Add("t.ispinned = " + D.Bool(filter.Pinned.Value));
            if (filter.Locked != null) conditions.Add("t.islocked = " + D.Bool(filter.Locked.Value));
            if (filter.Promoted != null) conditions.Add(filter.Promoted.Value ? "t.promotedfeedpostid IS NOT NULL" : "t.promotedfeedpostid IS NULL");
            if (filter.MinScore != null) conditions.Add("t.trendingscore >= " + D.Num(filter.MinScore.Value));
            if (filter.ExcludeArchivedForums)
                conditions.Add("EXISTS (SELECT 1 FROM forums fa WHERE " + Scope("fa") + " AND fa.id = t.forumid AND fa.isarchived = " + D.Bool(false) + ")");
            if (filter.CandidateIds != null) conditions.Add(filter.CandidateIds.Count == 0 ? "1 = 0" : "t.id IN " + D.InList(filter.CandidateIds));
            return conditions;
        }

        private string StatesCondition(ThreadFilter filter)
        {
            string published = "(" + ForumCounterSql.Live("t") + LiveSource() + ")";
            if (filter.States == null) return published;
            List<string> parts = new List<string>();
            foreach (ThreadStateEnum state in filter.States.Distinct())
            {
                string part;
                switch (state)
                {
                    case ThreadStateEnum.Published:
                        parts.Add(published);
                        continue;
                    case ThreadStateEnum.Processing:
                        part = "(t.deletedutc IS NULL AND t.moderationstate = " + D.Enum(ModerationStateEnum.None) + " AND t.publishedutc IS NULL)";
                        break;
                    case ThreadStateEnum.Hidden:
                        part = "(t.deletedutc IS NULL AND t.moderationstate = " + D.Enum(ModerationStateEnum.Hidden) + ")";
                        break;
                    case ThreadStateEnum.Removed:
                        part = "(t.deletedutc IS NULL AND t.moderationstate = " + D.Enum(ModerationStateEnum.Removed) + ")";
                        break;
                    default:
                        part = "(t.deletedutc IS NOT NULL)";
                        break;
                }
                if (!String.IsNullOrEmpty(filter.StateOwnerUserId)) part = "(" + part + " AND t.authoruserid = " + D.Str(filter.StateOwnerUserId) + ")";
                parts.Add(part);
            }
            return parts.Count == 0 ? "1 = 0" : "(" + String.Join(" OR ", parts) + ")";
        }

        private string LiveSource()
        {
            return " AND (t.kind <> " + D.Enum(ThreadKindEnum.LinkedFeedPost) + " OR EXISTS (SELECT 1 FROM feedposts lf WHERE " + Scope("lf") + " AND lf.id = t.sourceid AND lf.deletedutc IS NULL AND lf.moderationstate = " +
                D.Enum(ModerationStateEnum.None) + " AND lf.publishedutc IS NOT NULL))";
        }

        private static EnumerationRequest WithoutMetadata(EnumerationRequest request)
        {
            EnumerationQuery q = request.Query;
            EnumerationQuery copy = new EnumerationQuery
            {
                MaxResults = q.MaxResults,
                ContinuationToken = q.ContinuationToken,
                Skip = q.Skip,
                Ordering = q.Ordering,
                Search = q.Search,
                CreatedAfterUtc = q.CreatedAfterUtc,
                CreatedBeforeUtc = q.CreatedBeforeUtc,
                LabelMatch = q.LabelMatch
            };
            return new EnumerationRequest
            {
                Collection = request.Collection,
                Query = copy,
                MaxResults = request.MaxResults,
                Skip = request.Skip,
                Ordering = request.Ordering,
                AfterKey = request.AfterKey,
                AfterId = request.AfterId,
                Fingerprint = request.Fingerprint,
                CountTotals = request.CountTotals,
                IncludeStaffMetadata = request.IncludeStaffMetadata
            };
        }

        internal static ForumThread Map(DataRow row)
        {
            return new ForumThread
            {
                Id = RowReader.GetString(row, "id"),
                ForumId = RowReader.GetString(row, "forumid"),
                Kind = RowReader.GetEnum(row, "kind", ThreadKindEnum.Original),
                Type = RowReader.GetEnum(row, "type", ThreadTypeEnum.Discussion),
                AuthorUserId = RowReader.GetString(row, "authoruserid"),
                Title = RowReader.GetString(row, "title"),
                Body = RowReader.GetNullableString(row, "body"),
                Excerpt = RowReader.GetString(row, "excerpt"),
                LinkPreviewId = RowReader.GetNullableString(row, "linkpreviewid"),
                ShowLinkPreview = RowReader.GetBool(row, "showlinkpreview"),
                Audience = RowReader.GetEnum(row, "audience", ContentAudienceEnum.Members),
                SourceType = RowReader.GetNullableString(row, "sourcetype"),
                SourceId = RowReader.GetNullableString(row, "sourceid"),
                CommentTargetType = RowReader.GetEnum(row, "committargettype", TargetTypeEnum.Thread),
                CommentTargetId = RowReader.GetString(row, "committargetid"),
                PromotedFeedPostId = RowReader.GetNullableString(row, "promotedfeedpostid"),
                PromotedByUserId = RowReader.GetNullableString(row, "promotedbyuserid"),
                PromotedUtc = RowReader.GetNullableDateTime(row, "promotedutc"),
                IsPinned = RowReader.GetBool(row, "ispinned"),
                PinnedUtc = RowReader.GetNullableDateTime(row, "pinnedutc"),
                PinnedByUserId = RowReader.GetNullableString(row, "pinnedbyuserid"),
                IsLocked = RowReader.GetBool(row, "islocked"),
                LockedUtc = RowReader.GetNullableDateTime(row, "lockedutc"),
                LockedByUserId = RowReader.GetNullableString(row, "lockedbyuserid"),
                LockReason = RowReader.GetNullableString(row, "lockreason"),
                AcceptedCommentId = RowReader.GetNullableString(row, "acceptedcommentid"),
                HasAcceptedAnswer = RowReader.GetBool(row, "hasacceptedanswer"),
                FeedbackStatus = RowReader.GetNullableEnum<FeedbackStatusEnum>(row, "feedbackstatus"),
                FeedbackFocus = RowReader.GetNullableString(row, "feedbackfocus"),
                FeedbackReviewerId = RowReader.GetNullableString(row, "feedbackreviewerid"),
                FeedbackReviewCommentId = RowReader.GetNullableString(row, "feedbackreviewcommentid"),
                FeedbackStatusUtc = RowReader.GetNullableDateTime(row, "feedbackstatusutc"),
                AttachmentCount = RowReader.GetInt(row, "attachmentcount"),
                HasVideo = RowReader.GetBool(row, "hasvideo"),
                CommentCount = RowReader.GetInt(row, "commentcount"),
                UniqueCommenterCount = RowReader.GetInt(row, "uniquecommentercount"),
                ReactionTotal = RowReader.GetInt(row, "reactiontotal"),
                OpenReportCount = RowReader.GetInt(row, "openreportcount"),
                TrendingScore = RowReader.GetDouble(row, "trendingscore"),
                TrendingComputedUtc = RowReader.GetNullableDateTime(row, "trendingcomputedutc"),
                LastActivityUtc = RowReader.GetDateTime(row, "lastactivityutc"),
                LastCommentUtc = RowReader.GetNullableDateTime(row, "lastcommentutc"),
                EditedUtc = RowReader.GetNullableDateTime(row, "editedutc"),
                RevisionCount = RowReader.GetInt(row, "revisioncount"),
                ModerationState = RowReader.GetEnum(row, "moderationstate", ModerationStateEnum.None),
                ModeratedUtc = RowReader.GetNullableDateTime(row, "moderatedutc"),
                ModeratedByUserId = RowReader.GetNullableString(row, "moderatedbyuserid"),
                ModerationReason = RowReader.GetNullableString(row, "moderationreason"),
                PublishedUtc = RowReader.GetNullableDateTime(row, "publishedutc"),
                DeletedUtc = RowReader.GetNullableDateTime(row, "deletedutc"),
                DeletedByUserId = RowReader.GetNullableString(row, "deletedbyuserid"),
                PurgedUtc = RowReader.GetNullableDateTime(row, "purgedutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        private static ThreadRevision MapRevision(DataRow row)
        {
            return new ThreadRevision
            {
                Id = RowReader.GetString(row, "id"),
                ThreadId = RowReader.GetString(row, "threadid"),
                Revision = RowReader.GetInt(row, "revision"),
                Title = RowReader.GetString(row, "title"),
                Body = RowReader.GetNullableString(row, "body"),
                Type = RowReader.GetEnum(row, "type", ThreadTypeEnum.Discussion),
                AttachmentSummary = RowReader.GetNullableString(row, "attachmentsummary"),
                EditedByUserId = RowReader.GetNullableString(row, "editedbyuserid"),
                EditedByRole = RowReader.GetEnum(row, "editedbyrole", ThreadEditorRoleEnum.Author),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }

        private static ThreadFollow MapFollow(DataRow row)
        {
            return new ThreadFollow
            {
                Id = RowReader.GetString(row, "id"),
                ThreadId = RowReader.GetString(row, "threadid"),
                UserId = RowReader.GetString(row, "userid"),
                Source = RowReader.GetEnum(row, "source", FollowSourceEnum.Explicit),
                IsOptedOut = RowReader.GetBool(row, "isoptedout"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }

        public Task<List<ExcerptSource>> ExcerptCandidatesAsync(string? afterId, int max, CancellationToken token = default)
        {
            return ExcerptCandidatesCoreAsync("forumthreads", "title", afterId, max, token);
        }

        public Task UpdateExcerptAsync(string id, string excerpt, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            return UpdateExcerptCoreAsync("forumthreads", id, excerpt ?? String.Empty, token);
        }
    }
}
