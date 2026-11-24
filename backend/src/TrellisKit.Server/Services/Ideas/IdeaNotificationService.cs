namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services.Email;
    using TrellisKit.Server.Services.Email.Templates;

    /// <summary>
    /// Ideas board notifications (docs/PHASE5_CONTRACT.md sections 7.2, 9, and 10): IdeaStatusChanged to voters and
    /// followers through the notification fan-out worker (keyset batches; one unread row per idea and member, refreshed
    /// on later changes), the optional idea_shipped email to voters who turned Ideas email on, and the comment
    /// notifications of idea conversations (PostComment to the idea's author, CommentReply, Mention). Never to the actor,
    /// never across blocks, and only to members who may read the idea.
    /// </summary>
    public class IdeaNotificationService
    {
        #region Private-Members

        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public IdeaNotificationService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The client path of an idea, optionally at a comment.
        /// </summary>
        /// <param name="ideaId">Idea identifier.</param>
        /// <param name="commentId">Comment identifier, or null.</param>
        /// <returns>Path.</returns>
        public static string IdeaPath(string ideaId, string? commentId)
        {
            return "/ideas/" + ideaId + (String.IsNullOrEmpty(commentId) ? String.Empty : "?comment=" + commentId);
        }

        /// <summary>
        /// The notification title of a status change.
        /// </summary>
        /// <param name="idea">Idea.</param>
        /// <returns>Title.</returns>
        public static string StatusTitle(Idea idea)
        {
            ArgumentNullException.ThrowIfNull(idea);
            return "Status update on " + idea.Title;
        }

        /// <summary>
        /// Queue the IdeaStatusChanged fan-out of a status change (source id: idea id and change time).
        /// </summary>
        /// <param name="idea">Idea after the change.</param>
        /// <param name="changedUtc">Change time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when queued.</returns>
        public Task<bool> StatusChangedAsync(Idea idea, DateTime changedUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(idea);
            return _Services.Notifications.StartFanoutAsync(NotificationTypeEnum.IdeaStatusChanged, idea.Id + ":" + changedUtc.Ticks.ToString(CultureInfo.InvariantCulture), token);
        }

        /// <summary>
        /// Notification fan-out hook (NotificationService.ExtraFanout): processes IdeaStatusChanged jobs in keyset batches
        /// of notifications.fanoutBatchSize over the idea's voters and followers.
        /// </summary>
        /// <param name="job">Fan-out job.</param>
        /// <param name="holder">Claim holder.</param>
        /// <param name="claimDuration">Claim extension per batch.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Notifications created or refreshed; -1 when the claim was lost.</returns>
        /// <exception cref="ArgumentNullException">The job is null.</exception>
        public async Task<int> ProcessFanoutAsync(NotificationFanout job, string holder, TimeSpan claimDuration, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(job);
            int colon = job.SourceId.IndexOf(':');
            string ideaId = colon > 0 ? job.SourceId.Substring(0, colon) : job.SourceId;
            Idea? idea = await _Services.Database.Ideas.ReadAsync(ideaId, token).ConfigureAwait(false);
            if (idea == null || idea.DeletedUtc != null || idea.ModerationState != ModerationStateEnum.None) return 0;
            List<IdeaStatus> statuses = await _Services.Ideas.Statuses.AllAsync(token).ConfigureAwait(false);
            IdeaStatus? status = IdeaStatusService.Find(statuses, idea.StatusKey);
            string label = status?.Label ?? idea.StatusKey;
            string body = "Now " + label + (String.IsNullOrWhiteSpace(idea.StatusNote) ? "." : ": " + idea.StatusNote!.Trim());
            if (body.Length > 300) body = body.Substring(0, 297) + "...";
            string title = StatusTitle(idea);
            bool shipped = IdeaStatusService.IsShipped(idea.StatusKey);
            int batchSize = Math.Max(1, _Services.Settings.Notifications.FanoutBatchSize);
            string? after = job.LastUserId;
            int created = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                List<string> batch = await _Services.Database.Ideas.RecipientIdsAsync(idea.Id, after, batchSize, token).ConfigureAwait(false);
                if (batch.Count == 0) break;
                List<string> allowed = await ReadersAsync(idea, batch, token).ConfigureAwait(false);
                DateTime now = DateTime.UtcNow;
                HashSet<string> refreshed = await _Services.Database.Ideas.RefreshStatusNotificationsAsync(idea.Id, allowed, title, body, now, token).ConfigureAwait(false);
                List<NotificationDraft> drafts = allowed.Where(r => !refreshed.Contains(r)).Select(r => new NotificationDraft
                {
                    RecipientUserId = r,
                    Type = NotificationTypeEnum.IdeaStatusChanged,
                    TargetType = TargetTypeEnum.Idea,
                    TargetId = idea.Id,
                    TitleText = idea.Title,
                    Body = body,
                    Path = IdeaPath(idea.Id, null),
                    EventUtc = now,
                    QueuePush = true
                }).ToList();
                int added = drafts.Count == 0 ? 0 : await _Services.Notifications.CreateManyAsync(drafts, false, token).ConfigureAwait(false);
                if (shipped) await EmailShippedAsync(idea, allowed, job.SourceId, token).ConfigureAwait(false);
                created += added + refreshed.Count;
                after = batch[batch.Count - 1];
                if (!await _Services.Database.NotificationFanouts.RecordProgressAsync(job.Id, holder, after, added + refreshed.Count, DateTime.UtcNow + claimDuration, token).ConfigureAwait(false)) return -1;
                if (batch.Count < batchSize) break;
            }
            return created;
        }

        /// <summary>
        /// Comment hook of idea conversations: PostComment to the idea's author for a top-level comment, CommentReply to
        /// the replied-to author, and Mention to mentioned members (one notification per recipient, readers only).
        /// </summary>
        /// <param name="host">Idea host.</param>
        /// <param name="comment">New comment.</param>
        /// <param name="parent">Replied-to comment, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task CommentCreatedAsync(CommentHost host, Comment comment, Comment? parent, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(host);
            ArgumentNullException.ThrowIfNull(comment);
            Idea? idea = host.Idea ?? await _Services.Database.Ideas.ReadAsync(host.TargetId, token).ConfigureAwait(false);
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Comment, comment.Id, token).ConfigureAwait(false);
            if (idea == null) return;
            string path = IdeaPath(idea.Id, comment.Id);
            List<NotificationDraft> drafts = new List<NotificationDraft>();
            if (parent == null)
            {
                if (!String.IsNullOrEmpty(idea.AuthorUserId))
                {
                    drafts.Add(new NotificationDraft
                    {
                        RecipientUserId = idea.AuthorUserId,
                        Type = NotificationTypeEnum.PostComment,
                        ActorUserId = comment.AuthorUserId,
                        TargetType = TargetTypeEnum.Comment,
                        TargetId = comment.Id,
                        CommentId = comment.Id,
                        SubjectNoun = "idea",
                        Path = path,
                        Body = comment.Excerpt
                    });
                }
            }
            else
            {
                drafts.Add(new NotificationDraft
                {
                    RecipientUserId = parent.AuthorUserId,
                    Type = NotificationTypeEnum.CommentReply,
                    ActorUserId = comment.AuthorUserId,
                    TargetType = TargetTypeEnum.Comment,
                    TargetId = comment.Id,
                    CommentId = comment.Id,
                    ParentCommentId = parent.Id,
                    Path = path,
                    Body = comment.Excerpt
                });
            }
            List<NotificationDraft> mentions = await _Services.Mentions.ClaimDraftsAsync(TargetTypeEnum.Comment, comment.Id, comment.AuthorUserId, null, comment.Excerpt, token).ConfigureAwait(false);
            foreach (NotificationDraft mention in mentions) mention.Path = path;
            drafts.AddRange(mentions);
            if (drafts.Count == 0) return;
            HashSet<string> readers = new HashSet<string>(await ReadersAsync(idea, drafts.Select(d => d.RecipientUserId).Distinct(StringComparer.Ordinal).ToList(), token).ConfigureAwait(false), StringComparer.Ordinal);
            await _Services.Notifications.CreateForSourceAsync(drafts.Where(d => readers.Contains(d.RecipientUserId)), token).ConfigureAwait(false);
        }

        /// <summary>
        /// The members among the given ones who may read an idea (not deleted; staff, the author, or the audience met).
        /// </summary>
        /// <param name="idea">Idea.</param>
        /// <param name="userIds">Candidates.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Readers in the given order.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<List<string>> ReadersAsync(Idea idea, List<string> userIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(idea);
            ArgumentNullException.ThrowIfNull(userIds);
            if (userIds.Count == 0) return new List<string>();
            Dictionary<string, User> users = (await _Services.Database.Users.ReadManyAsync(userIds, token).ConfigureAwait(false)).ToDictionary(u => u.Id, StringComparer.Ordinal);
            DateTime now = DateTime.UtcNow;
            HashSet<string> members = new HashSet<string>((await _Services.Database.Entitlements.EnumerateByUsersAsync(userIds, token).ConfigureAwait(false)).Where(e => e.IsActiveAt(now)).Select(e => e.UserId), StringComparer.Ordinal);
            List<RoleAssignment> roles = await _Services.Database.RoleAssignments.EnumerateByUsersAsync(userIds, token).ConfigureAwait(false);
            HashSet<string> staff = new HashSet<string>(roles.Where(r => r.Role == RoleEnum.SystemAdmin || r.Role == RoleEnum.SupportAgent).Select(r => r.UserId), StringComparer.Ordinal);
            List<string> result = new List<string>();
            foreach (string id in userIds)
            {
                if (!users.TryGetValue(id, out User? user) || user.IsDeleted) continue;
                if (staff.Contains(id) || String.Equals(idea.AuthorUserId, id, StringComparison.Ordinal))
                {
                    result.Add(id);
                    continue;
                }
                if (ContentAudienceRule.RecipientMayOpen(_Services.Access.Current, idea.Audience, false, user.AgeAttestedUtc != null, members.Contains(id))) result.Add(id);
            }
            return result;
        }

        #endregion

        #region Private-Methods

        private async Task EmailShippedAsync(Idea idea, List<string> recipients, string sourceId, CancellationToken token)
        {
            HashSet<string> voters = await _Services.Database.Ideas.VotersAmongAsync(idea.Id, recipients, token).ConfigureAwait(false);
            if (voters.Count == 0) return;
            List<string> ids = voters.ToList();
            List<NotificationPreference> preferences = await _Services.Database.NotificationPreferences.EnumerateAsync(ids, token).ConfigureAwait(false);
            List<User> users = await _Services.Database.Users.ReadManyAsync(ids, token).ConfigureAwait(false);
            foreach (User user in users)
            {
                token.ThrowIfCancellationRequested();
                if (user.IsDeleted || !user.EmailVerified || String.IsNullOrWhiteSpace(user.Email)) continue;
                if (!NotificationPreferenceService.Effective(user.Id, preferences, NotificationCategoryEnum.Ideas).Email) continue;
                EmailRequest request = new EmailRequest
                {
                    Template = IdeaShippedEmail.TemplateKey,
                    To = user.Email,
                    UserId = user.Id,
                    IdempotencyKey = "idea_shipped:" + sourceId + ":" + user.Id
                };
                request.Tokens["ideaTitle"] = idea.Title;
                request.Tokens["statusNote"] = idea.StatusNote ?? String.Empty;
                request.Tokens["link"] = _Services.Email.Link("/app" + IdeaPath(idea.Id, null));
                string unsubscribe;
                try
                {
                    unsubscribe = _Services.FeedPostEmails.UnsubscribeUrl(_Services.FeedPostEmails.CreateUnsubscribeToken(user.Id, NotificationCategoryEnum.Ideas));
                    request.Headers["List-Unsubscribe"] = "<" + unsubscribe + ">";
                    request.Headers["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click";
                }
                catch (IntegrationNotConfiguredException)
                {
                    unsubscribe = _Services.Email.Link("/app/settings/notifications");
                }
                request.Tokens["unsubscribeLink"] = unsubscribe;
                await _Services.Email.QueueAsync(request, token).ConfigureAwait(false);
            }
        }

        #endregion
    }
}
