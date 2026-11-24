namespace TrellisKit.Server.Workers
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Runs notification fan-out jobs (Phase 2 contract 8.2 and 8.9). Jobs are claimed with a conditional update, so
    /// several instances share the queue. A NewFeedPost job fans out only a post that is published, not deleted, and not
    /// moderated, to every active member (not deleted, not banned, age attested, with an active entitlement or
    /// SystemAdmin, which is exactly ContentAudienceRule.RecipientMayOpen for every post audience under the FullyGated
    /// model; NotificationService.CreateManyAsync checks each recipient against the post's audience again) except the
    /// author and anyone on either side of a block with the author, in keyset batches of
    /// notifications.fanoutBatchSize ordered by user id. Each batch creates the in-app notifications in bulk (FeedPosts
    /// in-app preference respected, pushes queued, recipients that already have one skipped), sends the feed post
    /// emails, then records lastuserid, so a crashed job resumes after the last finished batch. Emails go to members
    /// with a verified address whose FeedPosts email preference is on, at most one per
    /// email.feedPostMinIntervalMinutes (posts in between are recorded Skipped), never twice per post (emailsends), in
    /// sub-batches of email.feedPostBatchSize, and not at all when email.feedPostEmailsEnabled is false. A claimed email
    /// row is recorded before sending (at most once). Unknown job types complete without work.
    /// <see cref="Signal"/> starts a pass immediately (wired to NotificationService.FanoutSignal).
    /// </summary>
    public sealed class NotificationFanoutWorker : IntervalWorker
    {
        #region Public-Members

        /// <inheritdoc />
        public override string JobName
        {
            get { return "notification_fanout"; }
        }

        /// <summary>
        /// Clock. Default DateTime.UtcNow. Tests set a fixed clock. Never null.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public Func<DateTime> Clock
        {
            get { return _Clock; }
            set { _Clock = value ?? throw new ArgumentNullException(nameof(Clock)); }
        }

        /// <summary>
        /// Seconds between passes when nothing signals the worker: the live setting notifications.fanoutIntervalSeconds
        /// (default 15, minimum 1, maximum 3600).
        /// </summary>
        public int IntervalSeconds
        {
            get { return _Settings.Notifications.FanoutIntervalSeconds; }
        }

        /// <summary>
        /// Jobs claimed per pass. Default 5. Minimum 1, maximum 100.
        /// </summary>
        public int JobsPerPass
        {
            get { return _JobsPerPass; }
            set { _JobsPerPass = Math.Clamp(value, 1, 100); }
        }

        /// <summary>
        /// Claim length, renewed after every batch. Default 5 minutes. Minimum 30 seconds, maximum 1 hour.
        /// </summary>
        public TimeSpan ClaimDuration
        {
            get { return _ClaimDuration; }
            set { _ClaimDuration = value < TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : value > TimeSpan.FromHours(1) ? TimeSpan.FromHours(1) : value; }
        }

        /// <summary>
        /// Supplies the property's current access configuration. Default FullyGated (every recipient needs a
        /// membership).
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public Func<AccessPolicy> Access
        {
            get { return _Access; }
            set { _Access = value ?? throw new ArgumentNullException(nameof(Access)); }
        }

        /// <summary>
        /// Claim holder identity of this instance.
        /// </summary>
        public string Holder { get; } = "fanout-" + Guid.NewGuid().ToString("N");

        #endregion

        #region Protected-Members

        /// <inheritdoc />
        protected override TimeSpan Interval
        {
            get { return TimeSpan.FromSeconds(IntervalSeconds); }
        }

        /// <inheritdoc />
        protected override TimeSpan InitialDelay
        {
            get { return TimeSpan.FromSeconds(15); }
        }

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly NotificationService _Notifications;
        private readonly FeedPostEmailService _Emails;
        private readonly UserSummaryService _Summaries;
        private readonly AppSettings _Settings;
        private Func<DateTime> _Clock = () => DateTime.UtcNow;
        private Func<AccessPolicy> _Access = () => AccessPolicy.FullyGatedDefault;
        private int _JobsPerPass = 5;
        private TimeSpan _ClaimDuration = TimeSpan.FromMinutes(5);
        private int _Signaled = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the worker.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="notifications">Notification service.</param>
        /// <param name="emails">Feed post email service.</param>
        /// <param name="summaries">User summary builder.</param>
        /// <param name="settings">Running settings (live).</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public NotificationFanoutWorker(DatabaseDriverBase database, NotificationService notifications, FeedPostEmailService emails, UserSummaryService summaries, AppSettings settings, AppLogger logger)
            : base(logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
            _Emails = emails ?? throw new ArgumentNullException(nameof(emails));
            _Summaries = summaries ?? throw new ArgumentNullException(nameof(summaries));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start a pass in the background now, unless a signaled pass is already running. Never throws.
        /// </summary>
        /// <param name="token">Token that cancels the pass.</param>
        public void Signal(CancellationToken token = default)
        {
            if (Interlocked.CompareExchange(ref _Signaled, 1, 0) != 0) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    await RunOnceAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Shutting down.
                }
                catch (Exception e)
                {
                    Logger.Error("[NotificationFanoutWorker] signaled pass failed", e);
                }
                finally
                {
                    Interlocked.Exchange(ref _Signaled, 0);
                }
            }, CancellationToken.None);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<int> ExecuteAsync(CancellationToken token)
        {
            List<NotificationFanout> jobs = await _Database.NotificationFanouts.ClaimAsync(Holder, _Clock(), _ClaimDuration, _JobsPerPass, token).ConfigureAwait(false);
            int created = 0;
            foreach (NotificationFanout job in jobs)
            {
                token.ThrowIfCancellationRequested();
                created += await ProcessAsync(job, token).ConfigureAwait(false);
            }
            return created;
        }

        #endregion

        #region Private-Methods

        private async Task<int> ProcessAsync(NotificationFanout job, CancellationToken token)
        {
            using (Activity? activity = TrellisKitTelemetry.StartSpan("notifications.fanout"))
            {
                activity?.SetTag("notification.type", job.Type.ToString());
                Stopwatch stopwatch = Stopwatch.StartNew();
                int created = 0;
                Func<NotificationFanout, string, TimeSpan, CancellationToken, Task<int>>? handler = null;
                if (job.Type != NotificationTypeEnum.NewFeedPost && !_Notifications.FanoutHandlers.TryGetValue(job.Type, out handler)) handler = _Notifications.ExtraFanout;
                if (job.Type != NotificationTypeEnum.NewFeedPost && handler != null)
                {
                    int extra = await handler(job, Holder, _ClaimDuration, token).ConfigureAwait(false);
                    if (extra < 0)
                    {
                        Logger.Warn("[NotificationFanoutWorker] lost the claim on job " + job.Id + "; another instance continues it");
                        return 0;
                    }
                    await _Database.NotificationFanouts.CompleteAsync(job.Id, Holder, _Clock(), token).ConfigureAwait(false);
                    TrellisKitTelemetry.Record(TrellisKitTelemetry.NotificationFanoutDuration, stopwatch.Elapsed.TotalSeconds, "type", job.Type.ToString());
                    return extra;
                }
                NotificationSourcePost? post = job.Type == NotificationTypeEnum.NewFeedPost ? await _Database.Notifications.ReadSourcePostAsync(job.SourceId, token).ConfigureAwait(false) : null;
                if (post != null && post.IsFanoutable)
                {
                    // The access model decides who may open the post: recipients (in-app, push, and email) are
                    // pre-filtered by membership only where the post's audience needs one (docs/ACCESS_MODELS_CONTRACT.md
                    // section 7); NotificationService checks each recipient against the audience again.
                    FeedPost? source = await _Database.FeedPosts.ReadAsync(post.Id, token).ConfigureAwait(false);
                    bool requireMembership = AccessDecider.RecipientsNeedMembership(Access(), source?.Audience ?? ContentAudienceEnum.Members);
                    string authorName = (await _Summaries.BuildOneAsync(post.AuthorUserId, token).ConfigureAwait(false)).DisplayName;
                    string? after = job.LastUserId;
                    int batchSize = _Settings.Notifications.FanoutBatchSize;
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        DateTime now = _Clock();
                        List<FanoutRecipient> batch = await _Database.Notifications.EnumerateFanoutRecipientsAsync(post.AuthorUserId, NotificationCategoryEnum.FeedPosts,
                            NotificationCatalog.DefaultEmail(NotificationCategoryEnum.FeedPosts), after, batchSize, requireMembership, now, token).ConfigureAwait(false);
                        if (batch.Count == 0) break;
                        activity?.SetTag("job.batch_size", batch.Count);
                        List<NotificationDraft> drafts = batch.Where(r => r.InApp).Select(r => new NotificationDraft
                        {
                            RecipientUserId = r.UserId,
                            Type = NotificationTypeEnum.NewFeedPost,
                            ActorUserId = post.AuthorUserId,
                            TargetType = TargetTypeEnum.FeedPost,
                            TargetId = post.Id,
                            FeedPostId = post.Id,
                            TitleText = post.Title,
                            Body = String.IsNullOrWhiteSpace(post.Excerpt) ? null : post.Excerpt,
                            Path = "/feed/" + post.Id
                        }).ToList();
                        int added = await _Notifications.CreateManyAsync(drafts, true, token).ConfigureAwait(false);
                        created += added;
                        await SendEmailsAsync(batch, post, authorName, token).ConfigureAwait(false);
                        after = batch[batch.Count - 1].UserId;
                        if (!await _Database.NotificationFanouts.RecordProgressAsync(job.Id, Holder, after, added, _Clock() + _ClaimDuration, token).ConfigureAwait(false))
                        {
                            Logger.Warn("[NotificationFanoutWorker] lost the claim on job " + job.Id + "; another instance continues it");
                            return created;
                        }
                        if (batch.Count < batchSize) break;
                    }
                }
                await _Database.NotificationFanouts.CompleteAsync(job.Id, Holder, _Clock(), token).ConfigureAwait(false);
                TrellisKitTelemetry.Record(TrellisKitTelemetry.NotificationFanoutDuration, stopwatch.Elapsed.TotalSeconds, "type", job.Type.ToString());
                activity?.SetTag("created", created);
                return created;
            }
        }

        private async Task SendEmailsAsync(List<FanoutRecipient> batch, NotificationSourcePost post, string authorName, CancellationToken token)
        {
            if (!_Settings.Email.FeedPostEmailsEnabled) return;
            int interval = _Settings.Email.FeedPostMinIntervalMinutes;
            int chunk = _Settings.Email.FeedPostBatchSize;
            for (int i = 0; i < batch.Count; i += chunk)
            {
                foreach (FanoutRecipient recipient in batch.Skip(i).Take(chunk))
                {
                    token.ThrowIfCancellationRequested();
                    if (!recipient.EmailVerified || String.IsNullOrWhiteSpace(recipient.Email)) continue;
                    if (!recipient.EmailOn)
                    {
                        TrellisKitTelemetry.Add(TrellisKitTelemetry.FeedPostEmails, 1, "outcome", "skipped_preference");
                        continue;
                    }
                    DateTime now = _Clock();
                    EmailSend send = new EmailSend
                    {
                        Id = IdGenerator.Generate(Constants.EmailSendIdPrefix),
                        UserId = recipient.UserId,
                        Category = NotificationCategoryEnum.FeedPosts,
                        SourceId = post.Id,
                        Status = EmailSendStatusEnum.Sent,
                        CreatedUtc = now
                    };
                    if (interval > 0 && await _Database.EmailSends.SentSinceAsync(recipient.UserId, NotificationCategoryEnum.FeedPosts, now.AddMinutes(-interval), post.Id, token).ConfigureAwait(false))
                    {
                        send.Status = EmailSendStatusEnum.Skipped;
                        if (await _Database.EmailSends.TryCreateAsync(send, token).ConfigureAwait(false)) TrellisKitTelemetry.Add(TrellisKitTelemetry.FeedPostEmails, 1, "outcome", "skipped_interval");
                        continue;
                    }
                    if (!await _Database.EmailSends.TryCreateAsync(send, token).ConfigureAwait(false)) continue;
                    bool ok;
                    try
                    {
                        ok = await _Emails.QueueAsync(recipient, post, authorName, token).ConfigureAwait(false);
                    }
                    catch (IntegrationNotConfiguredException e)
                    {
                        Logger.Warn("[NotificationFanoutWorker] feed post email not built: " + e.Message);
                        ok = false;
                    }
                    if (!ok) await _Database.EmailSends.UpdateStatusAsync(send.Id, EmailSendStatusEnum.Error, token).ConfigureAwait(false);
                    TrellisKitTelemetry.Add(TrellisKitTelemetry.FeedPostEmails, 1, "outcome", ok ? "sent" : "error");
                }
            }
        }

        #endregion
    }
}
