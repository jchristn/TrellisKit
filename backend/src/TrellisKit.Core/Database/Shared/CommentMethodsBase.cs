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
    /// Shared comment and comment revision methods. Query alias c (comments), k (counted comments), cr (revisions).
    /// </summary>
    internal abstract class CommentMethodsBase : MethodsBase, ICommentMethods
    {
        protected CommentMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<Comment> CreateAsync(Comment comment, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(comment);
            RequireText(comment.Id, nameof(comment.Id));
            RequireText(comment.TargetId, nameof(comment.TargetId));
            RequireText(comment.AuthorUserId, nameof(comment.AuthorUserId));
            RequireText(comment.Path, nameof(comment.Path));
            List<string> statements = new List<string>();
            AddLock(statements, comment.TargetType, comment.TargetId);
            statements.Add("INSERT INTO comments (propertyid, id, targettype, targetid, parentcommentid, rootcommentid, replytocommentid, path, depth, authoruserid, body, excerpt, linkpreviewid, " +
                "showlinkpreview, attachmentcount, childcount, descendantcount, reactiontotal, score, editedutc, revisioncount, moderationstate, createdutc, updatedutc) VALUES (" +
                Pid + ", " + D.Str(comment.Id) + ", " + D.Enum(comment.TargetType) + ", " + D.Str(comment.TargetId) + ", " + D.Str(comment.ParentCommentId) + ", " + D.Str(comment.RootCommentId) + ", " +
                D.Str(comment.ReplyToCommentId) + ", " + D.Str(comment.Path) + ", " + D.Num(comment.Depth) + ", " + D.Str(comment.AuthorUserId) + ", " + D.Str(comment.Body) + ", " +
                D.Str(comment.Excerpt) + ", " + D.Str(comment.LinkPreviewId) + ", " + D.Bool(comment.ShowLinkPreview) + ", " + D.Num(comment.AttachmentCount) + ", " +
                D.Num(0) + ", " + D.Num(0) + ", " + D.Num(0) + ", " + D.Num(0) + ", NULL, " + D.Num(0) + ", " + D.Enum(ModerationStateEnum.None) + ", " +
                D.Ts(comment.CreatedUtc) + ", " + D.Ts(comment.UpdatedUtc) + ");");
            AddRecompute(statements, comment);
            await TransactionAsync(statements, token).ConfigureAwait(false);
            return (await ReadAsync(comment.Id, token).ConfigureAwait(false))!;
        }

        public async Task<Comment?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            DataTable table = await QueryAsync("SELECT c.* FROM comments c WHERE " + Scope("c") + " AND c.id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<List<Comment>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            List<string> list = ids.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (list.Count == 0) return new List<Comment>();
            DataTable table = await QueryAsync("SELECT c.* FROM comments c WHERE " + Scope("c") + " AND c.id IN " + D.InList(list) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<Comment> UpdateAsync(Comment comment, CommentRevision? revision, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(comment);
            RequireText(comment.Id, nameof(comment.Id));
            string id = D.Str(comment.Id);
            List<string> statements = new List<string>();
            if (revision != null)
            {
                statements.Add("INSERT INTO commentrevisions (propertyid, id, commentid, revision, body, attachmentsummary, createdutc) SELECT " + Pid + ", " + D.Str(revision.Id) + ", " + id +
                    ", COALESCE(MAX(revision), 0) + 1, " + D.Str(revision.Body) + ", " + D.Str(revision.AttachmentSummary) + ", " + D.Ts(revision.CreatedUtc) +
                    " FROM commentrevisions WHERE " + Scope() + " AND commentid = " + id + ";");
            }
            statements.Add("UPDATE comments SET body = " + D.Str(comment.Body) + ", excerpt = " + D.Str(comment.Excerpt) + ", linkpreviewid = " + D.Str(comment.LinkPreviewId) +
                ", showlinkpreview = " + D.Bool(comment.ShowLinkPreview) + ", editedutc = " + D.Ts(comment.EditedUtc) +
                ", revisioncount = (SELECT COUNT(*) FROM commentrevisions r WHERE " + Scope("r") + " AND r.commentid = " + id + "), updatedutc = " + D.Ts(comment.UpdatedUtc) +
                " WHERE " + Scope() + " AND id = " + id + " AND deletedutc IS NULL;");
            await TransactionAsync(statements, token).ConfigureAwait(false);
            await RecomputeAttachmentsAsync(comment.Id, token).ConfigureAwait(false);
            return (await ReadAsync(comment.Id, token).ConfigureAwait(false)) ?? comment;
        }

        public async Task<bool> RecomputeAttachmentsAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE comments SET attachmentcount = (SELECT COUNT(*) FROM mediaattachments m WHERE " + Scope("m") + " AND m.attachedtargettype = " + D.Enum(TargetTypeEnum.Comment) +
                " AND m.attachedtargetid = comments.id AND m.deletedutc IS NULL) WHERE " + Scope() + " AND id = " + D.Str(id) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<Comment?> DeleteAsync(string id, CommentDeletedReasonEnum reason, bool erase, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            Comment? existing = await ReadAsync(id, token).ConfigureAwait(false);
            if (existing == null) return null;
            if (existing.DeletedUtc != null && !erase) return existing;
            string cid = D.Str(id);
            List<string> statements = new List<string>();
            AddLock(statements, existing.TargetType, existing.TargetId);
            statements.Add("UPDATE comments SET deletedutc = COALESCE(deletedutc, " + D.Ts(nowUtc) + "), deletedreason = " + D.Enum(reason) +
                (erase ? ", body = NULL, excerpt = NULL, linkpreviewid = NULL" : String.Empty) + ", revisioncount = " + D.Num(0) + ", updatedutc = " + D.Ts(nowUtc) +
                " WHERE " + Scope() + " AND id = " + cid + ";");
            statements.AddRange(Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.CommentRevision, "SELECT id FROM commentrevisions WHERE " + Scope() + " AND commentid = " + cid));
            statements.Add("DELETE FROM commentrevisions WHERE " + Scope() + " AND commentid = " + cid + ";");
            statements.Add("DELETE FROM mentions WHERE " + Scope() + " AND sourcetype = " + D.Enum(TargetTypeEnum.Comment) + " AND sourceid = " + cid + ";");
            statements.AddRange(Driver.Metadata.DeleteStatements(TargetTypeEnum.Comment, id));
            AddRecompute(statements, existing);
            await TransactionAsync(statements, token).ConfigureAwait(false);
            return await ReadAsync(id, token).ConfigureAwait(false);
        }

        public async Task<Comment?> SetModerationStateAsync(string id, ModerationStateEnum state, string? moderatorUserId, string? reason, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            Comment? existing = await ReadAsync(id, token).ConfigureAwait(false);
            if (existing == null) return null;
            List<string> statements = new List<string>();
            AddLock(statements, existing.TargetType, existing.TargetId);
            statements.Add("UPDATE comments SET moderationstate = " + D.Enum(state) + ", moderatedutc = " + D.Ts(nowUtc) + ", moderatedbyuserid = " + D.Str(moderatorUserId) +
                ", moderationreason = " + D.Str(reason) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + ";");
            AddRecompute(statements, existing);
            await TransactionAsync(statements, token).ConfigureAwait(false);
            return await ReadAsync(id, token).ConfigureAwait(false);
        }

        public async Task<EnumerationResult<Comment>> EnumerateLevelAsync(TargetTypeEnum targetType, string targetId, string? parentCommentId, EnumerationRequest request, CommentViewerFilter viewer, CancellationToken token = default)
        {
            RequireText(targetId, nameof(targetId));
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(viewer);
            string ordering = request.Ordering.Name;
            List<string> all = new List<string> { "c.targettype = " + D.Enum(targetType), "c.targetid = " + D.Str(targetId), Eligible(viewer) };
            all.Add(parentCommentId == null ? "c.parentcommentid IS NULL" : "c.parentcommentid = " + D.Str(parentCommentId));
            all.AddRange(EnumerationConditions(request));
            List<string> paged = new List<string>(all);
            if (request.HasPosition) paged.Add(After(ordering, request.AfterKey, request.AfterId!));
            DataTable table = await QueryAsync("SELECT c.* FROM comments c" + Where(paged) + " ORDER BY " + OrderBy(ordering, "c") +
                " LIMIT " + D.Num(request.MaxResults + 1) + (!request.HasPosition && request.Skip > 0 ? " OFFSET " + D.Num(request.Skip) : String.Empty) + ";", token).ConfigureAwait(false);
            List<Comment> rows = table.Rows.Cast<DataRow>().Select(Map).ToList();
            bool more = rows.Count > request.MaxResults;
            if (more) rows = rows.Take(request.MaxResults).ToList();
            EnumerationResult<Comment> result = new EnumerationResult<Comment>
            {
                MaxResults = request.MaxResults,
                Skip = request.HasPosition ? 0 : request.Skip,
                EndOfResults = !more,
                TimestampUtc = DateTime.UtcNow,
                Objects = rows
            };
            if (more && rows.Count > 0)
            {
                Comment last = rows[rows.Count - 1];
                result.ContinuationToken = ContinuationTokens.Encode(request.Fingerprint, ordering, CommentOrderingKeys.KeyOf(last, ordering), last.Id);
            }
            if (request.CountTotals)
            {
                result.TotalRecords = await CountAsync("SELECT COUNT(*) AS cnt FROM comments c" + Where(all) + ";", token).ConfigureAwait(false);
                if (!more) result.RecordsRemaining = 0;
                else
                {
                    Comment last = rows[rows.Count - 1];
                    List<string> after = new List<string>(all) { After(ordering, CommentOrderingKeys.KeyOf(last, ordering), last.Id) };
                    result.RecordsRemaining = await CountAsync("SELECT COUNT(*) AS cnt FROM comments c" + Where(after) + ";", token).ConfigureAwait(false);
                }
            }
            return result;
        }

        public async Task<CommentLevel> ListChildrenAsync(IReadOnlyCollection<string> parentIds, string ordering, int limitPerParent, CommentViewerFilter viewer, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(parentIds);
            ArgumentNullException.ThrowIfNull(viewer);
            CommentLevel level = new CommentLevel();
            List<string> ids = parentIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return level;
            DataTable table = await QueryAsync("SELECT x.* FROM (SELECT c.*, ROW_NUMBER() OVER (PARTITION BY c.parentcommentid ORDER BY " + OrderBy(ordering, "c") + ") AS rn, " +
                "COUNT(*) OVER (PARTITION BY c.parentcommentid) AS siblingtotal FROM comments c WHERE " + Scope("c") + " AND c.parentcommentid IN " + D.InList(ids) + " AND " + Eligible(viewer) +
                ") x WHERE x.rn <= " + D.Num(Math.Max(1, limitPerParent) + 1) + " ORDER BY x.parentcommentid, x.rn;", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows)
            {
                Comment comment = Map(row);
                level.Comments.Add(comment);
                level.Totals[comment.ParentCommentId!] = (int)RowReader.GetLong(row, "siblingtotal");
            }
            return level;
        }

        public Task<EnumerationResult<CommentRevision>> EnumerateRevisionsAsync(string commentId, EnumerationRequest request, CancellationToken token = default)
        {
            RequireText(commentId, nameof(commentId));
            ArgumentNullException.ThrowIfNull(request);
            return EnumerateAsync(request, "cr.*", "commentrevisions cr", new List<string> { "cr.commentid = " + D.Str(commentId) }, MapRevision, token);
        }

        public async Task<List<CommentRevision>> ListRevisionsAsync(IEnumerable<string> commentIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(commentIds);
            List<string> ids = commentIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return new List<CommentRevision>();
            DataTable table = await QueryAsync("SELECT cr.* FROM commentrevisions cr WHERE " + Scope("cr") + " AND cr.commentid IN " + D.InList(ids) + " ORDER BY cr.commentid, cr.revision;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapRevision).ToList();
        }

        public async Task<List<Comment>> ListByAuthorAsync(string authorUserId, string? afterId, int limit, bool excludeErased, CancellationToken token = default)
        {
            RequireText(authorUserId, nameof(authorUserId));
            List<string> conditions = new List<string> { Scope("c"), "c.authoruserid = " + D.Str(authorUserId) };
            if (excludeErased) conditions.Add("(c.deletedreason IS NULL OR c.deletedreason <> " + D.Enum(CommentDeletedReasonEnum.AccountDeleted) + ")");
            if (!String.IsNullOrEmpty(afterId)) conditions.Add("c.id > " + D.Str(afterId));
            DataTable table = await QueryAsync("SELECT c.* FROM comments c" + Where(conditions) + " ORDER BY c.id ASC LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<bool> PurgeAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            string cid = D.Str(id);
            List<string> statements = new List<string>();
            statements.AddRange(Driver.Metadata.DeleteStatements(TargetTypeEnum.Comment, id));
            statements.AddRange(Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.CommentRevision, "SELECT id FROM commentrevisions WHERE " + Scope() + " AND commentid = " + cid));
            statements.Add("DELETE FROM commentrevisions WHERE " + Scope() + " AND commentid = " + cid + ";");
            statements.Add("DELETE FROM mentions WHERE " + Scope() + " AND sourcetype = " + D.Enum(TargetTypeEnum.Comment) + " AND sourceid = " + cid + ";");
            statements.Add("UPDATE comments SET body = NULL, excerpt = NULL, linkpreviewid = NULL, revisioncount = " + D.Num(0) + ", purgedutc = " + D.Ts(nowUtc) +
                ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + cid + " AND purgedutc IS NULL RETURNING id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        private void AddLock(List<string> statements, TargetTypeEnum targetType, string targetId)
        {
            if (targetType == TargetTypeEnum.FeedPost)
                statements.Add("UPDATE feedposts SET commentcount = commentcount WHERE " + Scope() + " AND id = " + D.Str(targetId) + ";");
            else if (targetType == TargetTypeEnum.Thread)
                statements.Add("UPDATE forumthreads SET commentcount = commentcount WHERE " + Scope() + " AND id = " + D.Str(targetId) + ";");
            else if (targetType == TargetTypeEnum.Idea)
                statements.Add("UPDATE ideas SET commentcount = commentcount WHERE " + Scope() + " AND id = " + D.Str(targetId) + ";");
        }

        private string VisibleK()
        {
            return "k.deletedutc IS NULL AND k.moderationstate = " + D.Enum(ModerationStateEnum.None);
        }

        private void AddRecompute(List<string> statements, Comment comment)
        {
            List<string> segments = comment.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
            for (int i = 0; i < segments.Count - 1; i++)
            {
                string ancestorPath = String.Join("/", segments.Take(i + 1)) + "/";
                string upper = ancestorPath.Substring(0, ancestorPath.Length - 1) + "0";
                string id = D.Str(segments[i]);
                string children = "(SELECT COUNT(*) FROM comments k WHERE " + Scope("k") + " AND k.parentcommentid = " + id + " AND " + VisibleK() + ")";
                statements.Add("UPDATE comments SET childcount = " + children +
                    ", descendantcount = (SELECT COUNT(*) FROM comments k WHERE " + Scope("k") + " AND k.path > " + D.Str(ancestorPath) + " AND k.path < " + D.Str(upper) + " AND " + VisibleK() + ")" +
                    ", score = reactiontotal + 2 * " + children + " WHERE " + Scope() + " AND id = " + id + ";");
            }
            if (comment.TargetType == TargetTypeEnum.FeedPost)
            {
                statements.Add("UPDATE feedposts SET commentcount = (SELECT COUNT(*) FROM comments k WHERE " + Scope("k") + " AND k.targettype = " + D.Enum(TargetTypeEnum.FeedPost) +
                    " AND k.targetid = feedposts.id AND " + VisibleK() + ") WHERE " + Scope() + " AND id = " + D.Str(comment.TargetId) + ";");
                // A feed post's discussion thread shares its conversation (Phase 3 section 8.2): mirror its counters.
                statements.Add(ForumCounterSql.RecomputeLinkedThreadComments(Pid, D.Str(comment.TargetId)));
                statements.Add(ForumCounterSql.RecomputeForum(Pid, ForumCounterSql.ForumOfLinkedThread(Pid, D.Str(comment.TargetId))));
            }
            else if (comment.TargetType == TargetTypeEnum.Thread)
            {
                statements.Add(ForumCounterSql.RecomputeThreadComments(Pid, D.Str(comment.TargetId)));
                statements.Add(ForumCounterSql.RecomputeForum(Pid, ForumCounterSql.ForumOfThread(Pid, D.Str(comment.TargetId))));
            }
            else if (comment.TargetType == TargetTypeEnum.Idea)
            {
                statements.Add("UPDATE ideas SET commentcount = (SELECT COUNT(*) FROM comments k WHERE " + Scope("k") + " AND k.targettype = " + D.Enum(TargetTypeEnum.Idea) +
                    " AND k.targetid = ideas.id AND " + VisibleK() + ") WHERE " + Scope() + " AND id = " + D.Str(comment.TargetId) + ";");
            }
        }

        private string Eligible(CommentViewerFilter viewer)
        {
            string unmoderated = "c.moderationstate = " + D.Enum(ModerationStateEnum.None);
            string visible = "(" + unmoderated + (viewer.HiddenAuthorIds.Count > 0 ? " AND c.authoruserid NOT IN " + D.InList(viewer.HiddenAuthorIds) : String.Empty) + ")";
            if (viewer.IsModerator) visible += " OR c.moderationstate <> " + D.Enum(ModerationStateEnum.None);
            else if (!String.IsNullOrEmpty(viewer.ViewerUserId)) visible += " OR (c.moderationstate <> " + D.Enum(ModerationStateEnum.None) + " AND c.authoruserid = " + D.Str(viewer.ViewerUserId) + ")";
            return "(c.descendantcount > 0 OR (c.deletedutc IS NULL AND (" + visible + ")))";
        }

        private static string OrderBy(string ordering, string alias)
        {
            if (String.Equals(ordering, CommentOrderingKeys.CreatedDescending, StringComparison.OrdinalIgnoreCase)) return alias + ".createdutc DESC, " + alias + ".id DESC";
            if (String.Equals(ordering, CommentOrderingKeys.CreatedAscending, StringComparison.OrdinalIgnoreCase)) return alias + ".createdutc ASC, " + alias + ".id ASC";
            return alias + ".score DESC, " + alias + ".createdutc ASC, " + alias + ".id ASC";
        }

        private string After(string ordering, string? key, string id)
        {
            CommentOrderingKeys.Parse(key, ordering, out long score, out DateTime created);
            string t = D.Ts(created);
            string i = D.Str(id);
            if (String.Equals(ordering, CommentOrderingKeys.CreatedDescending, StringComparison.OrdinalIgnoreCase))
                return "(c.createdutc < " + t + " OR (c.createdutc = " + t + " AND c.id < " + i + "))";
            string later = "(c.createdutc > " + t + " OR (c.createdutc = " + t + " AND c.id > " + i + "))";
            if (String.Equals(ordering, CommentOrderingKeys.CreatedAscending, StringComparison.OrdinalIgnoreCase)) return later;
            return "(c.score < " + D.Num(score) + " OR (c.score = " + D.Num(score) + " AND " + later + "))";
        }

        private static Comment Map(DataRow row)
        {
            return new Comment
            {
                Id = RowReader.GetString(row, "id"),
                TargetType = RowReader.GetEnum(row, "targettype", TargetTypeEnum.FeedPost),
                TargetId = RowReader.GetString(row, "targetid"),
                ParentCommentId = RowReader.GetNullableString(row, "parentcommentid"),
                RootCommentId = RowReader.GetString(row, "rootcommentid"),
                ReplyToCommentId = RowReader.GetNullableString(row, "replytocommentid"),
                Path = RowReader.GetString(row, "path"),
                Depth = RowReader.GetInt(row, "depth"),
                AuthorUserId = RowReader.GetString(row, "authoruserid"),
                Body = RowReader.GetNullableString(row, "body"),
                Excerpt = RowReader.GetNullableString(row, "excerpt"),
                LinkPreviewId = RowReader.GetNullableString(row, "linkpreviewid"),
                ShowLinkPreview = RowReader.GetBool(row, "showlinkpreview"),
                AttachmentCount = RowReader.GetInt(row, "attachmentcount"),
                ChildCount = RowReader.GetInt(row, "childcount"),
                DescendantCount = RowReader.GetInt(row, "descendantcount"),
                ReactionTotal = RowReader.GetInt(row, "reactiontotal"),
                Score = RowReader.GetInt(row, "score"),
                EditedUtc = RowReader.GetNullableDateTime(row, "editedutc"),
                RevisionCount = RowReader.GetInt(row, "revisioncount"),
                ModerationState = RowReader.GetEnum(row, "moderationstate", ModerationStateEnum.None),
                ModeratedUtc = RowReader.GetNullableDateTime(row, "moderatedutc"),
                ModeratedByUserId = RowReader.GetNullableString(row, "moderatedbyuserid"),
                ModerationReason = RowReader.GetNullableString(row, "moderationreason"),
                DeletedUtc = RowReader.GetNullableDateTime(row, "deletedutc"),
                DeletedReason = RowReader.GetNullableEnum<CommentDeletedReasonEnum>(row, "deletedreason"),
                PurgedUtc = RowReader.GetNullableDateTime(row, "purgedutc"),
                IsAcceptedAnswer = row.Table.Columns.Contains("isacceptedanswer") && RowReader.GetBool(row, "isacceptedanswer"),
                IsFeedbackReview = row.Table.Columns.Contains("isfeedbackreview") && RowReader.GetBool(row, "isfeedbackreview"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        private static CommentRevision MapRevision(DataRow row)
        {
            return new CommentRevision
            {
                Id = RowReader.GetString(row, "id"),
                CommentId = RowReader.GetString(row, "commentid"),
                Revision = RowReader.GetInt(row, "revision"),
                Body = RowReader.GetNullableString(row, "body"),
                AttachmentSummary = RowReader.GetNullableString(row, "attachmentsummary"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }

        public Task<List<ExcerptSource>> ExcerptCandidatesAsync(string? afterId, int max, CancellationToken token = default)
        {
            return ExcerptCandidatesCoreAsync("comments", null, afterId, max, token);
        }

        public Task UpdateExcerptAsync(string id, string excerpt, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            return UpdateExcerptCoreAsync("comments", id, excerpt ?? String.Empty, token);
        }
    }
}
