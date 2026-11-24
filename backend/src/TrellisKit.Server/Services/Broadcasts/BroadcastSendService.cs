namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Server.Services.Email;
    using TrellisKit.Server.Services.Email.Templates;

    /// <summary>
    /// Broadcast delivery (docs/PHASE5_CONTRACT.md section 8.3), one pass of BroadcastSendWorker under the
    /// broadcast_send job lease: due Scheduled broadcasts start, and every Sending broadcast is expanded in keyset batches
    /// of broadcasts.batchSize over user ids from its cursoruserid, at most broadcasts.maxRecipients in total. Each batch
    /// writes the delivery rows (unique per broadcast and member, so a re-run never duplicates), creates the in-app
    /// Broadcast notifications (category Announcements, path /announcements/{id}; a recipient that already has one is
    /// skipped), marks pushes for the push pipeline (the Announcements push preference unless Essential; pause, notification
    /// hours, tokens, and rate limits apply in PushDeliveryWorker), and queues the broadcast emails (the Announcements email
    /// preference unless Essential, never to suppressed addresses, at most once per member through a claim and an
    /// idempotency key), throttled to broadcasts.emailsPerMinute per property. The cursor advances only after a batch is
    /// complete. Cancel stops the remaining batches. The pass does nothing while features.broadcasts is off. Nothing is published on the realtime bus per recipient (section
    /// 4.12); clients refresh unread counts lazily. The pass does nothing while the broadcasts kill switch is on.
    /// Thread safety: safe to call concurrently (the lease serializes passes).
    /// </summary>
    public class BroadcastSendService
    {
        #region Public-Members

        /// <summary>
        /// Job lease name.
        /// </summary>
        public const string LeaseName = "broadcast_send";

        /// <summary>
        /// Clock. Default DateTime.UtcNow. Tests set a fixed or advancing clock. Never null.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public Func<DateTime> Clock
        {
            get { return _Clock; }
            set { _Clock = value ?? throw new ArgumentNullException(nameof(Clock)); }
        }

        /// <summary>
        /// Broadcasts processed per pass. Default 5. Minimum 1, maximum 100.
        /// </summary>
        public int BroadcastsPerPass
        {
            get { return _BroadcastsPerPass; }
            set { _BroadcastsPerPass = Math.Clamp(value, 1, 100); }
        }

        /// <summary>
        /// Batches per broadcast per pass (other broadcasts get a turn after this many). Default 20. Minimum 1, maximum 1000.
        /// </summary>
        public int BatchesPerPass
        {
            get { return _BatchesPerPass; }
            set { _BatchesPerPass = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// How long after finishing a broadcast its counters keep being refreshed (late push receipts and email delivery
        /// webhooks). Default 7 days. Minimum 1 hour, maximum 30 days.
        /// </summary>
        public TimeSpan StatsRefreshWindow
        {
            get { return _StatsRefreshWindow; }
            set { _StatsRefreshWindow = value < TimeSpan.FromHours(1) ? TimeSpan.FromHours(1) : value > TimeSpan.FromDays(30) ? TimeSpan.FromDays(30) : value; }
        }

        /// <summary>
        /// Lease duration. Default 5 minutes. Minimum 30 seconds, maximum 1 hour.
        /// </summary>
        public TimeSpan LeaseDuration
        {
            get { return _LeaseDuration; }
            set { _LeaseDuration = value < TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : value > TimeSpan.FromHours(1) ? TimeSpan.FromHours(1) : value; }
        }

        /// <summary>
        /// Lease holder identity of this instance.
        /// </summary>
        public string Holder { get; } = LeaseName + "-" + Environment.MachineName + "-" + Guid.NewGuid().ToString("N").Substring(0, 12);

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private readonly BroadcastSegmentService _Segments;
        private Func<DateTime> _Clock = () => DateTime.UtcNow;
        private int _BroadcastsPerPass = 5;
        private int _BatchesPerPass = 20;
        private TimeSpan _StatsRefreshWindow = TimeSpan.FromDays(7);
        private TimeSpan _LeaseDuration = TimeSpan.FromMinutes(5);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <param name="segments">Segments (audience compilation).</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public BroadcastSendService(ApplicationServices services, BroadcastSegmentService segments)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            _Segments = segments ?? throw new ArgumentNullException(nameof(segments));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// One pass: start due broadcasts, deliver batches of the Sending ones, refresh recent counters.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Recipients processed.</returns>
        public async Task<int> RunPassAsync(CancellationToken token = default)
        {
            if (!_Services.Property.Property.Features.Broadcasts) return 0;
            if (await _Services.LiveUpdates.AppConfig.IsKillSwitchOnAsync("broadcasts", token).ConfigureAwait(false)) return 0;
            if (!await _Services.Database.JobLeases.TryAcquireAsync(LeaseName, Holder, DateTime.UtcNow, _LeaseDuration, token).ConfigureAwait(false)) return 0;
            try
            {
                using (Activity? activity = TrellisKitTelemetry.StartSpan("job BroadcastSendWorker"))
                {
                    DateTime now = _Clock();
                    await _Services.Database.Broadcasts.StartDueAsync(now, _BroadcastsPerPass, token).ConfigureAwait(false);
                    int processed = 0;
                    foreach (string id in await _Services.Database.Broadcasts.ListSendingAsync(_BroadcastsPerPass, token).ConfigureAwait(false))
                    {
                        token.ThrowIfCancellationRequested();
                        processed += await ProcessAsync(id, token).ConfigureAwait(false);
                    }
                    foreach (string id in await _Services.Database.Broadcasts.ListRecentlyFinishedAsync(_Clock() - _StatsRefreshWindow, 20, token).ConfigureAwait(false))
                        await _Services.Database.Broadcasts.SyncStatsAsync(id, _Clock(), token).ConfigureAwait(false);
                    activity?.SetTag("processed", processed);
                    return processed;
                }
            }
            finally
            {
                await _Services.Database.JobLeases.ReleaseAsync(LeaseName, Holder, CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Deliver one batch of recipients of a broadcast: delivery rows, in-app notifications and push marks, and emails up
        /// to the email budget.
        /// </summary>
        /// <param name="broadcast">Broadcast.</param>
        /// <param name="recipients">Recipients.</param>
        /// <param name="emailBudget">Most emails to queue now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Emails left Pending because the budget ran out (0 when the batch is complete).</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<int> DeliverBatchAsync(Broadcast broadcast, List<BroadcastRecipient> recipients, int emailBudget, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(broadcast);
            ArgumentNullException.ThrowIfNull(recipients);
            if (recipients.Count == 0) return 0;
            using (Activity? activity = TrellisKitTelemetry.StartSpan("broadcast.batch"))
            {
                activity?.SetTag("broadcast.id", broadcast.Id);
                activity?.SetTag("job.batch_size", recipients.Count);
                DateTime now = _Clock();
                BroadcastDeliveryStatusEnum Requested(bool on) => on ? BroadcastDeliveryStatusEnum.Pending : BroadcastDeliveryStatusEnum.NotRequested;
                List<BroadcastDelivery> rows = recipients.Select(r => new BroadcastDelivery
                {
                    Id = IdGenerator.Generate(BroadcastIdPrefixes.Delivery),
                    BroadcastId = broadcast.Id,
                    UserId = r.UserId,
                    InAppStatus = Requested(broadcast.InApp),
                    PushStatus = Requested(broadcast.Push),
                    EmailStatus = Requested(broadcast.Email),
                    CreatedUtc = now,
                    UpdatedUtc = now
                }).ToList();
                await _Services.Database.Broadcasts.CreateDeliveriesAsync(rows, token).ConfigureAwait(false);
                List<BroadcastDelivery> pending = await _Services.Database.Broadcasts.ListPendingAsync(broadcast.Id, recipients.Select(r => r.UserId), token).ConfigureAwait(false);
                if (pending.Count == 0) return 0;
                Dictionary<string, BroadcastRecipient> byUser = recipients.GroupBy(r => r.UserId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
                bool exempt = broadcast.Category == BroadcastCategoryEnum.Essential || broadcast.IsTest;
                await NotificationsAsync(broadcast, pending.Where(d => d.InAppStatus == BroadcastDeliveryStatusEnum.Pending).ToList(), byUser, exempt, token).ConfigureAwait(false);
                return await EmailsAsync(broadcast, pending.Where(d => d.EmailStatus == BroadcastDeliveryStatusEnum.Pending).ToList(), byUser, exempt, emailBudget, token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

        private async Task<int> ProcessAsync(string id, CancellationToken token)
        {
            Broadcast? broadcast = await _Services.Database.Broadcasts.ReadAsync(id, token).ConfigureAwait(false);
            if (broadcast == null || broadcast.Status != BroadcastStatusEnum.Sending) return 0;
            BroadcastAudience audience;
            try
            {
                audience = await _Segments.CompileAsync(broadcast.Filters, token).ConfigureAwait(false);
            }
            catch (ApiErrorException e)
            {
                _Services.Logger.Warn("[BroadcastSendService] [p:" + _Services.Property.Property.Id + "] broadcast " + broadcast.Id + " canceled: its segment no longer applies (" + e.Message + ")");
                await _Services.Database.Broadcasts.TransitionAsync(broadcast.Id, new[] { BroadcastStatusEnum.Sending }, BroadcastStatusEnum.Canceled, null, _Clock(), token).ConfigureAwait(false);
                return 0;
            }
            int processed = 0;
            string? cursor = broadcast.CursorUserId;
            int batchSize = _Services.Settings.Broadcasts.BatchSize;
            int maxRecipients = _Services.Settings.Broadcasts.MaxRecipients;
            for (int batch = 0; batch < _BatchesPerPass; batch++)
            {
                token.ThrowIfCancellationRequested();
                BroadcastStats stats = await _Services.Database.Broadcasts.SyncStatsAsync(broadcast.Id, _Clock(), token).ConfigureAwait(false);
                int remaining = maxRecipients - stats.Targeted;
                List<BroadcastRecipient> recipients = remaining <= 0 ? new List<BroadcastRecipient>()
                    : await _Services.Database.Broadcasts.EnumerateRecipientsAsync(audience, cursor, Math.Min(batchSize, remaining), _Clock(), token).ConfigureAwait(false);
                if (recipients.Count == 0)
                {
                    await CompleteAsync(broadcast, token).ConfigureAwait(false);
                    return processed;
                }
                long queuedLastMinute = await _Services.Database.Broadcasts.CountEmailsQueuedSinceAsync(_Clock().AddMinutes(-1), token).ConfigureAwait(false);
                int budget = (int)Math.Max(0, _Services.Settings.Broadcasts.EmailsPerMinute - queuedLastMinute);
                int throttled = await DeliverBatchAsync(broadcast, recipients, budget, token).ConfigureAwait(false);
                processed += recipients.Count;
                if (throttled > 0)
                {
                    // The batch stays at the cursor; the next pass resumes its pending emails once the minute allows.
                    TrellisKitTelemetry.Add(BroadcastTelemetry.Deliveries, throttled, "channel", "email", "outcome", "throttled");
                    await _Services.Database.Broadcasts.SyncStatsAsync(broadcast.Id, _Clock(), token).ConfigureAwait(false);
                    return processed;
                }
                cursor = recipients[recipients.Count - 1].UserId;
                if (!await _Services.Database.Broadcasts.RecordProgressAsync(broadcast.Id, cursor, _Clock(), token).ConfigureAwait(false))
                {
                    // Canceled (or finished elsewhere): the remaining batches stop.
                    await _Services.Database.Broadcasts.SyncStatsAsync(broadcast.Id, _Clock(), token).ConfigureAwait(false);
                    return processed;
                }
                if (recipients.Count < Math.Min(batchSize, remaining) || recipients.Count >= remaining)
                {
                    await CompleteAsync(broadcast, token).ConfigureAwait(false);
                    return processed;
                }
            }
            await _Services.Database.Broadcasts.SyncStatsAsync(broadcast.Id, _Clock(), token).ConfigureAwait(false);
            return processed;
        }

        private async Task CompleteAsync(Broadcast broadcast, CancellationToken token)
        {
            await _Services.Database.Broadcasts.TransitionAsync(broadcast.Id, new[] { BroadcastStatusEnum.Sending }, BroadcastStatusEnum.Sent, null, _Clock(), token).ConfigureAwait(false);
            await TrellisKit.Server.Services.MetricAnnotations.RecordAsync(_Services, "Broadcast", "broadcast:" + broadcast.Id, "Broadcast sent", token).ConfigureAwait(false);
            await _Services.Database.Broadcasts.SyncStatsAsync(broadcast.Id, _Clock(), token).ConfigureAwait(false);
        }

        private async Task NotificationsAsync(Broadcast broadcast, List<BroadcastDelivery> deliveries, Dictionary<string, BroadcastRecipient> byUser, bool exempt, CancellationToken token)
        {
            if (deliveries.Count == 0) return;
            DateTime now = _Clock();
            Dictionary<string, string> existing = await _Services.Database.Broadcasts.NotificationIdsAsync(broadcast.Id, deliveries.Select(d => d.UserId), token).ConfigureAwait(false);
            string title = Excerpt(broadcast.Push ? broadcast.PushTitle ?? broadcast.InAppTitle ?? broadcast.Title : broadcast.InAppTitle ?? broadcast.Title, 200);
            string? body = broadcast.Push && !String.IsNullOrWhiteSpace(broadcast.PushBody) ? broadcast.PushBody : NullIfEmpty(MarkdownNormalizer.Excerpt(broadcast.InAppBody));
            List<Notification> rows = new List<Notification>();
            foreach (BroadcastDelivery delivery in deliveries)
            {
                BroadcastRecipient? recipient = byUser.TryGetValue(delivery.UserId, out BroadcastRecipient? r) ? r : null;
                BroadcastDeliveryStatusEnum push = delivery.PushStatus;
                if (push == BroadcastDeliveryStatusEnum.Pending)
                {
                    if (recipient == null || !recipient.HasPushToken) push = BroadcastDeliveryStatusEnum.NoToken;
                    else if (!exempt && !recipient.PushPreference) push = BroadcastDeliveryStatusEnum.SkippedPreference;
                    else push = BroadcastDeliveryStatusEnum.Queued;
                    TrellisKitTelemetry.Add(BroadcastTelemetry.Deliveries, 1, "channel", "push", "outcome", Outcome(push));
                }
                if (!existing.TryGetValue(delivery.UserId, out string? notificationId))
                {
                    notificationId = IdGenerator.Generate(Constants.NotificationIdPrefix);
                    rows.Add(new Notification
                    {
                        Id = notificationId,
                        RecipientUserId = delivery.UserId,
                        Type = NotificationTypeEnum.Broadcast,
                        Category = NotificationCategoryEnum.Announcements,
                        TargetType = TargetTypeEnum.Broadcast.ToString(),
                        TargetId = broadcast.Id,
                        Title = title,
                        Body = body,
                        Path = "/announcements/" + broadcast.Id,
                        PushState = push == BroadcastDeliveryStatusEnum.Queued ? PushStateEnum.Pending : PushStateEnum.None,
                        CreatedUtc = now,
                        UpdatedUtc = now
                    });
                }
                delivery.NotificationId = notificationId;
                delivery.InAppStatus = BroadcastDeliveryStatusEnum.Created;
                delivery.PushStatus = push;
            }
            if (rows.Count > 0)
            {
                await _Services.Database.Notifications.CreateManyAsync(rows, token).ConfigureAwait(false);
                TrellisKitTelemetry.Add(TrellisKitTelemetry.NotificationsCreated, rows.Count, "type", NotificationTypeEnum.Broadcast.ToString(), "aggregated", "false");
                TrellisKitTelemetry.Add(BroadcastTelemetry.Deliveries, rows.Count, "channel", "in_app", "outcome", "created");
            }
            await _Services.Database.Broadcasts.UpdateNotificationStatusesAsync(deliveries, now, token).ConfigureAwait(false);
        }

        private async Task<int> EmailsAsync(Broadcast broadcast, List<BroadcastDelivery> deliveries, Dictionary<string, BroadcastRecipient> byUser, bool exempt, int budget, CancellationToken token)
        {
            int left = budget;
            for (int i = 0; i < deliveries.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                BroadcastDelivery delivery = deliveries[i];
                BroadcastRecipient? recipient = byUser.TryGetValue(delivery.UserId, out BroadcastRecipient? r) ? r : null;
                DateTime now = _Clock();
                BroadcastDeliveryStatusEnum? settled = null;
                if (recipient == null || !recipient.EmailVerified || String.IsNullOrWhiteSpace(recipient.Email)) settled = BroadcastDeliveryStatusEnum.NoAddress;
                else if (!exempt && !recipient.EmailPreference) settled = BroadcastDeliveryStatusEnum.SkippedPreference;
                else if (recipient.EmailSuppressed) settled = BroadcastDeliveryStatusEnum.Suppressed;
                if (settled != null)
                {
                    if (await _Services.Database.Broadcasts.SettleEmailAsync(delivery.Id, settled.Value, now, token).ConfigureAwait(false))
                        TrellisKitTelemetry.Add(BroadcastTelemetry.Deliveries, 1, "channel", "email", "outcome", Outcome(settled.Value));
                    continue;
                }
                if (left <= 0) return deliveries.Count - i;
                if (!await _Services.Database.Broadcasts.ClaimEmailAsync(delivery.Id, now, token).ConfigureAwait(false)) continue;
                left--;
                EmailMessageRecord? record = await _Services.Email.QueueAsync(Request(broadcast, recipient!), token).ConfigureAwait(false);
                BroadcastDeliveryStatusEnum status = StatusOf(record);
                await _Services.Database.Broadcasts.SetEmailMessageAsync(delivery.Id, record?.Id, status, _Clock(), token).ConfigureAwait(false);
                TrellisKitTelemetry.Add(BroadcastTelemetry.Deliveries, 1, "channel", "email", "outcome", Outcome(status));
            }
            return 0;
        }

        private EmailRequest Request(Broadcast broadcast, BroadcastRecipient recipient)
        {
            bool essential = broadcast.Category == BroadcastCategoryEnum.Essential;
            string heading = broadcast.InAppTitle ?? broadcast.Title;
            EmailRequest request = new EmailRequest
            {
                Template = BroadcastEmail.TemplateKey,
                To = recipient.Email!,
                UserId = recipient.UserId,
                IdempotencyKey = "broadcast:" + broadcast.Id + ":" + recipient.UserId
            };
            request.Tokens["memberName"] = recipient.DisplayName;
            request.Tokens["subject"] = broadcast.EmailSubject ?? heading;
            request.Tokens["preheader"] = broadcast.EmailPreheader ?? String.Empty;
            request.Tokens["heading"] = heading;
            request.Tokens["body"] = broadcast.EmailBody ?? broadcast.InAppBody ?? String.Empty;
            request.Tokens["ctaLabel"] = broadcast.CtaLabel ?? "Read more";
            request.Tokens["ctaLink"] = _Services.Email.Link("/app" + (broadcast.CtaPath ?? "/announcements/" + broadcast.Id));
            request.AddListUnsubscribe = !essential;
            request.Essential = essential;
            string unsubscribe;
            try
            {
                unsubscribe = _Services.FeedPostEmails.UnsubscribeUrl(_Services.FeedPostEmails.CreateUnsubscribeToken(recipient.UserId, NotificationCategoryEnum.Announcements));
                if (!essential)
                {
                    request.Headers["List-Unsubscribe"] = "<" + unsubscribe + ">";
                    request.Headers["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click";
                }
            }
            catch (IntegrationNotConfiguredException)
            {
                unsubscribe = _Services.Email.Link("/app/settings/notifications");
            }
            request.Tokens["unsubscribeLink"] = unsubscribe;
            request.Tokens["footerNote"] = essential
                ? "This is an essential notice, so it is sent even when announcement emails are off in your notification settings."
                : "You get announcements by email because Announcements emails are on in your notification settings. Turn them off: " + unsubscribe;
            return request;
        }

        private static BroadcastDeliveryStatusEnum StatusOf(EmailMessageRecord? record)
        {
            if (record == null) return BroadcastDeliveryStatusEnum.Queued;
            switch (record.Status)
            {
                case EmailStatusEnum.Sent:
                    return BroadcastDeliveryStatusEnum.Sent;
                case EmailStatusEnum.Delivered:
                    return BroadcastDeliveryStatusEnum.Delivered;
                case EmailStatusEnum.Failed:
                    return BroadcastDeliveryStatusEnum.Failed;
                case EmailStatusEnum.Suppressed:
                    return BroadcastDeliveryStatusEnum.Suppressed;
                default:
                    return BroadcastDeliveryStatusEnum.Queued;
            }
        }

        private static string Outcome(BroadcastDeliveryStatusEnum status)
        {
            switch (status)
            {
                case BroadcastDeliveryStatusEnum.SkippedPreference:
                    return "skipped_preference";
                case BroadcastDeliveryStatusEnum.NoToken:
                    return "no_token";
                case BroadcastDeliveryStatusEnum.NoAddress:
                    return "no_address";
                case BroadcastDeliveryStatusEnum.Suppressed:
                    return "suppressed";
                case BroadcastDeliveryStatusEnum.Failed:
                    return "failed";
                case BroadcastDeliveryStatusEnum.Sent:
                    return "sent";
                default:
                    return "queued";
            }
        }

        private static string Excerpt(string text, int max)
        {
            string value = (text ?? String.Empty).Trim();
            return value.Length <= max ? value : value.Substring(0, max);
        }

        private static string? NullIfEmpty(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value;
        }

        #endregion
    }
}
