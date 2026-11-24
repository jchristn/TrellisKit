namespace TrellisKit.Server.Services.Messaging
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Direct message access and eligibility (docs/PHASE5_CONTRACT.md sections 0.3, 1.2 to 1.4, 3.2, resolved decision 26):
    /// the feature switch, the caller's and the recipient's messaging access, blocks in both directions, the recipient's
    /// message policy, requests, mutes, and the conversation viewer flags. One 403 MessagingNotAllowed covers blocks,
    /// policy, declined requests, and canSendDirectMessages, so a block is never revealed.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class MessagingPolicyService
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
        public MessagingPolicyService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether the access model requires a membership for messaging (FullyGated, or PartiallyGated with messaging gated).
        /// </summary>
        /// <returns>True when a membership is required.</returns>
        public bool MembershipRequired()
        {
            AccessPolicy policy = _Services.Access.Current;
            return policy.Model == AccessModelEnum.FullyGated || policy.IsGated(FeatureKeyEnum.Messaging);
        }

        /// <summary>
        /// 404 NotAvailable when the property turned messaging off (features.messaging).
        /// </summary>
        /// <exception cref="ApiErrorException">404 NotAvailable.</exception>
        public void RequireFeature()
        {
            if (!_Services.Property.Property.Features.Messaging) throw new ApiErrorException(404, "NotAvailable", "Direct messages are not available in this community.");
        }

        /// <summary>
        /// The caller's member session and messaging access: the feature switch, then 402 MembershipRequired (feature
        /// messaging) for members without a membership where the access model requires one. The route gate applies the same
        /// rule to /conversations and /messages; /me/messaging routes are account routes, so their services call this.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <returns>The caller's user id.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">404 NotAvailable, 403 Forbidden (admin token), 402 MembershipRequired.</exception>
        public string RequireCaller(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            RequireFeature();
            string userId = actor.RequireUserId();
            if (!CallerHasAccess(actor))
            {
                AccessDeniedResponse body = new AccessDeniedResponse
                {
                    Error = "MembershipRequired",
                    Message = "Direct messages are for members.",
                    Required = "Membership",
                    Feature = AccessPolicy.FeatureKey(FeatureKeyEnum.Messaging)
                };
                throw new ApiErrorException(402, "MembershipRequired", body.Message, null, body);
            }
            return userId;
        }

        /// <summary>
        /// Whether a caller has messaging access under the access model.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <returns>True when the caller may use messaging.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        public bool CallerHasAccess(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (actor.IsSystemAdmin) return true;
            if (actor.IsAnonymous || String.IsNullOrEmpty(actor.UserId) || !actor.AgeAttested) return false;
            if (actor.User != null && (actor.User.IsBanned || actor.User.IsDeleted)) return false;
            return !MembershipRequired() || actor.HasMembership;
        }

        /// <summary>
        /// Of the members, those with messaging access now: not deleted or banned, age attested, and a membership when the
        /// access model requires one (SystemAdmin always).
        /// </summary>
        /// <param name="userIds">Members.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Members with access.</returns>
        /// <exception cref="ArgumentNullException">The ids are null.</exception>
        public async Task<HashSet<string>> WithAccessAsync(IEnumerable<string> userIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(userIds);
            List<string> ids = userIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            if (ids.Count == 0) return result;
            List<User> users = await _Services.Database.Users.ReadManyAsync(ids, token).ConfigureAwait(false);
            HashSet<string> admins = await _Services.Summaries.SystemAdminsAsync(ids, token).ConfigureAwait(false);
            bool required = MembershipRequired();
            HashSet<string> members = new HashSet<string>(StringComparer.Ordinal);
            if (required)
            {
                DateTime now = DateTime.UtcNow;
                foreach (Entitlement e in await _Services.Database.Entitlements.EnumerateByUsersAsync(ids, token).ConfigureAwait(false))
                    if (e.IsActiveAt(now)) members.Add(e.UserId);
            }
            foreach (User user in users)
            {
                if (user.IsDeleted) continue;
                if (admins.Contains(user.Id)) { result.Add(user.Id); continue; }
                if (user.IsBanned || user.AgeAttestedUtc == null) continue;
                if (!required || members.Contains(user.Id)) result.Add(user.Id);
            }
            return result;
        }

        /// <summary>
        /// The other members of a viewer's conversations in one batch: account, access, staff, blocks both ways, whether
        /// they know the viewer, and their messaging settings.
        /// </summary>
        /// <param name="viewerUserId">Viewer.</param>
        /// <param name="userIds">Other members.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Peers keyed by user id.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<Dictionary<string, MessagingPeer>> PeersAsync(string viewerUserId, IEnumerable<string> userIds, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(viewerUserId)) throw new ArgumentNullException(nameof(viewerUserId));
            ArgumentNullException.ThrowIfNull(userIds);
            List<string> ids = userIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            Dictionary<string, MessagingPeer> result = new Dictionary<string, MessagingPeer>(StringComparer.Ordinal);
            if (ids.Count == 0) return result;
            Dictionary<string, User> users = (await _Services.Database.Users.ReadManyAsync(ids, token).ConfigureAwait(false)).ToDictionary(u => u.Id, StringComparer.Ordinal);
            HashSet<string> access = await WithAccessAsync(ids, token).ConfigureAwait(false);
            HashSet<string> staff = await _Services.Summaries.StaffAsync(ids, token).ConfigureAwait(false);
            HashSet<string> blockedByViewer = await _Services.Blocks.BlockedByAsync(viewerUserId, token).ConfigureAwait(false);
            HashSet<string> blockersOfViewer = await _Services.Blocks.BlockersOfAsync(viewerUserId, token).ConfigureAwait(false);
            HashSet<string> known = await _Services.Database.Conversations.KnownByAmongAsync(viewerUserId, ids, token).ConfigureAwait(false);
            Dictionary<string, MemberMessagingSettings> settings = await _Services.Database.Conversations.ReadSettingsAsync(ids, token).ConfigureAwait(false);
            foreach (string id in ids)
            {
                result[id] = new MessagingPeer
                {
                    UserId = id,
                    User = users.TryGetValue(id, out User? user) && !user.IsDeleted ? user : null,
                    HasAccess = access.Contains(id),
                    IsStaff = staff.Contains(id),
                    BlockedByViewer = blockedByViewer.Contains(id),
                    BlocksViewer = blockersOfViewer.Contains(id),
                    KnowsViewer = known.Contains(id),
                    Settings = settings.TryGetValue(id, out MemberMessagingSettings? s) ? s : Defaults(id)
                };
            }
            return result;
        }

        /// <summary>
        /// One peer (see <see cref="PeersAsync"/>).
        /// </summary>
        /// <param name="viewerUserId">Viewer.</param>
        /// <param name="userId">Other member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Peer.</returns>
        public async Task<MessagingPeer> PeerAsync(string viewerUserId, string userId, CancellationToken token = default)
        {
            Dictionary<string, MessagingPeer> peers = await PeersAsync(viewerUserId, new[] { userId }, token).ConfigureAwait(false);
            return peers.TryGetValue(userId, out MessagingPeer? peer) ? peer : new MessagingPeer { UserId = userId, Settings = Defaults(userId) };
        }

        /// <summary>
        /// A member's settings, or the defaults (messaging.defaultDmPolicy, messaging.defaultPushPreview).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Settings.</returns>
        public async Task<MemberMessagingSettings> SettingsOfAsync(string userId, CancellationToken token = default)
        {
            Dictionary<string, MemberMessagingSettings> settings = await _Services.Database.Conversations.ReadSettingsAsync(new[] { userId }, token).ConfigureAwait(false);
            return settings.TryGetValue(userId, out MemberMessagingSettings? s) ? s : Defaults(userId);
        }

        /// <summary>
        /// The default settings of a member without a stored row.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <returns>Defaults.</returns>
        public MemberMessagingSettings Defaults(string userId)
        {
            return new MemberMessagingSettings
            {
                UserId = userId ?? String.Empty,
                DmPolicy = _Services.Settings.Messaging.DefaultDmPolicy,
                PushPreview = _Services.Settings.Messaging.DefaultPushPreview,
                ReadReceipts = true,
                TypingIndicators = true,
                IgnoreNotificationHours = false,
                UpdatedUtc = null
            };
        }

        /// <summary>
        /// Evaluate whether a sender may send to a peer, in the order of section 3.2: the sender's access and
        /// canSendDirectMessages, the sender's mute (replies inside a conversation with staff pass), the recipient's
        /// existence and access, blocks in either direction, a declined request, and the recipient's policy (staff
        /// always pass).
        /// </summary>
        /// <param name="sender">Sender.</param>
        /// <param name="peer">Recipient.</param>
        /// <param name="entry">The existing conversation from the sender's side, or null for a new one.</param>
        /// <returns>Outcome.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public MessagingEligibilityEnum Evaluate(ContentActor sender, MessagingPeer peer, ConversationEntry? entry)
        {
            ArgumentNullException.ThrowIfNull(sender);
            ArgumentNullException.ThrowIfNull(peer);
            if (!CallerHasAccess(sender)) return MessagingEligibilityEnum.SenderNoAccess;
            if (!sender.IsSystemAdmin && sender.User != null && !sender.User.CanSendDirectMessages) return MessagingEligibilityEnum.NotAllowed;
            if (sender.IsMuted && !(entry != null && peer.IsStaff)) return MessagingEligibilityEnum.Restricted;
            if (peer.User == null || !peer.HasAccess) return MessagingEligibilityEnum.RecipientUnavailable;
            if (entry != null && !entry.Others.Any(o => String.Equals(o.UserId, peer.UserId, StringComparison.Ordinal) && o.LeftUtc == null)) return MessagingEligibilityEnum.RecipientUnavailable;
            if (peer.BlockedByViewer || peer.BlocksViewer) return MessagingEligibilityEnum.NotAllowed;
            ConversationMember? other = entry?.Others.FirstOrDefault(o => String.Equals(o.UserId, peer.UserId, StringComparison.Ordinal));
            if (other != null && other.RequestState == ConversationRequestStateEnum.Declined) return MessagingEligibilityEnum.NotAllowed;
            bool staffSender = sender.IsSystemAdmin || sender.IsStaff;
            if (staffSender) return MessagingEligibilityEnum.Allowed;
            // A reply to a request the sender received is always allowed (replying accepts).
            if (entry != null && (entry.Mine.RequestState == ConversationRequestStateEnum.Pending || entry.Mine.RequestState == ConversationRequestStateEnum.Declined)) return MessagingEligibilityEnum.Allowed;
            switch (peer.Settings.DmPolicy)
            {
                case DmPolicyEnum.Nobody:
                    return MessagingEligibilityEnum.NotAllowed;
                case DmPolicyEnum.Known:
                    return peer.KnowsViewer ? MessagingEligibilityEnum.Allowed : MessagingEligibilityEnum.NotAllowed;
                default:
                    return MessagingEligibilityEnum.Allowed;
            }
        }

        /// <summary>
        /// The route error of a refusal (counted in trelliskit.messaging.denied).
        /// </summary>
        /// <param name="outcome">A refusal outcome.</param>
        /// <returns>Error.</returns>
        public ApiErrorException Refusal(MessagingEligibilityEnum outcome)
        {
            switch (outcome)
            {
                case MessagingEligibilityEnum.SenderNoAccess:
                    MessagingTelemetry.Deny("unavailable");
                    AccessDeniedResponse body = new AccessDeniedResponse { Error = "MembershipRequired", Message = "Direct messages are for members.", Required = "Membership", Feature = AccessPolicy.FeatureKey(FeatureKeyEnum.Messaging) };
                    return new ApiErrorException(402, "MembershipRequired", body.Message, null, body);
                case MessagingEligibilityEnum.RecipientUnavailable:
                    MessagingTelemetry.Deny("unavailable");
                    return new ApiErrorException(403, "RecipientUnavailable", "This member can't receive messages right now.");
                case MessagingEligibilityEnum.Restricted:
                    MessagingTelemetry.Deny("restricted");
                    return new ApiErrorException(403, "MessagingRestricted", "You can't send messages right now.");
                default:
                    MessagingTelemetry.Deny("block_or_policy");
                    return new ApiErrorException(403, "MessagingNotAllowed", "You can't message this member.");
            }
        }

        /// <summary>
        /// Whether a new conversation from the sender to the peer is a message request: the sender is not staff and the
        /// recipient never messaged or accepted the sender.
        /// </summary>
        /// <param name="sender">Sender.</param>
        /// <param name="peer">Recipient.</param>
        /// <returns>True for a request.</returns>
        public bool IsRequest(ContentActor sender, MessagingPeer peer)
        {
            ArgumentNullException.ThrowIfNull(sender);
            ArgumentNullException.ThrowIfNull(peer);
            if (sender.IsSystemAdmin || sender.IsStaff) return false;
            return !peer.KnowsViewer;
        }

        /// <summary>
        /// The conversation viewer flags of section 1.4.
        /// </summary>
        /// <param name="actor">Viewer.</param>
        /// <param name="entry">Conversation from the viewer's side.</param>
        /// <param name="peer">The other member, or null after the other member's account was deleted.</param>
        /// <returns>Flags.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public ConversationViewer Viewer(ContentActor actor, ConversationEntry entry, MessagingPeer? peer)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(entry);
            ConversationViewer viewer = new ConversationViewer { CanReport = true };
            ConversationRequestStateEnum? state = entry.Mine.RequestState;
            viewer.CanAccept = state == ConversationRequestStateEnum.Pending || state == ConversationRequestStateEnum.Declined;
            viewer.CanDecline = state == ConversationRequestStateEnum.Pending;
            viewer.CanBlock = peer != null && peer.User != null && !peer.IsStaff && !peer.BlockedByViewer;
            MessagingEligibilityEnum outcome = peer == null ? MessagingEligibilityEnum.RecipientUnavailable : Evaluate(actor, peer, entry);
            bool limit = state == ConversationRequestStateEnum.AwaitingAcceptance && entry.Mine.RequestMessageCount >= _Services.Settings.Messaging.RequestMessageLimit;
            if (outcome == MessagingEligibilityEnum.Allowed && limit) viewer.CannotSendReason = MessagingCannotSendReasonEnum.RequestPending;
            else if (outcome == MessagingEligibilityEnum.RecipientUnavailable) viewer.CannotSendReason = MessagingCannotSendReasonEnum.RecipientUnavailable;
            else if (outcome == MessagingEligibilityEnum.Restricted) viewer.CannotSendReason = MessagingCannotSendReasonEnum.MessagingRestricted;
            else if (outcome == MessagingEligibilityEnum.SenderNoAccess) viewer.CannotSendReason = MessagingCannotSendReasonEnum.Unavailable;
            else if (outcome == MessagingEligibilityEnum.NotAllowed)
            {
                if (peer != null && peer.BlockedByViewer) viewer.CannotSendReason = MessagingCannotSendReasonEnum.Blocked;
                else if (actor.User != null && !actor.User.CanSendDirectMessages) viewer.CannotSendReason = MessagingCannotSendReasonEnum.MessagingRestricted;
                else viewer.CannotSendReason = MessagingCannotSendReasonEnum.Unavailable;
            }
            viewer.CanSend = viewer.CannotSendReason == null;
            bool media = viewer.CanSend && _Services.Settings.Messaging.AllowMedia && _Services.Settings.Messaging.MaxAttachments > 0;
            viewer.CanAttachMedia = media && actor.CanAttachMedia;
            viewer.CanAttachVideo = media && actor.CanAttachVideo;
            return viewer;
        }

        #endregion
    }
}
