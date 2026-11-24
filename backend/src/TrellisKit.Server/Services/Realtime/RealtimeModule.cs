namespace TrellisKit.Server.Services.Realtime
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Security;

    /// <summary>
    /// The per-property realtime services (<see cref="ApplicationServices.Realtime"/>, docs/PHASE5_CONTRACT.md section
    /// 4): the seams the messaging module plugs into and the engine's own live events.
    /// <list type="bullet">
    /// <item><see cref="Inbound"/>: client frame handlers by frame type (typing, ack, read, future types).</item>
    /// <item><see cref="Renderers"/>: bus event kind to per-recipient frame data, with the 2 second <see cref="Cache"/>.</item>
    /// <item><see cref="Registry"/>: realtimeconnections rows and <see cref="RealtimeConnectionRegistry.ForegroundAsync"/> for the push decision.</item>
    /// <item><see cref="DeliverAsync"/> and <see cref="DeliverToUsersAsync"/>: deliver an event to members of this property (in-process fast path plus the bus); <see cref="NotifyStatement"/> to publish inside a write transaction instead.</item>
    /// <item><see cref="EvaluateAccessAsync"/>: the socket capabilities of a session (messaging per section 0.3 and resolved decision 26).</item>
    /// <item><see cref="UnreadCountContributors"/>: the messaging module adds conversations, messages, and requests to unread.counts.</item>
    /// </list>
    /// <see cref="Attach"/> wires the engine hooks: session revocations (TokenService) publish session.revoked, member
    /// invalidations publish access.changed, stored notifications publish notification.created (small batches only),
    /// and access model edits re-evaluate this task's sockets.
    /// Thread safety: safe to use concurrently.
    /// </summary>
    public sealed class RealtimeModule
    {
        #region Public-Members

        /// <summary>
        /// Client frame handlers.
        /// </summary>
        public RealtimeInboundHandlerRegistry Inbound { get; } = new RealtimeInboundHandlerRegistry();

        /// <summary>
        /// Event renderers.
        /// </summary>
        public RealtimeRendererRegistry Renderers { get; } = new RealtimeRendererRegistry();

        /// <summary>
        /// Render cache (2 seconds by key).
        /// </summary>
        public RealtimeRenderCache Cache { get; } = new RealtimeRenderCache();

        /// <summary>
        /// Connection registry of this property.
        /// </summary>
        public RealtimeConnectionRegistry Registry { get; }

        /// <summary>
        /// This task's gateway (in-process fast path), or null when no gateway runs (services built outside a server).
        /// Set by the property directory.
        /// </summary>
        public IRealtimeLocalSink? LocalSink { get; set; } = null;

        /// <summary>
        /// A notification batch larger than this is not published per recipient (bulk fan-outs; recipients see the
        /// counts on their next interaction, section 4.12). Default 20. Minimum 0, maximum 500.
        /// </summary>
        public int MaxNotificationEventsPerBatch
        {
            get { return _MaxNotificationEventsPerBatch; }
            set { _MaxNotificationEventsPerBatch = Math.Clamp(value, 0, 500); }
        }

        /// <summary>
        /// Contributors to unread.counts: each adds keys (for example conversations, messages, requests) for a member.
        /// The module itself fills notifications.
        /// </summary>
        public List<Func<string, Dictionary<string, long>, CancellationToken, Task>> UnreadCountContributors { get; } = new List<Func<string, Dictionary<string, long>, CancellationToken, Task>>();

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private int _MaxNotificationEventsPerBatch = 20;
        private bool _Attached = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the module. Call <see cref="Attach"/> once the service graph is complete.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public RealtimeModule(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            Registry = new RealtimeConnectionRegistry(services.Database);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the built-in renderers and wire the engine hooks (idempotent).
        /// </summary>
        public void Attach()
        {
            if (_Attached) return;
            _Attached = true;
            Renderers.Register(RealtimeEventKinds.NotificationCreated, null, RealtimeCapabilities.Notifications, false, RenderNotificationAsync);
            Renderers.Register(RealtimeEventKinds.UnreadCounts, null, RealtimeCapabilities.Notifications, false, RenderUnreadCountsAsync);
            Renderers.Register(RealtimeEventKinds.TicketUpdated, null, RealtimeCapabilities.Tickets, false, RenderTicketAsync);
            Renderers.Register(RealtimeEventKinds.FeatureDisabled, null, null, false, RenderFeatureDisabledAsync);

            _Services.Tokens.SessionInvalidated = sessionId => Publish(new RealtimeEvent { Kind = RealtimeEventKinds.SessionRevoked, SessionId = sessionId });
            _Services.Tokens.UserInvalidated = userId => Publish(new RealtimeEvent { Kind = RealtimeEventKinds.AccessChanged, UserIds = new List<string> { userId } });
            _Services.Notifications.Created = OnNotificationsCreated;
            Action<AccessPolicy>? previous = _Services.Access.Changed;
            _Services.Access.Changed = policy =>
            {
                previous?.Invoke(policy);
                // Every task sees the new policy on its own registry reload, so the re-evaluation is local.
                LocalSink?.DispatchLocal(new RealtimeEvent { PropertyId = _Services.PropertyId, Kind = RealtimeEventKinds.AccessChanged });
            };
        }

        /// <summary>
        /// Deliver an event to members of this property: dispatched to this task's sockets at once (fast path) and
        /// published on the bus for the other tasks (this task drops the echo by event id). Call after the write
        /// committed; inside a write transaction append <see cref="NotifyStatement"/> instead.
        /// </summary>
        /// <param name="realtimeEvent">Event (references only; its property id is overwritten with this property).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The event is null.</exception>
        /// <exception cref="ArgumentException">The serialized event is larger than 1 KB (PostgreSQL bus).</exception>
        public async Task DeliverAsync(RealtimeEvent realtimeEvent, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(realtimeEvent);
            realtimeEvent.PropertyId = _Services.PropertyId;
            LocalSink?.DispatchLocal(realtimeEvent);
            await _Services.RealtimeBus.PublishAsync(realtimeEvent, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Deliver an event of a kind to a set of members of this property (see <see cref="DeliverAsync"/>).
        /// </summary>
        /// <param name="kind">Event kind.</param>
        /// <param name="userIds">Recipients (deduplicated; at most realtime.maxRecipientsPerEvent worth for one conversation).</param>
        /// <param name="configure">Sets the other references (conversation, seq, message, target, state), or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The event delivered.</returns>
        /// <exception cref="ArgumentNullException">The kind or recipients are null.</exception>
        public async Task<RealtimeEvent> DeliverToUsersAsync(string kind, IEnumerable<string> userIds, Action<RealtimeEvent>? configure = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(kind)) throw new ArgumentNullException(nameof(kind));
            ArgumentNullException.ThrowIfNull(userIds);
            RealtimeEvent realtimeEvent = new RealtimeEvent { Kind = kind, UserIds = userIds.Where(u => !String.IsNullOrEmpty(u)).Distinct(StringComparer.Ordinal).ToList() };
            configure?.Invoke(realtimeEvent);
            await DeliverAsync(realtimeEvent, token).ConfigureAwait(false);
            return realtimeEvent;
        }

        /// <summary>
        /// The statement that publishes an event when the write transaction it is appended to commits (null with the
        /// in-process bus: then call <see cref="DeliverAsync"/> after the write). The property id is set to this property.
        /// </summary>
        /// <param name="realtimeEvent">Event.</param>
        /// <returns>Statement, or null.</returns>
        /// <exception cref="ArgumentNullException">The event is null.</exception>
        public string? NotifyStatement(RealtimeEvent realtimeEvent)
        {
            ArgumentNullException.ThrowIfNull(realtimeEvent);
            realtimeEvent.PropertyId = _Services.PropertyId;
            return _Services.RealtimeBus.NotifyStatement(realtimeEvent);
        }

        /// <summary>
        /// The socket capabilities of a session now: notifications and tickets always; messaging when the property's
        /// messaging feature is on, the messaging kill switch is off for this caller, and the session's tier meets the
        /// messaging tier of the access model (Member in FullyGated or with messaging gated, else SignedIn).
        /// </summary>
        /// <param name="principal">Validated session.</param>
        /// <param name="header">Upgrade request headers (kill switch targeting by client and platform).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Access.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<RealtimeAccess> EvaluateAccessAsync(SessionPrincipal principal, Func<string, string?> header, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(principal);
            ArgumentNullException.ThrowIfNull(header);
            AccessPolicy policy = _Services.Access.Current;
            AccessTierEnum tier = AccessDecider.TierOf(principal.IsSystemAdmin, true, principal.AgeAttested, principal.HasMembership);
            string? killed = await KillSwitchMessageAsync(Constants.ApiPrefix + "/conversations", principal, header, token).ConfigureAwait(false);
            bool messaging = _Services.Property.Property.Features.Messaging && killed == null && AccessDecider.Meets(tier, AccessDecider.WriteTier(policy, FeatureKeyEnum.Messaging));
            RealtimeAccess access = new RealtimeAccess { AccessTier = tier, MessagingKilled = killed != null, MessagingKilledMessage = killed };
            if (messaging) access.Capabilities.Add(RealtimeCapabilities.Messaging);
            access.Capabilities.Add(RealtimeCapabilities.Notifications);
            access.Capabilities.Add(RealtimeCapabilities.Tickets);
            return access;
        }

        /// <summary>
        /// The message of the kill switch that blocks a route for a caller, or null when it is not blocked.
        /// </summary>
        /// <param name="path">Route path, for example /api/v1/ws.</param>
        /// <param name="principal">Session, or null before hello.</param>
        /// <param name="header">Request headers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Message, or null.</returns>
        /// <exception cref="ArgumentNullException">The path or header accessor is null.</exception>
        public async Task<string?> KillSwitchMessageAsync(string path, SessionPrincipal? principal, Func<string, string?> header, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            ArgumentNullException.ThrowIfNull(header);
            RequestContext? context = principal == null ? null : new RequestContext
            {
                IsAuthenticated = true,
                PropertyId = _Services.PropertyId,
                UserId = principal.UserId,
                SessionId = principal.SessionId,
                IsAdmin = principal.IsSystemAdmin,
                IsModerator = principal.IsModerator,
                HasMembership = principal.HasMembership,
                AgeAttested = principal.AgeAttested
            };
            AppGateDecision? decision = await _Services.LiveUpdates.AppConfig.GateAsync("GET", path, context, header, token).ConfigureAwait(false);
            if (decision == null || !String.Equals(decision.Error, "FeatureDisabled", StringComparison.Ordinal)) return null;
            return decision.Message;
        }

        /// <summary>
        /// The unread counts of a member (unread.counts data): notifications plus every contributor's keys.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        public async Task<Dictionary<string, long>> UnreadCountsAsync(string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            Dictionary<string, long> counts = new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["notifications"] = await _Services.Notifications.UnreadCountAsync(userId, token).ConfigureAwait(false)
            };
            foreach (Func<string, Dictionary<string, long>, CancellationToken, Task> contributor in UnreadCountContributors.ToList())
                await contributor(userId, counts, token).ConfigureAwait(false);
            return counts;
        }

        #endregion

        #region Private-Methods

        private void Publish(RealtimeEvent realtimeEvent)
        {
            _ = PublishSafelyAsync(realtimeEvent);
        }

        private async Task PublishSafelyAsync(RealtimeEvent realtimeEvent)
        {
            try
            {
                await DeliverAsync(realtimeEvent, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _Services.Logger.Warn("[RealtimeModule] [p:" + _Services.PropertyId + "] publish " + realtimeEvent.Kind + " failed: " + e.Message);
            }
        }

        private void OnNotificationsCreated(IReadOnlyList<Notification> rows)
        {
            if (rows.Count == 0 || rows.Count > _MaxNotificationEventsPerBatch) return;
            foreach (Notification row in rows)
            {
                if (String.IsNullOrEmpty(row.Id) || String.IsNullOrEmpty(row.RecipientUserId)) continue;
                Cache.Remove("notification-row:" + row.Id);
                Cache.Remove("notification:" + row.Id);
                Publish(new RealtimeEvent { Kind = RealtimeEventKinds.NotificationCreated, TargetId = row.Id, UserIds = new List<string> { row.RecipientUserId } });
            }
        }

        private async Task<object?> RenderNotificationAsync(RealtimeRenderContext context, CancellationToken token)
        {
            string? id = context.Event.TargetId;
            if (String.IsNullOrEmpty(id)) return null;
            Notification? row = await context.Cache.GetOrLoadAsync<Notification>("notification-row:" + id, t => _Services.Database.Notifications.ReadAsync(id, t), token).ConfigureAwait(false);
            if (row == null || !String.Equals(row.RecipientUserId, context.RecipientUserId, StringComparison.Ordinal)) return null;
            NotificationView? view = await context.Cache.GetOrLoadAsync<NotificationView>("notification:" + id, async t =>
            {
                List<NotificationView> views = await _Services.Notifications.ToViewsAsync(new List<Notification> { row }, t).ConfigureAwait(false);
                if (views.Count == 0) return null;
                Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.Notification, new[] { id }, false, t).ConfigureAwait(false);
                if (metadata.TryGetValue(id, out ObjectMetadata? meta))
                {
                    views[0].Labels = meta.Labels;
                    views[0].Tags = meta.Tags;
                }
                return views[0];
            }, token).ConfigureAwait(false);
            if (view == null) return null;
            long unread = await _Services.Notifications.UnreadCountAsync(context.RecipientUserId, token).ConfigureAwait(false);
            return new Dictionary<string, object?> { ["notification"] = view, ["unreadCount"] = unread };
        }

        private async Task<object?> RenderUnreadCountsAsync(RealtimeRenderContext context, CancellationToken token)
        {
            return await UnreadCountsAsync(context.RecipientUserId, token).ConfigureAwait(false);
        }

        private Task<object?> RenderTicketAsync(RealtimeRenderContext context, CancellationToken token)
        {
            if (String.IsNullOrEmpty(context.Event.TargetId)) return Task.FromResult<object?>(null);
            // A hint only: the client fetches the ticket over REST (no ticket tables are read here).
            object data = new Dictionary<string, object?>
            {
                ["ticketId"] = context.Event.TargetId,
                ["status"] = context.Event.State,
                ["lastActivityUtc"] = context.Event.PublishedUtc
            };
            return Task.FromResult<object?>(data);
        }

        private Task<object?> RenderFeatureDisabledAsync(RealtimeRenderContext context, CancellationToken token)
        {
            object data = new Dictionary<string, object?>
            {
                ["feature"] = context.Event.State,
                ["message"] = "This feature is temporarily unavailable."
            };
            return Task.FromResult<object?>(data);
        }

        #endregion
    }
}
