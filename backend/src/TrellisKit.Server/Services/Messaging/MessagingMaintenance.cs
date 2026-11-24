namespace TrellisKit.Server.Services.Messaging
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
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services.Email;
    using TrellisKit.Server.Services.Email.Templates;

    /// <summary>
    /// The passes of the messaging workers (docs/PHASE5_CONTRACT.md sections 3.6, 4.9, 12 and as-built note B), run by
    /// leased workers in the property runtime and callable directly with the module clock (tests set
    /// <see cref="MessagingModule.Clock"/>): MessageEmailWorker (messages_unread), MessageRetentionWorker
    /// (messaging.retentionDays and report snapshot pruning), ConversationPurgeWorker (conversations every member
    /// deleted), and MessagingLapseWorker (lapsed members in gated models).
    /// Thread safety: safe to call concurrently; each pass is meant to run under a job lease.
    /// </summary>
    public class MessagingMaintenance
    {
        #region Public-Members

        /// <summary>
        /// Members examined per email pass. Default 500, minimum 1, maximum 10000.
        /// </summary>
        public int EmailBatchSize
        {
            get { return _EmailBatchSize; }
            set { _EmailBatchSize = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// Messages processed per retention pass and conversations per purge pass. Default 200, minimum 1, maximum 1000.
        /// </summary>
        public int BatchSize
        {
            get { return _BatchSize; }
            set { _BatchSize = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Members examined per lapse pass. Default 1000, minimum 1, maximum 10000.
        /// </summary>
        public int LapseScanSize
        {
            get { return _LapseScanSize; }
            set { _LapseScanSize = Math.Clamp(value, 1, 10000); }
        }

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private readonly MessagingModule _Module;
        private int _EmailBatchSize = 500;
        private int _BatchSize = 200;
        private int _LapseScanSize = 1000;
        private string? _LapseCursor = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the passes.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <param name="module">Messaging module.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public MessagingMaintenance(ApplicationServices services, MessagingModule module)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            _Module = module ?? throw new ArgumentNullException(nameof(module));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// MessageEmailWorker pass: queue messages_unread for members with the Messages email on whose messages stayed
        /// unread longer than messaging.emailAfterMinutes, at most one per messaging.emailMinIntervalHours, about messages
        /// newer than the previous email. Sender names and counts only.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Emails queued.</returns>
        public async Task<int> EmailPassAsync(CancellationToken token = default)
        {
            if (!_Services.Property.Property.Features.Messaging) return 0;
            DateTime now = _Module.Clock();
            DateTime unreadBefore = now.AddMinutes(-_Services.Settings.Messaging.EmailAfterMinutes);
            DateTime sentBefore = now.AddHours(-_Services.Settings.Messaging.EmailMinIntervalHours);
            int queued = 0;
            string? after = null;
            int examined = 0;
            while (examined < _EmailBatchSize)
            {
                List<MessageEmailCandidate> candidates = await _Services.Database.Conversations.ListEmailCandidatesAsync(unreadBefore, sentBefore, after, Math.Min(200, _EmailBatchSize - examined), token).ConfigureAwait(false);
                if (candidates.Count == 0) break;
                List<string> ids = candidates.Select(c => c.UserId).ToList();
                List<NotificationPreference> preferences = await _Services.Database.NotificationPreferences.EnumerateAsync(ids, token).ConfigureAwait(false);
                HashSet<string> access = await _Module.Policy.WithAccessAsync(ids, token).ConfigureAwait(false);
                Dictionary<string, User> users = (await _Services.Database.Users.ReadManyAsync(ids, token).ConfigureAwait(false)).ToDictionary(u => u.Id, StringComparer.Ordinal);
                foreach (MessageEmailCandidate candidate in candidates)
                {
                    token.ThrowIfCancellationRequested();
                    examined++;
                    if (!access.Contains(candidate.UserId) || !users.TryGetValue(candidate.UserId, out User? user) || String.IsNullOrWhiteSpace(user.Email)) continue;
                    if (!NotificationPreferenceService.Effective(candidate.UserId, preferences, NotificationCategoryEnum.Messages).Email) continue;
                    if (await SendEmailAsync(user, candidate, unreadBefore, now, token).ConfigureAwait(false)) queued++;
                }
                after = candidates[candidates.Count - 1].UserId;
            }
            return queued;
        }

        /// <summary>
        /// MessageRetentionWorker pass: when messaging.retentionDays is above 0, messages older than that become
        /// tombstones (content and media purged); report snapshots of resolved reports are pruned after
        /// moderation.retainRemovedDays.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Messages and snapshots purged.</returns>
        public async Task<int> RetentionPassAsync(CancellationToken token = default)
        {
            DateTime now = _Module.Clock();
            int processed = await _Services.Database.Messages.PruneSnapshotsAsync(_Services.Settings.Moderation.RetainRemovedDays, now, _BatchSize, token).ConfigureAwait(false);
            int days = _Services.Settings.Messaging.RetentionDays;
            if (days <= 0) return processed;
            List<Message> due = await _Services.Database.Messages.ListRetentionDueAsync(now.AddDays(-days), _BatchSize, token).ConfigureAwait(false);
            foreach (Message message in due.Where(m => m.AttachmentCount > 0)) await _Services.Media.PurgeTargetAsync(TargetTypeEnum.Message, message.Id, token).ConfigureAwait(false);
            processed += await _Services.Database.Messages.PurgeContentAsync(due.Select(m => m.Id), now, token).ConfigureAwait(false);
            return processed;
        }

        /// <summary>
        /// ConversationPurgeWorker pass: conversations every member deleted (or left by account deletion) are purged
        /// <see cref="MessagingModule.PurgeAfterDays"/> later with their messages, tombstones, and media.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Conversations purged.</returns>
        public async Task<int> PurgePassAsync(CancellationToken token = default)
        {
            DateTime now = _Module.Clock();
            int purged = 0;
            foreach (string conversationId in await _Services.Database.Conversations.ListPurgeDueAsync(now, _BatchSize, token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                string? after = null;
                while (true)
                {
                    List<string> withMedia = await _Services.Database.Messages.WithAttachmentsAsync(conversationId, null, after, 200, token).ConfigureAwait(false);
                    if (withMedia.Count == 0) break;
                    foreach (string messageId in withMedia) await _Services.Media.PurgeTargetAsync(TargetTypeEnum.Message, messageId, token).ConfigureAwait(false);
                    after = withMedia[withMedia.Count - 1];
                }
                if (await _Services.Database.Conversations.PurgeConversationAsync(conversationId, now, token).ConfigureAwait(false))
                {
                    purged++;
                    MessagingTelemetry.Conversation("purge", "ok");
                }
            }
            return purged;
        }

        /// <summary>
        /// MessagingLapseWorker pass (gated models only): record when members of conversations lost messaging access and
        /// clear the record when access returns; after messaging.lapsedRetentionDays without rejoining, remove their
        /// conversations for them (as delete for me) and clear their contacts. The other member's history is untouched.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Members whose lapse state changed or whose conversations were removed.</returns>
        public async Task<int> LapsePassAsync(CancellationToken token = default)
        {
            if (!_Module.Policy.MembershipRequired()) return 0;
            DateTime now = _Module.Clock();
            int changed = 0;
            List<string> members = await _Services.Database.Conversations.MemberUserIdsAsync(_LapseCursor, _LapseScanSize, token).ConfigureAwait(false);
            _LapseCursor = members.Count < _LapseScanSize ? null : members[members.Count - 1];
            if (members.Count > 0)
            {
                HashSet<string> access = await _Module.Policy.WithAccessAsync(members, token).ConfigureAwait(false);
                foreach (string userId in members)
                {
                    token.ThrowIfCancellationRequested();
                    if (access.Contains(userId))
                    {
                        if (await _Services.Database.Conversations.ClearLapseAsync(userId, token).ConfigureAwait(false)) changed++;
                    }
                    else if (await _Services.Database.Conversations.RecordLapseAsync(userId, now, token).ConfigureAwait(false)) changed++;
                }
            }
            DateTime cutoff = now.AddDays(-_Services.Settings.Messaging.LapsedRetentionDays);
            List<MessagingLapse> due = await _Services.Database.Conversations.ListLapsesDueAsync(cutoff, _BatchSize, token).ConfigureAwait(false);
            if (due.Count == 0) return changed;
            HashSet<string> returned = await _Module.Policy.WithAccessAsync(due.Select(l => l.UserId), token).ConfigureAwait(false);
            foreach (MessagingLapse lapse in due)
            {
                token.ThrowIfCancellationRequested();
                if (returned.Contains(lapse.UserId))
                {
                    await _Services.Database.Conversations.ClearLapseAsync(lapse.UserId, token).ConfigureAwait(false);
                    changed++;
                    continue;
                }
                await _Services.Database.Conversations.PurgeForLapsedAsync(lapse.UserId, now, now.AddDays(_Module.PurgeAfterDays), token).ConfigureAwait(false);
                changed++;
            }
            return changed;
        }

        /// <summary>
        /// The sender names of the unread email: at most three names, then "and N others".
        /// </summary>
        /// <param name="names">Display names, most recent first.</param>
        /// <param name="total">Distinct senders.</param>
        /// <returns>Text.</returns>
        public static string SenderNames(IReadOnlyList<string> names, long total)
        {
            ArgumentNullException.ThrowIfNull(names);
            List<string> shown = names.Take(3).ToList();
            if (shown.Count == 0) return "members";
            long others = total - shown.Count;
            if (others > 0) return String.Join(", ", shown) + " and " + others.ToString(CultureInfo.InvariantCulture) + (others == 1 ? " other" : " others");
            if (shown.Count == 1) return shown[0];
            return String.Join(", ", shown.Take(shown.Count - 1)) + " and " + shown[shown.Count - 1];
        }

        #endregion

        #region Private-Methods

        private async Task<bool> SendEmailAsync(User user, MessageEmailCandidate candidate, DateTime unreadBefore, DateTime now, CancellationToken token)
        {
            List<string> senders = await _Services.Database.Conversations.UnreadSendersAsync(user.Id, unreadBefore, 3, token).ConfigureAwait(false);
            long total = await _Services.Database.Conversations.CountUnreadSendersAsync(user.Id, unreadBefore, token).ConfigureAwait(false);
            Dictionary<string, UserSummary> summaries = await _Services.Summaries.BuildAsync(senders.Select(s => (string?)s), token).ConfigureAwait(false);
            List<string> names = senders.Select(s => summaries.TryGetValue(s, out UserSummary? summary) ? summary.DisplayName : "A member").ToList();
            EmailRequest request = new EmailRequest
            {
                Template = MessagesUnreadEmail.TemplateKey,
                To = user.Email!,
                UserId = user.Id,
                IdempotencyKey = "messages_unread:" + user.Id + ":" + now.Ticks.ToString(CultureInfo.InvariantCulture)
            };
            request.Tokens["memberName"] = user.DisplayName;
            request.Tokens["count"] = candidate.Messages.ToString(CultureInfo.InvariantCulture);
            request.Tokens["senderNames"] = SenderNames(names, total);
            request.Tokens["link"] = _Services.Email.Link("/app/messages");
            string unsubscribe;
            try
            {
                unsubscribe = _Services.FeedPostEmails.UnsubscribeUrl(_Services.FeedPostEmails.CreateUnsubscribeToken(user.Id, NotificationCategoryEnum.Messages));
                request.Headers["List-Unsubscribe"] = "<" + unsubscribe + ">";
                request.Headers["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click";
            }
            catch (IntegrationNotConfiguredException)
            {
                unsubscribe = _Services.Email.Link("/app/settings/notifications");
            }
            request.Tokens["unsubscribeLink"] = unsubscribe;
            EmailMessageRecord? queued = await _Services.Email.QueueAsync(request, token).ConfigureAwait(false);
            await _Services.Database.Conversations.MarkEmailSentAsync(user.Id, now, token).ConfigureAwait(false);
            if (queued != null) TrellisKitTelemetry.Increment(TrellisKitTelemetry.Emails, "category", MessagesUnreadEmail.TemplateKey, "outcome", "queued");
            return queued != null;
        }

        #endregion
    }
}
