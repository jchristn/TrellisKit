namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Creates and serves notifications (Phase 2 contract 8.1 to 8.4). Every content slice creates notifications
    /// through this service: <see cref="CreateAsync"/> for one, <see cref="CreateForSourceAsync"/> for the candidates of
    /// one source item (one per recipient, highest priority wins), <see cref="CreateManyAsync"/> and
    /// <see cref="CreateForRecipientsAsync"/> for many (fan-out, moderators), and <see cref="StartFanoutAsync"/> to queue a
    /// background fan-out. Rules applied on every path: the actor is never notified about their own action; nothing is
    /// created across a block in either direction (except Moderation notices); a category whose in-app preference is off
    /// gets nothing (Moderation and Account cannot be turned off); Reaction notifications aggregate per target while
    /// unread; deleted recipients get nothing; notifications about a feed post or its comments (other than moderation
    /// notices) go only to recipients who may open the post under the property's access model (its author, or
    /// <see cref="AccessDecider.RecipientMayOpen"/>: SystemAdmin, or age attested plus the post's audience, with a membership
    /// always in FullyGated and for Members posts in PartiallyGated). Every created notification is queued for push (pushstate Pending)
    /// unless the draft says otherwise; the push worker decides delivery.
    /// Thread safety: safe to call concurrently; aggregation is race safe in the database.
    /// </summary>
    public class NotificationService
    {
        #region Public-Members

        /// <summary>Opt-in product metrics, supplied by the composition root.</summary>
        public Metrics.ProductMetricEventService? ProductEvents { get; set; } = null;

        /// <summary>
        /// Clock used for timestamps. Default DateTime.UtcNow. Tests set a fixed clock. Never null.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public Func<DateTime> Clock
        {
            get { return _Clock; }
            set { _Clock = value ?? throw new ArgumentNullException(nameof(Clock)); }
        }

        /// <summary>
        /// Called after a fan-out job is queued so the fan-out worker starts without waiting for its interval, or null.
        /// The composition root wires it to <c>NotificationFanoutWorker.Signal</c>.
        /// </summary>
        public Action? FanoutSignal { get; set; } = null;

        /// <summary>
        /// Called after notifications are stored (created or aggregated) with the stored rows; the realtime module
        /// publishes notification.created for small batches (bulk fan-outs are not published per recipient). Never
        /// throws into the caller and never changes what is stored.
        /// </summary>
        public Action<IReadOnlyList<Notification>>? Created { get; set; } = null;

        /// <summary>
        /// Supplies the property's current access configuration (who may open a post). Default FullyGated.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public Func<AccessPolicy> Access
        {
            get { return _Access; }
            set { _Access = value ?? throw new ArgumentNullException(nameof(Access)); }
        }

        /// <summary>
        /// Window within which an aggregated Reaction update is not queued for push again. Default one hour.
        /// </summary>
        public static readonly TimeSpan ReactionPushWindow = TimeSpan.FromHours(1);

        /// <summary>
        /// Processes fan-out job types other than NewFeedPost (set by the forum module: NewThreadInForum, PollClosed,
        /// ThreadReply). Arguments: job, claim holder, claim extension. Returns notifications created, or -1 when the
        /// claim was lost.
        /// </summary>
        public Func<NotificationFanout, string, TimeSpan, CancellationToken, Task<int>>? ExtraFanout { get; set; } = null;

        /// <summary>
        /// Fan-out processors by job type, consulted before <see cref="ExtraFanout"/> (the Phase 4 modules register
        /// NewEvent, EventReminder, EventUpdated, EventCanceled, and NewLessonInSeries). Same arguments and result as
        /// <see cref="ExtraFanout"/>. Register during module construction or Attach; not thread safe for writes.
        /// </summary>
        public Dictionary<NotificationTypeEnum, Func<NotificationFanout, string, TimeSpan, CancellationToken, Task<int>>> FanoutHandlers { get; } =
            new Dictionary<NotificationTypeEnum, Func<NotificationFanout, string, TimeSpan, CancellationToken, Task<int>>>();

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly AppSettings _Settings;
        private readonly UserSummaryService _Summaries;
        private readonly EnumerationService _Enumeration;
        private readonly MetadataService _Metadata;
        private readonly AppLogger _Logger;
        private Func<AccessPolicy> _Access = () => AccessPolicy.FullyGatedDefault;
        private Func<DateTime> _Clock = () => DateTime.UtcNow;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Running settings (read live).</param>
        /// <param name="summaries">User summary builder.</param>
        /// <param name="enumeration">Enumeration preparation.</param>
        /// <param name="metadata">Labels and tags.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public NotificationService(DatabaseDriverBase database, AppSettings settings, UserSummaryService summaries, EnumerationService enumeration, MetadataService metadata, AppLogger logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Summaries = summaries ?? throw new ArgumentNullException(nameof(summaries));
            _Enumeration = enumeration ?? throw new ArgumentNullException(nameof(enumeration));
            _Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create one notification from a draft, applying every rule. Reaction drafts aggregate into the recipient's
        /// unread notification for the same target (group key Reaction:&lt;targetType&gt;:&lt;targetId&gt;).
        /// </summary>
        /// <param name="draft">Draft.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created or aggregated notification, or null when a rule suppressed it.</returns>
        /// <exception cref="ArgumentNullException">The draft, its recipient, or its target is missing.</exception>
        public async Task<Notification?> CreateAsync(NotificationDraft draft, CancellationToken token = default)
        {
            Validate(draft);
            using (Activity? activity = TrellisKitTelemetry.StartSpan("notifications.create"))
            {
                activity?.SetTag("notification.type", draft.Type.ToString());
                NotificationCategoryEnum category = NotificationCatalog.CategoryOf(draft.Type);
                if (IsSelf(draft)) return null;
                User? recipient = await _Database.Users.ReadAsync(draft.RecipientUserId, token).ConfigureAwait(false);
                if (recipient == null || recipient.IsDeleted) return null;
                if (!String.IsNullOrEmpty(draft.ActorUserId) && category != NotificationCategoryEnum.Moderation && category != NotificationCategoryEnum.Support)
                {
                    HashSet<string> blocked = await _Database.Notifications.BlockedEitherWayAsync(draft.ActorUserId, new[] { draft.RecipientUserId }, token).ConfigureAwait(false);
                    if (blocked.Count > 0) return null;
                }
                List<NotificationPreference> preferences = await _Database.NotificationPreferences.EnumerateAsync(new[] { draft.RecipientUserId }, token).ConfigureAwait(false);
                if (!InAppOn(preferences, category)) return null;
                if ((await AudienceDeniedAsync(new List<NotificationDraft> { draft }, new List<User> { recipient }, token).ConfigureAwait(false)).Count > 0) return null;

                string? actorName = await ActorNameAsync(draft.ActorUserId, token).ConfigureAwait(false);
                DateTime now = _Clock();
                Notification row = ToRow(draft, category, actorName, now);
                if (!String.IsNullOrEmpty(draft.GroupKey) && NotificationCatalog.IsReplacingGroup(draft.Type))
                {
                    // Phase 5 ticket notifications: one row per ticket while unread, showing the latest event (no actor needed).
                    row.GroupKey = draft.GroupKey;
                    Notification? replaced = await _Database.Notifications.UpsertGroupedAsync(row, draft.QueuePush, ReactionPushWindow, token).ConfigureAwait(false);
                    if (replaced != null)
                        TrellisKitTelemetry.Add(TrellisKitTelemetry.NotificationsCreated, 1, "type", draft.Type.ToString(), "aggregated", String.Equals(replaced.Id, row.Id, StringComparison.Ordinal) ? "false" : "true");
                    return replaced;
                }
                if (!String.IsNullOrEmpty(draft.GroupKey) && !String.IsNullOrEmpty(draft.ActorUserId) && draft.Type != NotificationTypeEnum.Reaction)
                {
                    // Phase 3 aggregated types (ThreadReply per thread, NewThreadInForum per forum) group while unread
                    // and push at most once per group per hour, like reactions.
                    row.GroupKey = draft.GroupKey;
                    string who = actorName ?? "Someone";
                    if (draft.Type == NotificationTypeEnum.MessageRequest)
                    {
                        // Message requests: one row while unread, never pushed (docs/PHASE5_CONTRACT.md section 9.1).
                        Notification? requests = await _Database.Notifications.AggregateAsync(row, who + " sent you a message request", who + " and 1 other sent you message requests", who + " and ",
                            " others sent you message requests", false, ReactionPushWindow, token).ConfigureAwait(false);
                        if (requests != null) TrellisKitTelemetry.Add(TrellisKitTelemetry.NotificationsCreated, 1, "type", draft.Type.ToString(), "aggregated", String.Equals(requests.Id, row.Id, StringComparison.Ordinal) ? "false" : "true");
                        return requests;
                    }
                    string verb = draft.Type == NotificationTypeEnum.NewThreadInForum ? " posted in " : " commented on ";
                    string subject = String.IsNullOrWhiteSpace(draft.TitleText) ? Noun(draft) : draft.TitleText.Trim();
                    Notification? grouped = await _Database.Notifications.AggregateAsync(row, who + verb + subject, who + " and 1 other" + verb + subject, who + " and ", " others" + verb + subject,
                        draft.QueuePush, ReactionPushWindow, token).ConfigureAwait(false);
                    if (grouped != null)
                    {
                        bool isNew = String.Equals(grouped.Id, row.Id, StringComparison.Ordinal);
                        TrellisKitTelemetry.Add(TrellisKitTelemetry.NotificationsCreated, 1, "type", draft.Type.ToString(), "aggregated", isNew ? "false" : "true");
                        RaiseCreated(new List<Notification> { grouped });
                    }
                    return grouped;
                }
                if (draft.Type == NotificationTypeEnum.Reaction && !String.IsNullOrEmpty(draft.ActorUserId))
                {
                    row.GroupKey = GroupKey(draft.TargetType.ToString(), draft.TargetId);
                    string name = actorName ?? "Someone";
                    string suffix = " reacted to your " + Noun(draft);
                    Notification? aggregated = await _Database.Notifications.AggregateAsync(row, name + suffix, name + " and 1 other" + suffix, name + " and ", " others" + suffix,
                        draft.QueuePush, ReactionPushWindow, token).ConfigureAwait(false);
                    if (aggregated != null)
                    {
                        bool isNew = String.Equals(aggregated.Id, row.Id, StringComparison.Ordinal);
                        TrellisKitTelemetry.Add(TrellisKitTelemetry.NotificationsCreated, 1, "type", draft.Type.ToString(), "aggregated", isNew ? "false" : "true");
                        RaiseCreated(new List<Notification> { aggregated });
                    }
                    return aggregated;
                }
                await _Database.Notifications.CreateAsync(row, token).ConfigureAwait(false);
                TrellisKitTelemetry.Add(TrellisKitTelemetry.NotificationsCreated, 1, "type", draft.Type.ToString(), "aggregated", "false");
                RaiseCreated(new List<Notification> { row });
                return row;
            }
        }

        /// <summary>
        /// Create the notifications of one source item (for example a new comment): keeps one draft per recipient by
        /// priority (CommentReply, then PostComment, then Mention; see <see cref="SelectHighestPriority"/>), then creates
        /// each with <see cref="CreateAsync"/>.
        /// </summary>
        /// <param name="candidates">Candidate drafts for the source item.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The notifications created (suppressed ones are absent).</returns>
        /// <exception cref="ArgumentNullException">The candidates are null.</exception>
        public async Task<List<Notification>> CreateForSourceAsync(IEnumerable<NotificationDraft> candidates, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(candidates);
            List<Notification> created = new List<Notification>();
            foreach (NotificationDraft draft in SelectHighestPriority(candidates))
            {
                token.ThrowIfCancellationRequested();
                Notification? notification = await CreateAsync(draft, token).ConfigureAwait(false);
                if (notification != null) created.Add(notification);
            }
            return created;
        }

        /// <summary>
        /// Keep one draft per recipient: the highest <see cref="NotificationCatalog.Priority"/> (CommentReply 3,
        /// PostComment 2, Mention 1, others 0); ties keep the first. Drafts without a recipient are dropped. Order of the
        /// result follows the first appearance of each recipient.
        /// </summary>
        /// <param name="candidates">Candidates.</param>
        /// <returns>At most one draft per recipient.</returns>
        /// <exception cref="ArgumentNullException">The candidates are null.</exception>
        public static List<NotificationDraft> SelectHighestPriority(IEnumerable<NotificationDraft> candidates)
        {
            ArgumentNullException.ThrowIfNull(candidates);
            Dictionary<string, NotificationDraft> best = new Dictionary<string, NotificationDraft>(StringComparer.Ordinal);
            List<string> order = new List<string>();
            foreach (NotificationDraft draft in candidates)
            {
                if (draft == null || String.IsNullOrEmpty(draft.RecipientUserId)) continue;
                if (!best.TryGetValue(draft.RecipientUserId, out NotificationDraft? current))
                {
                    best[draft.RecipientUserId] = draft;
                    order.Add(draft.RecipientUserId);
                }
                else if (NotificationCatalog.Priority(draft.Type) > NotificationCatalog.Priority(current.Type))
                {
                    best[draft.RecipientUserId] = draft;
                }
            }
            return order.Select(r => best[r]).ToList();
        }

        /// <summary>
        /// Create many non-aggregated notifications efficiently (bulk reads of recipients, preferences, and blocks, then
        /// one insert transaction). Applies the same rules as <see cref="CreateAsync"/>. Reaction drafts are routed through
        /// <see cref="CreateAsync"/> one by one. Used by the fan-out worker and for "every moderator in scope" notices
        /// (ModerationOverdue).
        /// </summary>
        /// <param name="drafts">Drafts.</param>
        /// <param name="skipExisting">Skip recipients that already have a notification of the same type for the same target (makes a retried batch idempotent).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Notifications created.</returns>
        /// <exception cref="ArgumentNullException">The drafts are null.</exception>
        public async Task<int> CreateManyAsync(IEnumerable<NotificationDraft> drafts, bool skipExisting = false, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(drafts);
            List<NotificationDraft> all = drafts.Where(d => d != null).ToList();
            foreach (NotificationDraft draft in all) Validate(draft);
            int created = 0;
            foreach (NotificationDraft reaction in all.Where(d => d.Type == NotificationTypeEnum.Reaction || !String.IsNullOrEmpty(d.GroupKey)))
            {
                if (skipExisting && String.IsNullOrEmpty(reaction.GroupKey) == false
                    && (await _Database.Notifications.ExistingRecipientsAsync(reaction.Type, reaction.TargetId, new[] { reaction.RecipientUserId }, token).ConfigureAwait(false)).Count > 0) continue;
                if (await CreateAsync(reaction, token).ConfigureAwait(false) != null) created++;
            }
            List<NotificationDraft> plain = all.Where(d => d.Type != NotificationTypeEnum.Reaction && String.IsNullOrEmpty(d.GroupKey) && !IsSelf(d)).ToList();
            if (plain.Count == 0) return created;

            List<string> recipientIds = plain.Select(d => d.RecipientUserId).Distinct(StringComparer.Ordinal).ToList();
            List<User> users = await _Database.Users.ReadManyAsync(recipientIds, token).ConfigureAwait(false);
            HashSet<string> alive = new HashSet<string>(users.Where(u => !u.IsDeleted).Select(u => u.Id), StringComparer.Ordinal);
            List<NotificationPreference> preferences = await _Database.NotificationPreferences.EnumerateAsync(recipientIds, token).ConfigureAwait(false);
            Dictionary<string, List<NotificationPreference>> preferencesByUser = preferences.GroupBy(p => p.UserId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            HashSet<string> denied = await AudienceDeniedAsync(plain, users, token).ConfigureAwait(false);

            Dictionary<string, HashSet<string>> blockedByActor = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (IGrouping<string, NotificationDraft> group in plain.Where(d => !String.IsNullOrEmpty(d.ActorUserId)).GroupBy(d => d.ActorUserId!, StringComparer.Ordinal))
            {
                blockedByActor[group.Key] = await _Database.Notifications.BlockedEitherWayAsync(group.Key, group.Select(d => d.RecipientUserId), token).ConfigureAwait(false);
            }
            Dictionary<string, HashSet<string>> existing = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            if (skipExisting)
            {
                foreach (IGrouping<string, NotificationDraft> group in plain.GroupBy(d => d.Type + "|" + d.TargetId, StringComparer.Ordinal))
                {
                    NotificationDraft first = group.First();
                    existing[group.Key] = await _Database.Notifications.ExistingRecipientsAsync(first.Type, first.TargetId, group.Select(d => d.RecipientUserId), token).ConfigureAwait(false);
                }
            }

            Dictionary<string, string?> actorNames = new Dictionary<string, string?>(StringComparer.Ordinal);
            List<string> actorIds = plain.Where(d => !String.IsNullOrEmpty(d.ActorUserId)).Select(d => d.ActorUserId!).Distinct(StringComparer.Ordinal).ToList();
            if (actorIds.Count > 0)
            {
                Dictionary<string, UserSummary> summaries = await _Summaries.BuildAsync(actorIds, token).ConfigureAwait(false);
                foreach (KeyValuePair<string, UserSummary> pair in summaries) actorNames[pair.Key] = pair.Value.DisplayName;
            }

            DateTime now = _Clock();
            List<Notification> rows = new List<Notification>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (NotificationDraft draft in plain)
            {
                NotificationCategoryEnum category = NotificationCatalog.CategoryOf(draft.Type);
                if (!alive.Contains(draft.RecipientUserId)) continue;
                if (denied.Contains(AudienceKey(draft))) continue;
                if (!seen.Add(draft.Type + "|" + draft.TargetId + "|" + draft.RecipientUserId)) continue;
                if (!String.IsNullOrEmpty(draft.ActorUserId) && category != NotificationCategoryEnum.Moderation
                    && blockedByActor.TryGetValue(draft.ActorUserId, out HashSet<string>? blocked) && blocked.Contains(draft.RecipientUserId)) continue;
                if (!InAppOn(preferencesByUser.TryGetValue(draft.RecipientUserId, out List<NotificationPreference>? mine) ? mine : new List<NotificationPreference>(), category)) continue;
                if (skipExisting && existing.TryGetValue(draft.Type + "|" + draft.TargetId, out HashSet<string>? had) && had.Contains(draft.RecipientUserId)) continue;
                string? actorName = draft.ActorUserId != null && actorNames.TryGetValue(draft.ActorUserId, out string? name) ? name : null;
                rows.Add(ToRow(draft, category, actorName, now));
            }
            if (rows.Count == 0) return created;
            await _Database.Notifications.CreateManyAsync(rows, token).ConfigureAwait(false);
            foreach (IGrouping<NotificationTypeEnum, Notification> group in rows.GroupBy(r => r.Type))
            {
                TrellisKitTelemetry.Add(TrellisKitTelemetry.NotificationsCreated, group.Count(), "type", group.Key.ToString(), "aggregated", "false");
            }
            RaiseCreated(rows);
            return created + rows.Count;
        }

        /// <summary>
        /// Create the same notification for many recipients (for example ModerationOverdue for every moderator in
        /// scope: pass the moderator ids, which <see cref="ModeratorIdsAsync"/> lists for the global scope).
        /// </summary>
        /// <param name="template">Draft whose recipient is replaced per recipient.</param>
        /// <param name="recipientUserIds">Recipients.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Notifications created.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public Task<int> CreateForRecipientsAsync(NotificationDraft template, IEnumerable<string> recipientUserIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(template);
            ArgumentNullException.ThrowIfNull(recipientUserIds);
            List<NotificationDraft> drafts = recipientUserIds.Where(r => !String.IsNullOrEmpty(r)).Distinct(StringComparer.Ordinal).Select(r => template.For(r)).ToList();
            return CreateManyAsync(drafts, false, token);
        }

        /// <summary>
        /// Members holding SystemAdmin, Moderator, or FeedModerator (not deleted or banned). The moderation slice narrows
        /// this to the members in scope of a report.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Member identifiers.</returns>
        public Task<List<string>> ModeratorIdsAsync(CancellationToken token = default)
        {
            return _Database.Notifications.EnumerateModeratorIdsAsync(token);
        }

        /// <summary>
        /// Queue a background fan-out (for example NewFeedPost for a published post). Idempotent: a second call for the
        /// same type and source does nothing. Signals the fan-out worker.
        /// </summary>
        /// <param name="type">Type (NewFeedPost in Phase 2).</param>
        /// <param name="sourceId">Source item.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when this call queued the job.</returns>
        /// <exception cref="ArgumentNullException">The source is null or empty.</exception>
        public async Task<bool> StartFanoutAsync(NotificationTypeEnum type, string sourceId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(sourceId)) throw new ArgumentNullException(nameof(sourceId));
            bool created = await _Database.NotificationFanouts.CreateAsync(new NotificationFanout
            {
                Id = IdGenerator.Generate(Constants.FanoutJobIdPrefix),
                Type = type,
                SourceId = sourceId,
                CreatedUtc = _Clock()
            }, token).ConfigureAwait(false);
            if (created)
            {
                try
                {
                    FanoutSignal?.Invoke();
                }
                catch (InvalidOperationException e)
                {
                    _Logger.Warn("[NotificationService] fan-out signal failed: " + e.Message);
                }
            }
            return created;
        }

        /// <summary>
        /// Render the English title of a draft (Phase 2 contract 8.2). Reaction titles here are the single-actor form;
        /// aggregated titles ("Alex Chen and 4 others reacted to your comment") are rendered during aggregation.
        /// </summary>
        /// <param name="draft">Draft.</param>
        /// <param name="actorDisplayName">Actor display name, or null ("Someone").</param>
        /// <returns>Title.</returns>
        /// <exception cref="ArgumentNullException">The draft is null.</exception>
        public string RenderTitle(NotificationDraft draft, string? actorDisplayName)
        {
            ArgumentNullException.ThrowIfNull(draft);
            string actor = String.IsNullOrWhiteSpace(actorDisplayName) ? "Someone" : actorDisplayName.Trim();
            string noun = Noun(draft);
            switch (draft.Type)
            {
                case NotificationTypeEnum.PostComment:
                    return actor + " commented on your " + (!String.IsNullOrWhiteSpace(draft.SubjectNoun) ? draft.SubjectNoun.Trim() : (draft.TargetType == TargetTypeEnum.Comment && !String.IsNullOrEmpty(draft.ThreadId) ? "thread" : "post"));
                case NotificationTypeEnum.CommentReply:
                    return actor + " replied to your comment";
                case NotificationTypeEnum.Mention:
                    return actor + " mentioned you in " + Article(noun) + " " + noun;
                case NotificationTypeEnum.Reaction:
                    return actor + " reacted to your " + noun;
                case NotificationTypeEnum.NewFeedPost:
                    return String.IsNullOrWhiteSpace(draft.TitleText) ? actor + " posted" : actor + " posted: " + draft.TitleText.Trim();
                case NotificationTypeEnum.ModerationAction:
                    string reason = String.IsNullOrWhiteSpace(draft.ModerationReason) ? String.Empty : " (" + draft.ModerationReason.Trim() + ")";
                    if (draft.ModerationAction == ModerationActionTypeEnum.ForumMute) return "A moderator muted you in " + Subject(draft, "a forum") + reason;
                    if (draft.ModerationAction == ModerationActionTypeEnum.Warn) return "A moderator sent you a warning about your " + noun + reason;
                    return "A moderator " + Verb(draft.ModerationAction ?? ModerationActionTypeEnum.Remove) + " your " + noun + reason;
                case NotificationTypeEnum.ModerationOverdue:
                    return "A report on " + Article(noun) + " " + noun + " is past the " + _Settings.Moderation.ReviewTargetHours.ToString(CultureInfo.InvariantCulture) + " hour review target";
                case NotificationTypeEnum.ThreadReply:
                    return actor + " commented on " + Subject(draft, "a thread you follow");
                case NotificationTypeEnum.NewThreadInForum:
                    return actor + " posted in " + Subject(draft, "a forum you follow");
                case NotificationTypeEnum.AnswerAccepted:
                    return "Your answer was accepted on " + Subject(draft, "a question");
                case NotificationTypeEnum.FeedbackReviewed:
                    return actor + " reviewed your video";
                case NotificationTypeEnum.PollClosed:
                    return "Poll closed: " + Subject(draft, "a poll");
                case NotificationTypeEnum.ThreadPromoted:
                    return "Your thread was featured in the feed";
                case NotificationTypeEnum.ThreadMoved:
                    return "A moderator moved your thread to " + Subject(draft, "another forum");
                case NotificationTypeEnum.EventReminder:
                case NotificationTypeEnum.EventUpdated:
                case NotificationTypeEnum.EventCanceled:
                case NotificationTypeEnum.EventWaitlistPromoted:
                case NotificationTypeEnum.NewEvent:
                case NotificationTypeEnum.EventProposalDecision:
                    // Phase 4 event notifications carry their full title (docs/PHASE4_CONTRACT.md section 8.1).
                    return Subject(draft, "An event changed");
                case NotificationTypeEnum.NewLessonInSeries:
                    return Subject(draft, "New lesson in a series you follow");
                case NotificationTypeEnum.IdeaStatusChanged:
                    return "Status update on " + Subject(draft, "an idea you follow");
                case NotificationTypeEnum.TicketUpdate:
                case NotificationTypeEnum.TicketOpened:
                case NotificationTypeEnum.TicketMemberReplied:
                case NotificationTypeEnum.TicketOverdue:
                    return Subject(draft, "A support ticket was updated");
                case NotificationTypeEnum.MessageRequest:
                    return actor + " sent you a message request";
                case NotificationTypeEnum.DirectMessage:
                    return actor + " sent you a message";
                default:
                    // Phase 4 types carry their rendered title (docs/PHASE4_CONTRACT.md section 8.1).
                    if (draft.Type != NotificationTypeEnum.MembershipExpiring && !String.IsNullOrWhiteSpace(draft.TitleText)) return draft.TitleText.Trim();
                    DateTime ends = draft.EventUtc ?? _Clock();
                    return "Your free membership ends on " + ends.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// The recipient's unread count.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        public Task<long> UnreadCountAsync(string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            return _Database.Notifications.CountUnreadAsync(userId, token);
        }

        /// <summary>
        /// The unread count and the latest updatedUtc (GET /api/v1/notifications/unread-count).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Summary.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        public async Task<UnreadCountResponse> UnreadSummaryAsync(string userId, CancellationToken token = default)
        {
            long count = await UnreadCountAsync(userId, token).ConfigureAwait(false);
            DateTime? latest = await _Database.Notifications.LatestUpdatedAsync(userId, token).ConfigureAwait(false);
            return new UnreadCountResponse { UnreadCount = count, LatestUpdatedUtc = latest };
        }

        /// <summary>
        /// Enumerate the caller's notifications (POST /api/v1/notifications/enumerate and GET /api/v1/notifications):
        /// collection <see cref="NotificationCollections.Notifications"/> (orderings UpdatedDescending default,
        /// CreatedDescending, CreatedAscending; filters unreadOnly, type, category), with labels and tags and the
        /// caller's unread count.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query, or null for defaults.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken.</exception>
        public async Task<NotificationEnumerationResult> EnumerateAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string userId = actor.RequireUserId();
            bool staff = actor.IsStaffFor(TargetTypeEnum.Notification);
            EnumerationRequest request = _Enumeration.Prepare(query, NotificationCollections.Notifications, staff, "notifications|" + userId);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            bool unreadOnly = request.Query.FilterBool("unreadOnly", errors) ?? false;
            NotificationTypeEnum? type = request.Query.FilterEnum<NotificationTypeEnum>("type", errors);
            NotificationCategoryEnum? category = request.Query.FilterEnum<NotificationCategoryEnum>("category", errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationResult<Notification> page = await _Database.Notifications.EnumerateForRecipientAsync(userId, request, unreadOnly, type, category, token).ConfigureAwait(false);
            List<NotificationView> views = await ToViewsAsync(page.Objects, token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Metadata.LoadAsync(TargetTypeEnum.Notification, views.Select(v => v.Id), staff, token).ConfigureAwait(false);
            foreach (NotificationView view in views)
            {
                if (!metadata.TryGetValue(view.Id, out ObjectMetadata? meta)) continue;
                view.Labels = meta.Labels;
                view.Tags = meta.Tags;
            }
            long unread = await UnreadCountAsync(userId, token).ConfigureAwait(false);
            return NotificationEnumerationResult.From(page.With(views), unread);
        }

        /// <summary>
        /// Mark notifications read (1 to 100 ids; unknown and foreign ids are ignored).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="ids">Identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The unread count afterwards.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed when ids is missing, empty, or longer than 100.</exception>
        public async Task<long> MarkReadAsync(string userId, List<string>? ids, CancellationToken token = default)
        {
            List<string> valid = RequireIds(ids);
            int changed = await _Database.Notifications.MarkReadAsync(userId, valid, _Clock(), token).ConfigureAwait(false);
            if (changed > 0 && valid.Count == 1 && ProductEvents != null) await ProductEvents.ObserveAsync("notification.opened", userId, token).ConfigureAwait(false);
            return await UnreadCountAsync(userId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Mark notifications unread (1 to 100 ids; unknown and foreign ids are ignored).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="ids">Identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The unread count afterwards.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed when ids is missing, empty, or longer than 100.</exception>
        public async Task<long> MarkUnreadAsync(string userId, List<string>? ids, CancellationToken token = default)
        {
            List<string> valid = RequireIds(ids);
            await _Database.Notifications.MarkUnreadAsync(userId, valid, token).ConfigureAwait(false);
            return await UnreadCountAsync(userId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Mark every notification last updated at or before a time read.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="beforeUtc">Cutoff, or null for now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The unread count afterwards.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        public async Task<long> MarkAllReadAsync(string userId, DateTime? beforeUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            DateTime now = _Clock();
            DateTime cutoff = beforeUtc == null ? now : beforeUtc.Value.ToUniversalTime();
            await _Database.Notifications.MarkAllReadAsync(userId, cutoff, now, token).ConfigureAwait(false);
            return await UnreadCountAsync(userId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Build payloads (8.1) for stored notifications with one batched summary lookup. Moderation notices never expose
        /// the moderator: their actor is null and actors empty.
        /// </summary>
        /// <param name="rows">Notifications (with recent actors filled).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Payloads in the same order.</returns>
        /// <exception cref="ArgumentNullException">The rows are null.</exception>
        public async Task<List<NotificationView>> ToViewsAsync(List<Notification> rows, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(rows);
            IEnumerable<string?> ids = rows.Where(r => r.Category != NotificationCategoryEnum.Moderation).SelectMany(r => r.RecentActorIds.Cast<string?>().Append(r.ActorUserId));
            Dictionary<string, UserSummary> summaries = await _Summaries.BuildAsync(ids, token).ConfigureAwait(false);
            List<NotificationView> views = new List<NotificationView>();
            foreach (Notification row in rows)
            {
                bool hideActor = row.Category == NotificationCategoryEnum.Moderation;
                NotificationView view = new NotificationView
                {
                    Id = row.Id,
                    Type = row.Type,
                    Category = row.Category,
                    Title = row.Title,
                    Body = row.Body,
                    Actor = hideActor || row.ActorUserId == null ? null : (summaries.TryGetValue(row.ActorUserId, out UserSummary? actor) ? actor : UserSummary.Deleted()),
                    Actors = hideActor ? new List<UserSummary>() : row.RecentActorIds.Take(3).Select(a => summaries.TryGetValue(a, out UserSummary? s) ? s : UserSummary.Deleted()).ToList(),
                    ActorCount = row.ActorCount,
                    Target = new NotificationTarget { Type = row.TargetType, Id = row.TargetId },
                    Path = row.Path,
                    IsRead = row.ReadUtc != null,
                    ReadUtc = row.ReadUtc,
                    CreatedUtc = row.CreatedUtc,
                    UpdatedUtc = row.UpdatedUtc
                };
                if (row.FeedPostId != null) view.Context["feedPostId"] = row.FeedPostId;
                if (row.CommentId != null) view.Context["commentId"] = row.CommentId;
                if (row.ParentCommentId != null) view.Context["parentCommentId"] = row.ParentCommentId;
                if (row.ThreadId != null) view.Context["threadId"] = row.ThreadId;
                if (row.ForumId != null) view.Context["forumId"] = row.ForumId;
                if (row.PollId != null) view.Context["pollId"] = row.PollId;
                views.Add(view);
            }
            return views;
        }

        /// <summary>
        /// The aggregation group key of a target: Reaction:&lt;targetType&gt;:&lt;targetId&gt;.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <returns>Group key.</returns>
        public static string GroupKey(string targetType, string targetId)
        {
            return "Reaction:" + targetType + ":" + targetId;
        }

        #endregion

        #region Private-Methods

        private void RaiseCreated(IReadOnlyList<Notification> rows)
        {
            Action<IReadOnlyList<Notification>>? hook = Created;
            if (hook == null || rows.Count == 0) return;
            try
            {
                hook(rows);
            }
            catch (Exception e)
            {
                _Logger.Warn("[NotificationService] created hook failed: " + e.Message);
            }
        }

        private static string AudienceKey(NotificationDraft draft)
        {
            return draft.RecipientUserId + "|" + (draft.FeedPostId ?? String.Empty);
        }

        private async Task<HashSet<string>> AudienceDeniedAsync(List<NotificationDraft> drafts, List<User> recipients, CancellationToken token)
        {
            HashSet<string> denied = new HashSet<string>(StringComparer.Ordinal);
            List<NotificationDraft> relevant = drafts.Where(d => !String.IsNullOrEmpty(d.FeedPostId) && NotificationCatalog.CategoryOf(d.Type) != NotificationCategoryEnum.Moderation).ToList();
            if (relevant.Count == 0) return denied;
            List<FeedPost> posts = await _Database.FeedPosts.ReadManyAsync(relevant.Select(d => d.FeedPostId!).Distinct(StringComparer.Ordinal), token).ConfigureAwait(false);
            Dictionary<string, FeedPost> postsById = posts.ToDictionary(p => p.Id, StringComparer.Ordinal);
            List<string> recipientIds = relevant.Select(d => d.RecipientUserId).Distinct(StringComparer.Ordinal).ToList();
            Dictionary<string, User> usersById = recipients.Where(u => u != null).GroupBy(u => u.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            List<Entitlement> entitlements = await _Database.Entitlements.EnumerateByUsersAsync(recipientIds, token).ConfigureAwait(false);
            DateTime now = _Clock();
            HashSet<string> members = new HashSet<string>(entitlements.Where(e => e.IsActiveAt(now)).Select(e => e.UserId), StringComparer.Ordinal);
            List<RoleAssignment> roles = await _Database.RoleAssignments.EnumerateByUsersAsync(recipientIds, token).ConfigureAwait(false);
            HashSet<string> admins = new HashSet<string>(roles.Where(r => r.Role == RoleEnum.SystemAdmin).Select(r => r.UserId), StringComparer.Ordinal);
            foreach (NotificationDraft draft in relevant)
            {
                if (!postsById.TryGetValue(draft.FeedPostId!, out FeedPost? post)) continue;
                if (String.Equals(post.AuthorUserId, draft.RecipientUserId, StringComparison.Ordinal)) continue;
                bool attested = usersById.TryGetValue(draft.RecipientUserId, out User? user) && user.AgeAttestedUtc != null;
                if (!ContentAudienceRule.RecipientMayOpen(Access(), post.Audience, admins.Contains(draft.RecipientUserId), attested, members.Contains(draft.RecipientUserId))) denied.Add(AudienceKey(draft));
            }
            return denied;
        }

        private static void Validate(NotificationDraft draft)
        {
            ArgumentNullException.ThrowIfNull(draft);
            if (String.IsNullOrWhiteSpace(draft.RecipientUserId)) throw new ArgumentNullException(nameof(draft), "The draft has no recipient.");
            if (String.IsNullOrWhiteSpace(draft.TargetId)) throw new ArgumentNullException(nameof(draft), "The draft has no target id.");
        }

        private static bool IsSelf(NotificationDraft draft)
        {
            return !String.IsNullOrEmpty(draft.ActorUserId) && String.Equals(draft.ActorUserId, draft.RecipientUserId, StringComparison.Ordinal);
        }

        private static bool InAppOn(List<NotificationPreference> preferences, NotificationCategoryEnum category)
        {
            if (!NotificationCatalog.InAppEditable(category)) return true;
            NotificationPreference? row = preferences.FirstOrDefault(p => p.Category == category);
            return row == null ? NotificationCatalog.DefaultInApp(category) : row.InApp;
        }

        private async Task<string?> ActorNameAsync(string? actorUserId, CancellationToken token)
        {
            if (String.IsNullOrEmpty(actorUserId)) return null;
            UserSummary summary = await _Summaries.BuildOneAsync(actorUserId, token).ConfigureAwait(false);
            return summary.DisplayName;
        }

        private Notification ToRow(NotificationDraft draft, NotificationCategoryEnum category, string? actorName, DateTime now)
        {
            return new Notification
            {
                Id = IdGenerator.Generate(Constants.NotificationIdPrefix),
                RecipientUserId = draft.RecipientUserId,
                Type = draft.Type,
                Category = category,
                ActorUserId = String.IsNullOrEmpty(draft.ActorUserId) ? null : draft.ActorUserId,
                ActorCount = String.IsNullOrEmpty(draft.ActorUserId) ? 0 : 1,
                TargetType = draft.TargetType.ToString(),
                TargetId = draft.TargetId,
                FeedPostId = draft.FeedPostId,
                CommentId = draft.CommentId,
                ParentCommentId = draft.ParentCommentId,
                ThreadId = draft.ThreadId,
                ForumId = draft.ForumId,
                PollId = draft.PollId,
                Title = RenderTitle(draft, actorName),
                Body = String.IsNullOrWhiteSpace(draft.Body) ? null : draft.Body,
                Path = PathOf(draft),
                PushState = draft.QueuePush ? PushStateEnum.Pending : PushStateEnum.None,
                CreatedUtc = now,
                UpdatedUtc = now
            };
        }

        private static string PathOf(NotificationDraft draft)
        {
            if (!String.IsNullOrWhiteSpace(draft.Path)) return draft.Path.Trim();
            if (draft.Type == NotificationTypeEnum.MembershipExpiring) return "/account/membership";
            string? post = draft.FeedPostId ?? (draft.TargetType == TargetTypeEnum.FeedPost ? draft.TargetId : null);
            string? comment = draft.CommentId ?? (draft.TargetType == TargetTypeEnum.Comment ? draft.TargetId : null);
            if (post != null && comment != null) return "/feed/" + post + "?comment=" + comment;
            if (post != null) return "/feed/" + post;
            return "/notifications";
        }

        private static string Noun(NotificationDraft draft)
        {
            if (!String.IsNullOrWhiteSpace(draft.SubjectNoun)) return draft.SubjectNoun.Trim();
            switch (draft.TargetType)
            {
                case TargetTypeEnum.FeedPost:
                    return "post";
                case TargetTypeEnum.Comment:
                    return "comment";
                case TargetTypeEnum.Thread:
                    return "thread";
                case TargetTypeEnum.Poll:
                    return "poll";
                case TargetTypeEnum.Forum:
                    return "forum";
                case TargetTypeEnum.User:
                    return "member";
                default:
                    return "item";
            }
        }

        private static string Subject(NotificationDraft draft, string fallback)
        {
            return String.IsNullOrWhiteSpace(draft.TitleText) ? fallback : draft.TitleText.Trim();
        }

        private static string Article(string noun)
        {
            return noun.Length > 0 && "aeiouAEIOU".IndexOf(noun[0]) >= 0 ? "an" : "a";
        }

        private static string Verb(ModerationActionTypeEnum action)
        {
            switch (action)
            {
                case ModerationActionTypeEnum.Hide:
                case ModerationActionTypeEnum.AutoHide:
                    return "hid";
                case ModerationActionTypeEnum.Unhide:
                case ModerationActionTypeEnum.Restore:
                    return "restored";
                case ModerationActionTypeEnum.Dismiss:
                    return "reviewed";
                case ModerationActionTypeEnum.Lock:
                    return "locked";
                case ModerationActionTypeEnum.Unlock:
                    return "unlocked";
                default:
                    return "removed";
            }
        }

        private static List<string> RequireIds(List<string>? ids)
        {
            List<string> valid = (ids ?? new List<string>()).Where(i => !String.IsNullOrWhiteSpace(i)).Select(i => i.Trim()).Distinct(StringComparer.Ordinal).ToList();
            if (ids == null || ids.Count < 1 || ids.Count > 100 || valid.Count == 0) throw ApiErrorException.Field("ids", "Send 1 to 100 notification ids.");
            return valid;
        }

        #endregion
    }
}
