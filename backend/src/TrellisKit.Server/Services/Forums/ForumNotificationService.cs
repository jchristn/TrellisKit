namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Forum notifications (docs/PHASE3_CONTRACT.md section 9.3): mentions in threads, comment notifications with the
    /// de-duplication priority CommentReply, PostComment, Mention, ThreadReply, follower fan-outs (ThreadReply,
    /// NewThreadInForum, PollClosed) in the Phase 2 fan-out job, and the single-recipient forum events.
    /// </summary>
    public class ForumNotificationService
    {
        #region Private-Members

        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public ForumNotificationService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Path of a thread, optionally to one of its comments.
        /// </summary>
        /// <param name="threadId">Thread.</param>
        /// <param name="commentId">Comment, or null.</param>
        /// <returns>App-relative path.</returns>
        public static string ThreadPath(string threadId, string? commentId)
        {
            return "/threads/" + threadId + (String.IsNullOrEmpty(commentId) ? String.Empty : "?comment=" + commentId);
        }

        /// <summary>
        /// A thread was published: mention notifications and the NewThreadInForum fan-out.
        /// </summary>
        /// <param name="context">Thread context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task ThreadPublishedAsync(ThreadContext context, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ForumThread thread = context.Thread;
            if (thread.Kind == ThreadKindEnum.Original) await MentionsAsync(TargetTypeEnum.Thread, thread.Id, thread.AuthorUserId, thread, null, thread.Excerpt, token).ConfigureAwait(false);
            await _Services.Notifications.StartFanoutAsync(NotificationTypeEnum.NewThreadInForum, thread.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Claim and send mention notifications of a thread or thread comment.
        /// </summary>
        /// <param name="sourceType">Thread or Comment.</param>
        /// <param name="sourceId">Source identifier.</param>
        /// <param name="authorUserId">Author (the actor).</param>
        /// <param name="thread">The thread.</param>
        /// <param name="commentId">Comment, or null for the thread body.</param>
        /// <param name="excerpt">Notification body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task MentionsAsync(TargetTypeEnum sourceType, string sourceId, string authorUserId, ForumThread thread, string? commentId, string? excerpt, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(thread);
            List<NotificationDraft> drafts = await _Services.Mentions.ClaimDraftsAsync(sourceType, sourceId, authorUserId, null, excerpt, token).ConfigureAwait(false);
            if (drafts.Count == 0) return;
            foreach (NotificationDraft draft in drafts) Decorate(draft, thread, commentId);
            HashSet<string> readers = await ReadersAsync(drafts.Select(d => d.RecipientUserId), thread, token).ConfigureAwait(false);
            await _Services.Notifications.CreateForSourceAsync(drafts.Where(d => readers.Contains(d.RecipientUserId)), token).ConfigureAwait(false);
        }

        /// <summary>
        /// Notifications of a new visible comment on a thread conversation: PostComment or CommentReply, mentions, and
        /// ThreadReply to followers (inline up to notifications.fanoutBatchSize followers, otherwise the fan-out job).
        /// Also auto-follows the commenter when enabled.
        /// </summary>
        /// <param name="thread">The thread whose conversation got the comment.</param>
        /// <param name="comment">The new comment.</param>
        /// <param name="parent">Its parent, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task CommentCreatedAsync(ForumThread thread, Comment comment, Comment? parent, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(thread);
            ArgumentNullException.ThrowIfNull(comment);
            using (Activity? activity = TrellisKitTelemetry.StartSpan("forums.comment_notifications"))
            {
                activity?.SetTag("thread.id", thread.Id);
                string userId = comment.AuthorUserId;
                List<NotificationDraft> drafts = new List<NotificationDraft>();
                if (thread.Kind == ThreadKindEnum.Original)
                {
                    NotificationDraft direct = parent == null
                        ? new NotificationDraft { RecipientUserId = thread.AuthorUserId, Type = NotificationTypeEnum.PostComment }
                        : new NotificationDraft { RecipientUserId = parent.AuthorUserId, Type = NotificationTypeEnum.CommentReply, ParentCommentId = parent.Id };
                    direct.ActorUserId = userId;
                    direct.TargetType = TargetTypeEnum.Comment;
                    direct.TargetId = comment.Id;
                    direct.CommentId = comment.Id;
                    direct.Body = comment.Excerpt;
                    Decorate(direct, thread, comment.Id);
                    drafts.Add(direct);
                    List<NotificationDraft> mentions = await _Services.Mentions.ClaimDraftsAsync(TargetTypeEnum.Comment, comment.Id, userId, null, comment.Excerpt, token).ConfigureAwait(false);
                    foreach (NotificationDraft mention in mentions) Decorate(mention, thread, comment.Id);
                    drafts.AddRange(mentions);
                }
                if (await AutoFollowEnabledAsync(userId, token).ConfigureAwait(false)) await _Services.Forums.Threads.AutoFollowAsync(thread.Id, userId, FollowSourceEnum.Comment, token).ConfigureAwait(false);
                long followers = await _Services.Database.Threads.CountFollowersAsync(thread.Id, token).ConfigureAwait(false);
                if (followers <= _Services.Settings.Notifications.FanoutBatchSize)
                {
                    List<string> ids = await _Services.Database.Threads.FollowerIdsAsync(thread.Id, null, _Services.Settings.Notifications.FanoutBatchSize, token).ConfigureAwait(false);
                    drafts.AddRange(ids.Where(id => !String.Equals(id, userId, StringComparison.Ordinal)).Select(id => ReplyDraft(thread, comment, id)));
                }
                else
                {
                    await _Services.Notifications.StartFanoutAsync(NotificationTypeEnum.ThreadReply, comment.Id, token).ConfigureAwait(false);
                }
                HashSet<string> readers = await ReadersAsync(drafts.Select(d => d.RecipientUserId), thread, token).ConfigureAwait(false);
                await _Services.Notifications.CreateForSourceAsync(drafts.Where(d => readers.Contains(d.RecipientUserId)), token).ConfigureAwait(false);
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
        }

        /// <summary>
        /// The accepted answer notification (to the comment author; never to the actor).
        /// </summary>
        /// <param name="actor">Who accepted it.</param>
        /// <param name="thread">The Question thread.</param>
        /// <param name="comment">The accepted comment.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task AnswerAcceptedAsync(ContentActor actor, ForumThread thread, Comment comment, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(thread);
            ArgumentNullException.ThrowIfNull(comment);
            if (String.Equals(comment.AuthorUserId, thread.AuthorUserId, StringComparison.Ordinal) && String.Equals(actor.UserId, thread.AuthorUserId, StringComparison.Ordinal)) return;
            NotificationDraft draft = new NotificationDraft { RecipientUserId = comment.AuthorUserId, Type = NotificationTypeEnum.AnswerAccepted, ActorUserId = actor.UserId, TargetType = TargetTypeEnum.Comment, TargetId = comment.Id, CommentId = comment.Id, TitleText = thread.Title, Body = comment.Excerpt };
            Decorate(draft, thread, comment.Id);
            await _Services.Notifications.CreateAsync(draft, token).ConfigureAwait(false);
        }

        /// <summary>
        /// The feedback reviewed notification (to the thread author).
        /// </summary>
        /// <param name="actor">The reviewer.</param>
        /// <param name="thread">The Feedback thread.</param>
        /// <param name="reviewCommentId">The review comment.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task FeedbackReviewedAsync(ContentActor actor, ForumThread thread, string reviewCommentId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(thread);
            NotificationDraft draft = new NotificationDraft { RecipientUserId = thread.AuthorUserId, Type = NotificationTypeEnum.FeedbackReviewed, ActorUserId = actor.UserId, TargetType = TargetTypeEnum.Thread, TargetId = thread.Id, CommentId = reviewCommentId, TitleText = thread.Title };
            Decorate(draft, thread, reviewCommentId);
            await _Services.Notifications.CreateAsync(draft, token).ConfigureAwait(false);
        }

        /// <summary>
        /// The thread moved notification (to the author; Moderation category, actor hidden).
        /// </summary>
        /// <param name="thread">The thread (before the move).</param>
        /// <param name="destination">Destination forum.</param>
        /// <param name="reason">Reason, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task ThreadMovedAsync(ForumThread thread, Forum destination, string? reason, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(thread);
            ArgumentNullException.ThrowIfNull(destination);
            NotificationDraft draft = new NotificationDraft { RecipientUserId = thread.AuthorUserId, Type = NotificationTypeEnum.ThreadMoved, TargetType = TargetTypeEnum.Thread, TargetId = thread.Id, TitleText = destination.Name, Body = reason };
            Decorate(draft, thread, null);
            draft.ForumId = destination.Id;
            await _Services.Notifications.CreateAsync(draft, token).ConfigureAwait(false);
        }

        /// <summary>
        /// The thread promoted notification (to the thread author).
        /// </summary>
        /// <param name="actor">Who promoted it.</param>
        /// <param name="thread">The thread.</param>
        /// <param name="feedPostId">The feed card.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task ThreadPromotedAsync(ContentActor actor, ForumThread thread, string feedPostId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(thread);
            NotificationDraft draft = new NotificationDraft { RecipientUserId = thread.AuthorUserId, Type = NotificationTypeEnum.ThreadPromoted, ActorUserId = actor.UserId, TargetType = TargetTypeEnum.Thread, TargetId = thread.Id, FeedPostId = null, TitleText = thread.Title };
            Decorate(draft, thread, null);
            draft.Path = "/feed/" + feedPostId;
            await _Services.Notifications.CreateAsync(draft, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Start the PollClosed fan-out of a poll (to its author and voters; idempotent per poll).
        /// </summary>
        /// <param name="pollId">Poll.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public Task StartPollClosedAsync(string pollId, CancellationToken token = default)
        {
            return _Services.Notifications.StartFanoutAsync(NotificationTypeEnum.PollClosed, pollId, token);
        }

        /// <summary>
        /// Process one forum fan-out job (NewThreadInForum, PollClosed, or ThreadReply) in batches of
        /// notifications.fanoutBatchSize, recording progress so another instance can continue it.
        /// </summary>
        /// <param name="job">Claimed job.</param>
        /// <param name="holder">Claim holder.</param>
        /// <param name="claimDuration">Claim extension per batch.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Notifications created, or -1 when the claim was lost.</returns>
        public async Task<int> ProcessFanoutAsync(NotificationFanout job, string holder, TimeSpan claimDuration, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(job);
            int batchSize = Math.Max(1, _Services.Settings.Notifications.FanoutBatchSize);
            ForumThread? thread = null;
            Comment? comment = null;
            Poll? poll = null;
            switch (job.Type)
            {
                case NotificationTypeEnum.NewThreadInForum:
                    thread = await _Services.Database.Threads.ReadAsync(job.SourceId, token).ConfigureAwait(false);
                    break;
                case NotificationTypeEnum.ThreadReply:
                    comment = await _Services.Database.Comments.ReadAsync(job.SourceId, token).ConfigureAwait(false);
                    if (comment != null) thread = await ThreadOfCommentAsync(comment, token).ConfigureAwait(false);
                    break;
                case NotificationTypeEnum.PollClosed:
                    poll = await _Services.Database.Polls.ReadAsync(job.SourceId, token).ConfigureAwait(false);
                    if (poll != null) thread = await _Services.Database.Threads.ReadAsync(poll.ThreadId, token).ConfigureAwait(false);
                    break;
                default:
                    return 0;
            }
            if (thread == null || ForumAccessService.StateOf(thread) != ThreadStateEnum.Published) return 0;
            if (comment != null && (comment.DeletedUtc != null || comment.ModerationState != ModerationStateEnum.None)) return 0;
            List<Forum> forums = await _Services.Database.Forums.ReadManyAsync(new[] { thread.ForumId }, token).ConfigureAwait(false);
            Forum? forum = forums.FirstOrDefault();
            if (forum == null || forum.DeletedUtc != null) return 0;
            int created = 0;
            string? after = job.LastUserId;
            if (job.Type == NotificationTypeEnum.PollClosed && after == null)
            {
                created += await SendAsync(new List<NotificationDraft> { PollDraft(thread, poll!, thread.AuthorUserId) }, thread, token).ConfigureAwait(false);
            }
            while (true)
            {
                token.ThrowIfCancellationRequested();
                List<string> batch = job.Type switch
                {
                    NotificationTypeEnum.NewThreadInForum => await _Services.Database.Forums.FollowerIdsAsync(forum.Id, after, batchSize, token).ConfigureAwait(false),
                    NotificationTypeEnum.ThreadReply => await _Services.Database.Threads.FollowerIdsAsync(thread.Id, after, batchSize, token).ConfigureAwait(false),
                    _ => await _Services.Database.Polls.VoterIdsAsync(poll!.Id, after, batchSize, token).ConfigureAwait(false)
                };
                if (batch.Count == 0) break;
                List<NotificationDraft> drafts = new List<NotificationDraft>();
                foreach (string recipient in batch)
                {
                    if (job.Type == NotificationTypeEnum.NewThreadInForum && !String.Equals(recipient, thread.AuthorUserId, StringComparison.Ordinal)) drafts.Add(NewThreadDraft(thread, forum, recipient));
                    else if (job.Type == NotificationTypeEnum.ThreadReply && !String.Equals(recipient, comment!.AuthorUserId, StringComparison.Ordinal)) drafts.Add(ReplyDraft(thread, comment, recipient));
                    else if (job.Type == NotificationTypeEnum.PollClosed && !String.Equals(recipient, thread.AuthorUserId, StringComparison.Ordinal)) drafts.Add(PollDraft(thread, poll!, recipient));
                }
                if (comment != null) drafts = await WithoutDirectRecipientsAsync(drafts, comment, token).ConfigureAwait(false);
                int added = await SendAsync(drafts, thread, token).ConfigureAwait(false);
                created += added;
                after = batch[batch.Count - 1];
                if (!await _Services.Database.NotificationFanouts.RecordProgressAsync(job.Id, holder, after, added, DateTime.UtcNow + claimDuration, token).ConfigureAwait(false)) return -1;
                if (batch.Count < batchSize) break;
            }
            return created;
        }

        /// <summary>
        /// The thread whose conversation a comment belongs to (the comment's Thread target, or the discussion thread of
        /// its feed post), or null.
        /// </summary>
        /// <param name="comment">Comment.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Thread, or null.</returns>
        public async Task<ForumThread?> ThreadOfCommentAsync(Comment comment, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(comment);
            if (comment.TargetType == TargetTypeEnum.Thread) return await _Services.Database.Threads.ReadAsync(comment.TargetId, token).ConfigureAwait(false);
            if (comment.TargetType == TargetTypeEnum.FeedPost) return await _Services.Database.Threads.ReadBySourceAsync(TargetTypeEnum.FeedPost.ToString(), comment.TargetId, token).ConfigureAwait(false);
            return null;
        }

        #endregion

        #region Private-Methods

        private static void Decorate(NotificationDraft draft, ForumThread thread, string? commentId)
        {
            draft.ThreadId = thread.Id;
            draft.ForumId = thread.ForumId;
            if (draft.TargetType == TargetTypeEnum.FeedPost || (draft.TargetType == TargetTypeEnum.Thread && draft.Type == NotificationTypeEnum.Mention)) draft.SubjectNoun = "thread";
            draft.Path = ThreadPath(thread.Id, commentId);
        }

        private static NotificationDraft ReplyDraft(ForumThread thread, Comment comment, string recipient)
        {
            NotificationDraft draft = new NotificationDraft
            {
                RecipientUserId = recipient,
                Type = NotificationTypeEnum.ThreadReply,
                ActorUserId = comment.AuthorUserId,
                TargetType = TargetTypeEnum.Comment,
                TargetId = comment.Id,
                CommentId = comment.Id,
                TitleText = thread.Title,
                Body = comment.Excerpt,
                GroupKey = "ThreadReply:Thread:" + thread.Id
            };
            Decorate(draft, thread, comment.Id);
            return draft;
        }

        private static NotificationDraft NewThreadDraft(ForumThread thread, Forum forum, string recipient)
        {
            NotificationDraft draft = new NotificationDraft
            {
                RecipientUserId = recipient,
                Type = NotificationTypeEnum.NewThreadInForum,
                ActorUserId = thread.AuthorUserId,
                TargetType = TargetTypeEnum.Thread,
                TargetId = thread.Id,
                TitleText = forum.Name,
                Body = thread.Title,
                GroupKey = "NewThreadInForum:Forum:" + forum.Id
            };
            Decorate(draft, thread, null);
            draft.Path = "/forums/" + forum.Id;
            return draft;
        }

        private static NotificationDraft PollDraft(ForumThread thread, Poll poll, string recipient)
        {
            NotificationDraft draft = new NotificationDraft
            {
                RecipientUserId = recipient,
                Type = NotificationTypeEnum.PollClosed,
                TargetType = TargetTypeEnum.Poll,
                TargetId = poll.Id,
                TitleText = thread.Title,
                PollId = poll.Id
            };
            Decorate(draft, thread, null);
            return draft;
        }

        private async Task<int> SendAsync(List<NotificationDraft> drafts, ForumThread thread, CancellationToken token)
        {
            if (drafts.Count == 0) return 0;
            HashSet<string> readers = await ReadersAsync(drafts.Select(d => d.RecipientUserId), thread, token).ConfigureAwait(false);
            return await _Services.Notifications.CreateManyAsync(drafts.Where(d => readers.Contains(d.RecipientUserId)), true, token).ConfigureAwait(false);
        }

        private async Task<List<NotificationDraft>> WithoutDirectRecipientsAsync(List<NotificationDraft> drafts, Comment comment, CancellationToken token)
        {
            if (drafts.Count == 0) return drafts;
            HashSet<string> direct = new HashSet<string>(StringComparer.Ordinal);
            IEnumerable<string> recipients = drafts.Select(d => d.RecipientUserId).ToList();
            foreach (NotificationTypeEnum type in new[] { NotificationTypeEnum.PostComment, NotificationTypeEnum.CommentReply, NotificationTypeEnum.Mention })
                direct.UnionWith(await _Services.Database.Notifications.ExistingRecipientsAsync(type, comment.Id, recipients, token).ConfigureAwait(false));
            return drafts.Where(d => !direct.Contains(d.RecipientUserId)).ToList();
        }

        private async Task<HashSet<string>> ReadersAsync(IEnumerable<string> userIds, ForumThread thread, CancellationToken token)
        {
            List<string> ids = userIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            if (ids.Count == 0) return result;
            List<Forum> forums = await _Services.Database.Forums.ReadManyAsync(new[] { thread.ForumId }, token).ConfigureAwait(false);
            ContentAudienceEnum forumAudience = forums.FirstOrDefault()?.Audience ?? ContentAudienceEnum.Members;
            List<User> users = await _Services.Database.Users.ReadManyAsync(ids, token).ConfigureAwait(false);
            List<Entitlement> entitlements = await _Services.Database.Entitlements.EnumerateByUsersAsync(ids, token).ConfigureAwait(false);
            List<RoleAssignment> roles = await _Services.Database.RoleAssignments.EnumerateByUsersAsync(ids, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            HashSet<string> members = new HashSet<string>(entitlements.Where(e => e.IsActiveAt(now)).Select(e => e.UserId), StringComparer.Ordinal);
            foreach (User user in users.Where(u => !u.IsDeleted))
            {
                bool admin = roles.Any(r => r.UserId == user.Id && r.Role == RoleEnum.SystemAdmin);
                bool moderator = roles.Any(r => r.UserId == user.Id && r.Role == RoleEnum.Moderator &&
                    (r.ResourceType == ResourceTypeEnum.Global || (r.ResourceType == ResourceTypeEnum.Forum && (r.ResourceId == "*" || r.ResourceId == thread.ForumId))));
                bool attested = user.AgeAttestedUtc != null;
                bool member = members.Contains(user.Id);
                if (admin || moderator || String.Equals(user.Id, thread.AuthorUserId, StringComparison.Ordinal)
                    || (ContentAudienceRule.RecipientMayOpen(thread.Audience, false, attested, member) && ContentAudienceRule.RecipientMayOpen(forumAudience, false, attested, member)))
                    result.Add(user.Id);
            }
            return result;
        }

        private async Task<bool> AutoFollowEnabledAsync(string userId, CancellationToken token)
        {
            bool? preference = await _Services.Database.Forums.ReadAutoFollowOnCommentAsync(userId, token).ConfigureAwait(false);
            return preference ?? _Services.Settings.Forums.AutoFollowOnComment;
        }

        #endregion
    }
}
