namespace TrellisKit.Server.Workers
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Sends pushes for Pending notifications and checks their receipts (Phase 2 contract 8.8). A singleton lease in
    /// jobleases keeps one instance sending, and each notification moves out of Pending with a conditional update
    /// before it is sent, so a push is sent at most once. Checks in order, each with its trelliskit.push.sends outcome:
    /// push.enabled false (suppressed_preference), the category's push preference or in-app off (suppressed_preference),
    /// pushPaused (suppressed_paused), outside notification hours (suppressed_hours, suppressed and never delayed), no
    /// active token (no_token: tokens that are disabled or whose session is revoked or expired do not count),
    /// push.maxPerUserPerHour reached (suppressed_rate), and a Reaction target already pushed within the hour
    /// (suppressed_rate). Each message carries badge = the recipient's unread count, sound default, channelId default,
    /// priority high, and data { notificationId, type, path }. Tickets are stored in pushdeliveries; receipts are read
    /// push.receiptDelayMinutes later: DeviceNotRegistered deletes the token (pruned), other errors count per token and
    /// disable it after <see cref="DisableAfterFailures"/> consecutive failures. Interval push.sendIntervalSeconds.
    /// </summary>
    public sealed class PushDeliveryWorker : IntervalWorker
    {
        #region Public-Members

        /// <inheritdoc />
        public override string JobName
        {
            get { return "push_delivery"; }
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
        /// Pending notifications handled per pass. Default 500. Minimum 1, maximum 5000.
        /// </summary>
        public int BatchSize
        {
            get { return _BatchSize; }
            set { _BatchSize = Math.Clamp(value, 1, 5000); }
        }

        /// <summary>
        /// Receipts checked per pass. Default 1000. Minimum 1, maximum 10000.
        /// </summary>
        public int ReceiptBatchSize
        {
            get { return _ReceiptBatchSize; }
            set { _ReceiptBatchSize = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// Consecutive failures that disable a token. Default 5. Minimum 1, maximum 100.
        /// </summary>
        public int DisableAfterFailures
        {
            get { return _DisableAfterFailures; }
            set { _DisableAfterFailures = Math.Clamp(value, 1, 100); }
        }

        /// <summary>
        /// Tickets without a receipt after this many hours are closed as errors. Default 24. Minimum 1, maximum 168.
        /// </summary>
        public int ReceiptGiveUpHours
        {
            get { return _ReceiptGiveUpHours; }
            set { _ReceiptGiveUpHours = Math.Clamp(value, 1, 168); }
        }

        /// <summary>
        /// Lease holder identity of this instance.
        /// </summary>
        public string Holder { get; } = "push-" + Guid.NewGuid().ToString("N");

        /// <summary>
        /// Optional hook that names the pending notifications exempt from their category's push preference (Essential
        /// broadcasts, docs/PHASE5_CONTRACT.md section 8.3). Pause, notification hours, tokens, and rate limits still apply.
        /// Default null (no exemptions).
        /// </summary>
        public Func<IReadOnlyList<Notification>, CancellationToken, Task<HashSet<string>>>? PreferenceExemptions { get; set; } = null;

        #endregion

        #region Protected-Members

        /// <inheritdoc />
        protected override TimeSpan Interval
        {
            get { return TimeSpan.FromSeconds(_Settings.Push.SendIntervalSeconds); }
        }

        /// <inheritdoc />
        protected override TimeSpan InitialDelay
        {
            get { return TimeSpan.FromSeconds(10); }
        }

        #endregion

        #region Private-Members

        private const string _DeviceNotRegistered = "DeviceNotRegistered";
        private readonly DatabaseDriverBase _Database;
        private readonly IPushSender _Sender;
        private readonly AppSettings _Settings;
        private Func<DateTime> _Clock = () => DateTime.UtcNow;
        private int _BatchSize = 500;
        private int _ReceiptBatchSize = 1000;
        private int _DisableAfterFailures = 5;
        private int _ReceiptGiveUpHours = 24;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the worker.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="sender">Push sender.</param>
        /// <param name="settings">Running settings (live).</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public PushDeliveryWorker(DatabaseDriverBase database, IPushSender sender, AppSettings settings, AppLogger logger) : base(logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Sender = sender ?? throw new ArgumentNullException(nameof(sender));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<int> ExecuteAsync(CancellationToken token)
        {
            DateTime now = _Clock();
            TimeSpan lease = TimeSpan.FromSeconds(Math.Max(60, _Settings.Push.SendIntervalSeconds * 4));
            if (!await _Database.JobLeases.TryAcquireAsync(JobName, Holder, now, lease, token).ConfigureAwait(false)) return 0;
            int processed = await SendPassAsync(now, token).ConfigureAwait(false);
            processed += await ReceiptPassAsync(_Clock(), token).ConfigureAwait(false);
            return processed;
        }

        #endregion

        #region Private-Methods

        private async Task<int> SendPassAsync(DateTime now, CancellationToken token)
        {
            List<Notification> pending = await _Database.Notifications.EnumeratePendingPushAsync(_BatchSize, token).ConfigureAwait(false);
            if (pending.Count == 0) return 0;
            using (Activity? activity = TrellisKitTelemetry.StartSpan("push.send"))
            {
                activity?.SetTag("job.batch_size", pending.Count);
                List<string> recipients = pending.Select(n => n.RecipientUserId).Distinct(StringComparer.Ordinal).ToList();
                List<NotificationPreference> preferences = await _Database.NotificationPreferences.EnumerateAsync(recipients, token).ConfigureAwait(false);
                HashSet<string> exempt = PreferenceExemptions == null ? new HashSet<string>(StringComparer.Ordinal) : await PreferenceExemptions(pending, token).ConfigureAwait(false);
                Dictionary<string, NotificationHours> hours = (await _Database.NotificationPreferences.EnumerateHoursAsync(recipients, token).ConfigureAwait(false))
                    .ToDictionary(h => h.UserId, StringComparer.Ordinal);
                Dictionary<string, List<PushToken>> tokens = (await _Database.PushTokens.EnumerateActiveByUsersAsync(recipients, now, token).ConfigureAwait(false))
                    .GroupBy(t => t.UserId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
                Dictionary<string, long> pushedThisHour = new Dictionary<string, long>(StringComparer.Ordinal);
                HashSet<string> groupsPushed = new HashSet<string>(StringComparer.Ordinal);
                Dictionary<string, List<string>> suppressed = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                List<Notification> toSend = new List<Notification>();
                DateTime hourAgo = now.AddHours(-1);

                foreach (Notification notification in pending)
                {
                    token.ThrowIfCancellationRequested();
                    string recipient = notification.RecipientUserId;
                    NotificationHours memberHours = hours.TryGetValue(recipient, out NotificationHours? h) ? h : NotificationPreferenceService.DefaultHours(_Settings.Notifications, recipient);
                    string? outcome = null;
                    if (!_Settings.Push.Enabled) outcome = "suppressed_preference";
                    else if (!exempt.Contains(notification.Id) && !NotificationPreferenceService.Effective(recipient, preferences, notification.Category).Push) outcome = "suppressed_preference";
                    else if (memberHours.PushPaused) outcome = "suppressed_paused";
                    else if (!NotificationHoursRule.IsWithin(memberHours, now)) outcome = "suppressed_hours";
                    else if (!tokens.ContainsKey(recipient)) outcome = "no_token";
                    else
                    {
                        if (!pushedThisHour.TryGetValue(recipient, out long count))
                        {
                            count = await _Database.Notifications.CountPushedSinceAsync(recipient, hourAgo, token).ConfigureAwait(false);
                            pushedThisHour[recipient] = count;
                        }
                        if (count >= _Settings.Push.MaxPerUserPerHour) outcome = "suppressed_rate";
                        else if (notification.Type == NotificationTypeEnum.Reaction && notification.GroupKey != null)
                        {
                            string groupMarker = recipient + "|" + notification.GroupKey;
                            if (groupsPushed.Contains(groupMarker) || await _Database.Notifications.GroupPushedSinceAsync(recipient, notification.GroupKey, hourAgo, token).ConfigureAwait(false))
                                outcome = "suppressed_rate";
                            else groupsPushed.Add(groupMarker);
                        }
                    }
                    if (outcome != null)
                    {
                        if (!suppressed.TryGetValue(outcome, out List<string>? ids)) suppressed[outcome] = ids = new List<string>();
                        ids.Add(notification.Id);
                        continue;
                    }
                    pushedThisHour[recipient] = pushedThisHour[recipient] + 1;
                    toSend.Add(notification);
                }

                foreach (KeyValuePair<string, List<string>> pair in suppressed)
                {
                    List<string> changed = await _Database.Notifications.TransitionPushStateAsync(pair.Value, PushStateEnum.Suppressed, null, token).ConfigureAwait(false);
                    TrellisKitTelemetry.Add(TrellisKitTelemetry.PushSends, changed.Count, "outcome", pair.Key);
                }
                int sent = await SendAsync(toSend, tokens, now, token).ConfigureAwait(false);
                return suppressed.Values.Sum(v => v.Count) + sent;
            }
        }

        private async Task<int> SendAsync(List<Notification> toSend, Dictionary<string, List<PushToken>> tokens, DateTime now, CancellationToken token)
        {
            if (toSend.Count == 0) return 0;
            HashSet<string> claimed = new HashSet<string>(await _Database.Notifications.TransitionPushStateAsync(toSend.Select(n => n.Id).ToList(), PushStateEnum.Sent, now, token).ConfigureAwait(false), StringComparer.Ordinal);
            List<Notification> mine = toSend.Where(n => claimed.Contains(n.Id)).ToList();
            if (mine.Count == 0) return 0;

            Dictionary<string, long> badges = new Dictionary<string, long>(StringComparer.Ordinal);
            List<PushMessage> messages = new List<PushMessage>();
            List<Notification> messageNotifications = new List<Notification>();
            List<PushToken> messageTokens = new List<PushToken>();
            foreach (Notification notification in mine)
            {
                if (!badges.TryGetValue(notification.RecipientUserId, out long badge))
                {
                    badge = await _Database.Notifications.CountUnreadAsync(notification.RecipientUserId, token).ConfigureAwait(false);
                    badges[notification.RecipientUserId] = badge;
                }
                foreach (PushToken pushToken in tokens[notification.RecipientUserId])
                {
                    messages.Add(new PushMessage
                    {
                        To = pushToken.Token,
                        Title = notification.Title,
                        Body = notification.Body,
                        Data = new Dictionary<string, string> { { "notificationId", notification.Id }, { "type", notification.Type.ToString() }, { "path", notification.Path } },
                        Sound = "default",
                        Badge = (int)Math.Min(badge, Int32.MaxValue),
                        ChannelId = "default",
                        Priority = "high"
                    });
                    messageNotifications.Add(notification);
                    messageTokens.Add(pushToken);
                }
            }

            List<PushTicket> tickets;
            try
            {
                tickets = await _Sender.SendAsync(messages, token).ConfigureAwait(false);
            }
            catch (HttpRequestException e)
            {
                Logger.Warn("[PushDeliveryWorker] push send failed for " + mine.Count + " notifications: " + e.Message);
                foreach (Notification notification in mine) await _Database.Notifications.SetPushStateAsync(notification.Id, PushStateEnum.Failed, token).ConfigureAwait(false);
                TrellisKitTelemetry.Add(TrellisKitTelemetry.PushSends, mine.Count, "outcome", "error");
                return mine.Count;
            }

            List<PushDelivery> deliveries = new List<PushDelivery>();
            HashSet<string> delivered = new HashSet<string>(StringComparer.Ordinal);
            List<string> prune = new List<string>();
            List<string> failed = new List<string>();
            for (int i = 0; i < messages.Count; i++)
            {
                PushTicket ticket = i < tickets.Count ? tickets[i] : new PushTicket { Status = "error", Message = "No ticket returned." };
                bool ok = ticket.IsOk && !String.IsNullOrEmpty(ticket.Id);
                deliveries.Add(new PushDelivery
                {
                    Id = IdGenerator.Generate(Constants.PushDeliveryIdPrefix),
                    NotificationId = messageNotifications[i].Id,
                    PushTokenId = messageTokens[i].Id,
                    TicketId = ok ? ticket.Id : null,
                    Status = ok ? PushDeliveryStatusEnum.Sent : PushDeliveryStatusEnum.Error,
                    Error = ok ? null : (ticket.Error ?? ticket.Message ?? "Unknown"),
                    CreatedUtc = now,
                    ReceiptCheckedUtc = ok ? null : now
                });
                if (ok) delivered.Add(messageNotifications[i].Id);
                else if (String.Equals(ticket.Error, _DeviceNotRegistered, StringComparison.Ordinal)) prune.Add(messageTokens[i].Id);
                else failed.Add(messageTokens[i].Id);
            }
            await _Database.PushTokens.CreateDeliveriesAsync(deliveries, token).ConfigureAwait(false);
            foreach (string id in failed.Distinct(StringComparer.Ordinal)) await _Database.PushTokens.RecordFailureAsync(id, _DisableAfterFailures, now, token).ConfigureAwait(false);
            foreach (string id in prune.Distinct(StringComparer.Ordinal))
            {
                if (await _Database.PushTokens.DeleteAsync(id, token).ConfigureAwait(false)) TrellisKitTelemetry.Add(TrellisKitTelemetry.PushTokens, 1, "action", "pruned");
            }
            int errors = 0;
            foreach (Notification notification in mine.Where(n => !delivered.Contains(n.Id)))
            {
                await _Database.Notifications.SetPushStateAsync(notification.Id, PushStateEnum.Failed, token).ConfigureAwait(false);
                errors++;
            }
            TrellisKitTelemetry.Add(TrellisKitTelemetry.PushSends, mine.Count - errors, "outcome", "sent");
            if (errors > 0) TrellisKitTelemetry.Add(TrellisKitTelemetry.PushSends, errors, "outcome", "error");
            return mine.Count;
        }

        private async Task<int> ReceiptPassAsync(DateTime now, CancellationToken token)
        {
            List<PushDelivery> due = await _Database.PushTokens.EnumerateDueReceiptsAsync(now.AddMinutes(-_Settings.Push.ReceiptDelayMinutes), _ReceiptBatchSize, token).ConfigureAwait(false);
            if (due.Count == 0) return 0;
            using (Activity? activity = TrellisKitTelemetry.StartSpan("push.receipts"))
            {
                activity?.SetTag("job.batch_size", due.Count);
                Dictionary<string, PushReceipt> receipts;
                try
                {
                    receipts = await _Sender.GetReceiptsAsync(due.Select(d => d.TicketId!).ToList(), token).ConfigureAwait(false);
                }
                catch (HttpRequestException e)
                {
                    Logger.Warn("[PushDeliveryWorker] receipt check failed: " + e.Message);
                    return 0;
                }
                int checkedCount = 0;
                foreach (PushDelivery delivery in due)
                {
                    token.ThrowIfCancellationRequested();
                    if (!receipts.TryGetValue(delivery.TicketId!, out PushReceipt? receipt))
                    {
                        if (delivery.CreatedUtc >= now.AddHours(-_ReceiptGiveUpHours)) continue;
                        delivery.Status = PushDeliveryStatusEnum.Error;
                        delivery.Error = "NoReceipt";
                        delivery.ReceiptCheckedUtc = now;
                        await _Database.PushTokens.UpdateDeliveryAsync(delivery, token).ConfigureAwait(false);
                        TrellisKitTelemetry.Add(TrellisKitTelemetry.PushReceipts, 1, "status", "other");
                        checkedCount++;
                        continue;
                    }
                    delivery.ReceiptCheckedUtc = now;
                    checkedCount++;
                    if (receipt.IsOk)
                    {
                        delivery.Status = PushDeliveryStatusEnum.Ok;
                        delivery.Error = null;
                        await _Database.PushTokens.UpdateDeliveryAsync(delivery, token).ConfigureAwait(false);
                        await _Database.PushTokens.RecordSuccessAsync(delivery.PushTokenId, now, token).ConfigureAwait(false);
                        TrellisKitTelemetry.Add(TrellisKitTelemetry.PushReceipts, 1, "status", "ok");
                        continue;
                    }
                    delivery.Status = PushDeliveryStatusEnum.Error;
                    delivery.Error = receipt.Error ?? receipt.Message ?? "Unknown";
                    await _Database.PushTokens.UpdateDeliveryAsync(delivery, token).ConfigureAwait(false);
                    TrellisKitTelemetry.Add(TrellisKitTelemetry.PushReceipts, 1, "status", StatusLabel(receipt.Error));
                    if (String.Equals(receipt.Error, _DeviceNotRegistered, StringComparison.Ordinal))
                    {
                        if (await _Database.PushTokens.DeleteAsync(delivery.PushTokenId, token).ConfigureAwait(false)) TrellisKitTelemetry.Add(TrellisKitTelemetry.PushTokens, 1, "action", "pruned");
                    }
                    else
                    {
                        await _Database.PushTokens.RecordFailureAsync(delivery.PushTokenId, _DisableAfterFailures, now, token).ConfigureAwait(false);
                    }
                }
                return checkedCount;
            }
        }

        private static string StatusLabel(string? error)
        {
            switch (error)
            {
                case _DeviceNotRegistered:
                    return "device_not_registered";
                case "MessageTooBig":
                    return "message_too_big";
                case "MessageRateExceeded":
                    return "message_rate_exceeded";
                default:
                    return "other";
            }
        }

        #endregion
    }
}
