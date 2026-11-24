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
    using TrellisKit.Core.Observability;
    using TrellisKit.Server.Services.Email;
    using TrellisKit.Server.Services.Email.Templates;

    /// <summary>
    /// Library notifications (docs/PHASE4_CONTRACT.md sections 6.2, 8, and 9): the NewLessonInSeries fan-out to series
    /// followers (aggregated per series while unread, only to followers who may open the lesson), the weekly
    /// library_digest email for members who turned Library email on (the Phase 3 digest pattern), and PostComment to
    /// library staff on lessons with notifyStaffOnComment.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class LibraryNotificationService
    {
        #region Public-Members

        /// <summary>
        /// Telemetry email category of the digest.
        /// </summary>
        public const string EmailCategory = "library_digest";

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public LibraryNotificationService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Fan-out handler of NewLessonInSeries jobs (source: the lesson).
        /// </summary>
        /// <param name="job">Job.</param>
        /// <param name="holder">Claim holder.</param>
        /// <param name="claimDuration">Claim extension per batch.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Notifications created or aggregated, or -1 when the claim was lost.</returns>
        public async Task<int> FanoutAsync(NotificationFanout job, string holder, TimeSpan claimDuration, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(job);
            Lesson? lesson = await _Services.Database.Library.Catalog.ReadLessonAsync(job.SourceId, token).ConfigureAwait(false);
            if (lesson == null) return 0;
            LibrarySeries? series = await _Services.Database.Library.Catalog.ReadSeriesAsync(lesson.SeriesId, token).ConfigureAwait(false);
            if (!LibraryAccess.IsLive(lesson, series)) return 0;
            int batchSize = Math.Max(1, _Services.Settings.Notifications.FanoutBatchSize);
            bool needsMembership = AccessDecider.RecipientsNeedMembership(_Services.Access.Current, lesson.Audience);
            int created = 0;
            string? after = job.LastUserId;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                List<string> batch = await _Services.Database.Library.Progress.FollowerIdsAsync(series!.Id, after, batchSize, token).ConfigureAwait(false);
                if (batch.Count == 0) break;
                List<string> recipients = needsMembership ? await MembersAmongAsync(batch, token).ConfigureAwait(false) : batch;
                DateTime now = DateTime.UtcNow;
                List<string> aggregated = await _Services.Database.Library.Catalog.AggregateNewLessonNotificationsAsync(series.Id, recipients, series.Title, now, token).ConfigureAwait(false);
                HashSet<string> done = new HashSet<string>(aggregated, StringComparer.Ordinal);
                List<NotificationDraft> drafts = recipients.Where(r => !done.Contains(r)).Select(r => new NotificationDraft
                {
                    RecipientUserId = r,
                    Type = NotificationTypeEnum.NewLessonInSeries,
                    TargetType = TargetTypeEnum.Series,
                    TargetId = series.Id,
                    TitleText = "New lesson in " + series.Title + ": " + lesson.Title,
                    Body = lesson.Summary,
                    Path = "/lessons/" + lesson.Id,
                    QueuePush = true
                }).ToList();
                int added = aggregated.Count + (drafts.Count == 0 ? 0 : await _Services.Notifications.CreateManyAsync(drafts, false, token).ConfigureAwait(false));
                created += added;
                after = batch[batch.Count - 1];
                if (!await _Services.Database.NotificationFanouts.RecordProgressAsync(job.Id, holder, after, added, DateTime.UtcNow + claimDuration, token).ConfigureAwait(false)) return -1;
                if (batch.Count < batchSize) break;
            }
            return created;
        }

        /// <summary>
        /// PostComment to library staff for a new comment on a lesson with notifyStaffOnComment (never to the author).
        /// </summary>
        /// <param name="lesson">Lesson.</param>
        /// <param name="comment">Comment.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Notifications created.</returns>
        public async Task<int> CommentToStaffAsync(Lesson lesson, Comment comment, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(lesson);
            ArgumentNullException.ThrowIfNull(comment);
            if (!lesson.NotifyStaffOnComment) return 0;
            List<string> staff = await _Services.Database.Library.Catalog.StaffIdsAsync(token).ConfigureAwait(false);
            List<NotificationDraft> drafts = staff.Where(s => !String.Equals(s, comment.AuthorUserId, StringComparison.Ordinal)).Select(s => new NotificationDraft
            {
                RecipientUserId = s,
                Type = NotificationTypeEnum.PostComment,
                ActorUserId = comment.AuthorUserId,
                TargetType = TargetTypeEnum.Lesson,
                TargetId = lesson.Id,
                CommentId = comment.Id,
                Body = comment.Excerpt,
                Path = "/lessons/" + lesson.Id + "?comment=" + comment.Id
            }).ToList();
            return drafts.Count == 0 ? 0 : await _Services.Notifications.CreateManyAsync(drafts, false, token).ConfigureAwait(false);
        }

        /// <summary>
        /// One library_digest pass: members with the Library email preference on get one digest per
        /// library.digestIntervalHours listing their unread Library notifications since the previous digest.
        /// </summary>
        /// <param name="nowUtc">Now.</param>
        /// <param name="limit">Most members checked (1 to 1000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Digests queued.</returns>
        public async Task<int> DigestAsync(DateTime nowUtc, int limit, CancellationToken token = default)
        {
            TimeSpan interval = TimeSpan.FromHours(_Services.Settings.Library.DigestIntervalHours);
            List<EmailDigestCandidate> due = await _Services.Database.NotificationPreferences.ListDigestsDueAsync(new[] { NotificationCategoryEnum.Library }, nowUtc - interval, Math.Clamp(limit, 1, 1000), token).ConfigureAwait(false);
            int sent = 0;
            foreach (EmailDigestCandidate candidate in due)
            {
                token.ThrowIfCancellationRequested();
                DateTime? sentUtc = null;
                if (candidate.LastCheckedUtc != null && await SendDigestAsync(candidate, candidate.LastCheckedUtc.Value, token).ConfigureAwait(false))
                {
                    sentUtc = nowUtc;
                    sent++;
                }
                await _Services.Database.NotificationPreferences.MarkDigestAsync(candidate.UserId, candidate.Category, nowUtc, sentUtc, token).ConfigureAwait(false);
            }
            return sent;
        }

        #endregion

        #region Private-Methods

        private async Task<List<string>> MembersAmongAsync(List<string> userIds, CancellationToken token)
        {
            DateTime now = DateTime.UtcNow;
            List<Entitlement> entitlements = await _Services.Database.Entitlements.EnumerateByUsersAsync(userIds, token).ConfigureAwait(false);
            HashSet<string> members = new HashSet<string>(entitlements.Where(e => e.IsActiveAt(now)).Select(e => e.UserId), StringComparer.Ordinal);
            List<RoleAssignment> roles = await _Services.Database.RoleAssignments.EnumerateByUsersAsync(userIds, token).ConfigureAwait(false);
            foreach (RoleAssignment role in roles.Where(r => r.Role == RoleEnum.SystemAdmin || r.Role == RoleEnum.LibraryEditor)) members.Add(role.UserId);
            return userIds.Where(members.Contains).ToList();
        }

        private async Task<bool> SendDigestAsync(EmailDigestCandidate candidate, DateTime sinceUtc, CancellationToken token)
        {
            long count = await _Services.Database.Notifications.CountUnreadSinceAsync(candidate.UserId, candidate.Category, sinceUtc, token).ConfigureAwait(false);
            if (count == 0) return false;
            List<Notification> items = await _Services.Database.Notifications.ListUnreadSinceAsync(candidate.UserId, candidate.Category, sinceUtc, _Services.Settings.Library.DigestMaxItems, token).ConfigureAwait(false);
            List<string> lines = items.Select(n => n.Title.Trim() + ": " + _Services.Email.Link("/app" + n.Path)).ToList();
            if (count > items.Count) lines.Add("And " + (count - items.Count) + " more.");
            EmailRequest request = new EmailRequest
            {
                Template = LibraryDigestEmail.TemplateKey,
                To = candidate.Email,
                UserId = candidate.UserId,
                IdempotencyKey = "library_digest:" + candidate.UserId + ":" + sinceUtc.Ticks.ToString(CultureInfo.InvariantCulture)
            };
            request.Tokens["memberName"] = candidate.DisplayName;
            request.Tokens["count"] = count.ToString(CultureInfo.InvariantCulture);
            request.Tokens["items"] = String.Join("\n", lines);
            request.Tokens["link"] = _Services.Email.Link("/app/library");
            string unsubscribe;
            try
            {
                unsubscribe = _Services.FeedPostEmails.UnsubscribeUrl(_Services.FeedPostEmails.CreateUnsubscribeToken(candidate.UserId, candidate.Category));
                request.Headers["List-Unsubscribe"] = "<" + unsubscribe + ">";
                request.Headers["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click";
            }
            catch (IntegrationNotConfiguredException)
            {
                unsubscribe = _Services.Email.Link("/app/settings/notifications");
            }
            request.Tokens["unsubscribeLink"] = unsubscribe;
            EmailMessageRecord? queued = await _Services.Email.QueueAsync(request, token).ConfigureAwait(false);
            if (queued != null) TrellisKitTelemetry.Increment(TrellisKitTelemetry.Emails, "category", EmailCategory, "outcome", "queued");
            return queued != null;
        }

        #endregion
    }
}
