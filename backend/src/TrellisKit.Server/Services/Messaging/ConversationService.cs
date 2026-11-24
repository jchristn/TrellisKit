namespace TrellisKit.Server.Services.Messaging
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services.Realtime;

    /// <summary>
    /// Direct conversations (docs/PHASE5_CONTRACT.md sections 3.1, 3.2, 5.2, and as-built note B): the inbox lists,
    /// starting a conversation (one per pair), requests (accept, decline, undo), folders, mutes, delete for me, read and
    /// delivered positions, the sync of changes, unread counts, and the member's messaging settings.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class ConversationService
    {
        #region Public-Members

        /// <summary>
        /// Rate limit bucket of new conversations per day.
        /// </summary>
        public const string NewConversationBucket = "dm.newconversation";

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private readonly MessagingModule _Module;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <param name="module">Messaging module.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public ConversationService(ApplicationServices services, MessagingModule module)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            _Module = module ?? throw new ArgumentNullException(nameof(module));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The conversation from a member's side: 404 when it does not exist, the user is not a member, or the
        /// conversation was deleted for the user (ids never leak).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entry.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<ConversationEntry> RequireEntryAsync(string userId, string conversationId, CancellationToken token = default)
        {
            ConversationEntry? entry = String.IsNullOrWhiteSpace(conversationId) ? null : await _Services.Database.Conversations.ReadEntryAsync(conversationId.Trim(), userId, token).ConfigureAwait(false);
            if (entry == null || entry.Mine.DeletedUtc != null || entry.Mine.LeftUtc != null) throw ApiErrorException.NotFound("Conversation");
            return entry;
        }

        /// <summary>
        /// POST /api/v1/conversations/enumerate and GET /api/v1/conversations.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query (filters folder, unreadOnly, withUserId).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken.</exception>
        public async Task<EnumerationResult<DirectConversationView>> EnumerateAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            string me = _Module.Policy.RequireCaller(actor);
            EnumerationQuery q = query ?? new EnumerationQuery();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            ConversationListFilter filter = new ConversationListFilter { Folder = ConversationFolderEnum.Inbox };
            string? folder = q.FilterString("folder");
            if (folder != null)
            {
                if (String.Equals(folder, "All", StringComparison.OrdinalIgnoreCase)) filter.Folder = null;
                else if (Enum.TryParse(folder, true, out ConversationFolderEnum parsed) && Enum.IsDefined(parsed) && !Int32.TryParse(folder, out int _)) filter.Folder = parsed;
                else errors["filters.folder"] = "Use Inbox, Requests, Archived, or All.";
            }
            filter.UnreadOnly = q.FilterBool("unreadOnly", errors) ?? false;
            filter.WithUserId = q.FilterString("withUserId");
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, MessagingCollections.Conversations, false, "conversations|" + me);
            EnumerationResult<ConversationMember> page = await _Services.Database.Conversations.EnumerateAsync(me, request, filter, token).ConfigureAwait(false);
            return page.With(await ViewsAsync(actor, me, page.Objects, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/me/messaging/declined and POST /api/v1/me/messaging/declined/enumerate: requests the caller declined.
        /// Undo with POST /api/v1/conversations/{id}/accept.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">402 MembershipRequired, 400 ValidationFailed or InvalidContinuationToken.</exception>
        public async Task<EnumerationResult<DirectConversationView>> DeclinedAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            string me = _Module.Policy.RequireCaller(actor);
            EnumerationRequest request = _Services.Enumeration.Prepare(query, MessagingCollections.Declined, false, "declined|" + me);
            EnumerationResult<ConversationMember> page = await _Services.Database.Conversations.EnumerateAsync(me, request, new ConversationListFilter { Declined = true }, token).ConfigureAwait(false);
            return page.With(await ViewsAsync(actor, me, page.Objects, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/conversations/{id}.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Payload.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<DirectConversationView> GetAsync(ContentActor actor, string conversationId, CancellationToken token = default)
        {
            string me = _Module.Policy.RequireCaller(actor);
            ConversationEntry entry = await RequireEntryAsync(me, conversationId, token).ConfigureAwait(false);
            return await _Module.Views.ConversationAsync(actor, entry, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/conversations: start (or return) the direct conversation with a member, optionally with a first message.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Outcome (Created true for 201).</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or CannotMessageSelf, 403 MessagingNotAllowed, RecipientUnavailable, MessagingRestricted,
        /// RequestLimitReached, 404 NotFound, 429 RateLimited, and the send errors for the first message.</exception>
        public async Task<ConversationStartOutcome> StartAsync(ContentActor actor, DirectConversationStartRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            string me = _Module.Policy.RequireCaller(actor);
            string recipientId = (request.RecipientUserId ?? String.Empty).Trim();
            if (recipientId.Length == 0) throw ApiErrorException.Field("recipientUserId", "Required.");
            if (String.Equals(recipientId, me, StringComparison.Ordinal)) throw new ApiErrorException(400, "CannotMessageSelf", "You cannot message yourself.");
            User? recipient = await _Services.Database.Users.ReadAsync(recipientId, token).ConfigureAwait(false);
            if (recipient == null || recipient.IsDeleted) throw ApiErrorException.NotFound("Member");
            if (request.Message != null) _Module.Messages.Validate(request.Message);
            string directKey = DirectKey(me, recipientId);
            Conversation? existing = await _Services.Database.Conversations.ReadByDirectKeyAsync(directKey, token).ConfigureAwait(false);
            bool created = false;
            if (existing == null)
            {
                MessagingPeer peer = await _Module.Policy.PeerAsync(me, recipientId, token).ConfigureAwait(false);
                MessagingEligibilityEnum outcome = _Module.Policy.Evaluate(actor, peer, null);
                if (outcome != MessagingEligibilityEnum.Allowed)
                {
                    MessagingTelemetry.Conversation("create", "denied");
                    throw _Module.Policy.Refusal(outcome);
                }
                bool isRequest = _Module.Policy.IsRequest(actor, peer);
                if (isRequest)
                {
                    long pending = await _Services.Database.Conversations.CountPendingSentAsync(me, token).ConfigureAwait(false);
                    if (pending >= _Services.Settings.Messaging.MaxPendingRequests)
                    {
                        MessagingTelemetry.Deny("request_limit");
                        throw new ApiErrorException(403, "RequestLimitReached", "You have " + pending + " message requests waiting for an answer. Try again after some are accepted.");
                    }
                }
                EnforceNewConversationLimit(actor);
                DateTime now = _Module.Clock();
                Conversation conversation = new Conversation
                {
                    Id = IdGenerator.Generate(MessagingIdPrefixes.Conversation),
                    Kind = ConversationKindEnum.Direct,
                    DirectKey = directKey,
                    CreatedByUserId = me,
                    CreatedUtc = now,
                    UpdatedUtc = now
                };
                List<ConversationMember> members = new List<ConversationMember>
                {
                    NewMember(conversation.Id, me, ConversationFolderEnum.Inbox, isRequest ? ConversationRequestStateEnum.AwaitingAcceptance : null, now),
                    NewMember(conversation.Id, recipientId, isRequest ? ConversationFolderEnum.Requests : ConversationFolderEnum.Inbox, isRequest ? ConversationRequestStateEnum.Pending : null, now)
                };
                created = await _Services.Database.Conversations.CreateDirectAsync(conversation, members, token).ConfigureAwait(false);
                if (created)
                {
                    MessagingTelemetry.Conversation("create", isRequest ? "request" : "ok");
                    await _Module.PublishAsync(_Module.Event(RealtimeEventKinds.ConversationCreated, conversation.Id, new[] { me, recipientId }), token).ConfigureAwait(false);
                    existing = conversation;
                }
                else
                {
                    existing = await _Services.Database.Conversations.ReadByDirectKeyAsync(directKey, token).ConfigureAwait(false);
                    if (existing == null) throw new InvalidOperationException("The direct conversation was neither created nor found.");
                }
            }
            if (!created && await _Services.Database.Conversations.RestoreForMemberAsync(existing.Id, me, _Module.Clock(), token).ConfigureAwait(false))
                await _Module.PublishAsync(_Module.Event(RealtimeEventKinds.ConversationUpdated, existing.Id, new[] { me }), token).ConfigureAwait(false);
            ConversationEntry entry = await RequireEntryAsync(me, existing.Id, token).ConfigureAwait(false);
            ConversationStartResponse response = new ConversationStartResponse();
            if (request.Message != null)
            {
                MessageSendResult sent = await _Module.Messages.SendInAsync(actor, entry, request.Message, token).ConfigureAwait(false);
                response.Message = sent.Message;
                entry = await RequireEntryAsync(me, existing.Id, token).ConfigureAwait(false);
            }
            response.Conversation = await _Module.Views.ConversationAsync(actor, entry, token).ConfigureAwait(false);
            return new ConversationStartOutcome { Response = response, Created = created };
        }

        /// <summary>
        /// POST /api/v1/conversations/{id}/read: move the caller's read position forward (monotonic).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Position and unread count.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound.</exception>
        public async Task<ConversationReadResponse> ReadAsync(ContentActor actor, string conversationId, ConversationSeqRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            string me = _Module.Policy.RequireCaller(actor);
            long seq = RequireSeq(request);
            ConversationEntry entry = await RequireEntryAsync(me, conversationId, token).ConfigureAwait(false);
            long position = await ReadCoreAsync(entry, seq, token).ConfigureAwait(false);
            Dictionary<string, int> unread = await _Services.Database.Conversations.UnreadCountsAsync(me, new[] { entry.Conversation.Id }, token).ConfigureAwait(false);
            return new ConversationReadResponse { MyLastReadSeq = position, UnreadCount = unread.TryGetValue(entry.Conversation.Id, out int count) ? count : 0 };
        }

        /// <summary>
        /// Move a member's read position (REST read and the WebSocket read frame); publishes receipt.read when it moved.
        /// </summary>
        /// <param name="entry">Conversation from the member's side.</param>
        /// <param name="seq">Requested position.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Position afterwards.</returns>
        public async Task<long> ReadCoreAsync(ConversationEntry entry, long seq, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(entry);
            long position = await _Services.Database.Conversations.MarkReadAsync(entry.Conversation.Id, entry.Mine.UserId, seq, _Module.Clock(), token).ConfigureAwait(false) ?? entry.Mine.LastReadSeq;
            if (position > entry.Mine.LastReadSeq)
            {
                RealtimeEvent evt = _Module.Event(RealtimeEventKinds.ReceiptRead, entry.Conversation.Id, new[] { entry.Mine.UserId }.Concat(entry.Others.Select(o => o.UserId)));
                evt.Seq = position;
                evt.ActorUserId = entry.Mine.UserId;
                await _Module.PublishAsync(evt, token).ConfigureAwait(false);
            }
            return position;
        }

        /// <summary>
        /// POST /api/v1/conversations/{id}/delivered (REST twin of the WebSocket ack).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound.</exception>
        public async Task DeliveredAsync(ContentActor actor, string conversationId, ConversationSeqRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            string me = _Module.Policy.RequireCaller(actor);
            long seq = RequireSeq(request);
            ConversationEntry entry = await RequireEntryAsync(me, conversationId, token).ConfigureAwait(false);
            await DeliveredCoreAsync(entry, seq, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Move a member's delivery position; publishes receipt.delivered when it moved.
        /// </summary>
        /// <param name="entry">Conversation from the member's side.</param>
        /// <param name="seq">Requested position.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The new position, or null when unchanged.</returns>
        public async Task<long?> DeliveredCoreAsync(ConversationEntry entry, long seq, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(entry);
            long? moved = await _Services.Database.Conversations.MarkDeliveredAsync(entry.Conversation.Id, entry.Mine.UserId, seq, _Module.Clock(), token).ConfigureAwait(false);
            if (moved != null)
            {
                RealtimeEvent evt = _Module.Event(RealtimeEventKinds.ReceiptDelivered, entry.Conversation.Id, new[] { entry.Mine.UserId }.Concat(entry.Others.Select(o => o.UserId)));
                evt.Seq = moved;
                evt.ActorUserId = entry.Mine.UserId;
                await _Module.PublishAsync(evt, token).ConfigureAwait(false);
            }
            return moved;
        }

        /// <summary>
        /// POST /api/v1/conversations/{id}/accept: accept a pending request, or undo a decline.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Payload.</returns>
        /// <exception cref="ApiErrorException">404 NotFound, 409 NotPendingRequest.</exception>
        public async Task<DirectConversationView> AcceptAsync(ContentActor actor, string conversationId, CancellationToken token = default)
        {
            string me = _Module.Policy.RequireCaller(actor);
            ConversationEntry entry = await RequireEntryAsync(me, conversationId, token).ConfigureAwait(false);
            if (!await _Services.Database.Conversations.AcceptAsync(entry.Conversation.Id, me, _Module.Clock(), token).ConfigureAwait(false))
                throw new ApiErrorException(409, "NotPendingRequest", "This conversation is not a pending request to you.");
            MessagingTelemetry.Conversation("accept", "ok");
            return await ChangedAsync(actor, entry, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/conversations/{id}/decline: decline a pending request (the sender is not told).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Payload.</returns>
        /// <exception cref="ApiErrorException">404 NotFound, 409 NotPendingRequest.</exception>
        public async Task<DirectConversationView> DeclineAsync(ContentActor actor, string conversationId, CancellationToken token = default)
        {
            string me = _Module.Policy.RequireCaller(actor);
            ConversationEntry entry = await RequireEntryAsync(me, conversationId, token).ConfigureAwait(false);
            if (!await _Services.Database.Conversations.DeclineAsync(entry.Conversation.Id, me, _Module.Clock(), token).ConfigureAwait(false))
                throw new ApiErrorException(409, "NotPendingRequest", "This conversation is not a pending request to you.");
            MessagingTelemetry.Conversation("decline", "ok");
            return await ChangedAsync(actor, entry, false, token).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT /api/v1/conversations/{id}/folder.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Payload.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound, 409 RequestPending.</exception>
        public async Task<DirectConversationView> SetFolderAsync(ContentActor actor, string conversationId, ConversationFolderRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            string me = _Module.Policy.RequireCaller(actor);
            string raw = (request.Folder ?? String.Empty).Trim();
            if (!Enum.TryParse(raw, true, out ConversationFolderEnum folder) || Int32.TryParse(raw, out int _) || folder == ConversationFolderEnum.Requests)
                throw ApiErrorException.Field("folder", "Use Inbox or Archived.");
            ConversationEntry entry = await RequireEntryAsync(me, conversationId, token).ConfigureAwait(false);
            if (!await _Services.Database.Conversations.SetFolderAsync(entry.Mine.Id, folder, _Module.Clock(), token).ConfigureAwait(false))
                throw new ApiErrorException(409, "RequestPending", "Accept or decline the request first.");
            return await ChangedAsync(actor, entry, false, token).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT (mute) and DELETE (unmute) /api/v1/conversations/{id}/mute.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <param name="muted">Mute or unmute.</param>
        /// <param name="untilUtc">Mute end, or null for indefinitely.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Payload.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound.</exception>
        public async Task<DirectConversationView> MuteAsync(ContentActor actor, string conversationId, bool muted, DateTime? untilUtc, CancellationToken token = default)
        {
            string me = _Module.Policy.RequireCaller(actor);
            DateTime now = _Module.Clock();
            if (muted && untilUtc != null && untilUtc.Value.ToUniversalTime() <= now) throw ApiErrorException.Field("untilUtc", "Must be in the future (or null for indefinitely).");
            ConversationEntry entry = await RequireEntryAsync(me, conversationId, token).ConfigureAwait(false);
            await _Services.Database.Conversations.SetMuteAsync(entry.Mine.Id, muted, untilUtc?.ToUniversalTime(), now, token).ConfigureAwait(false);
            return await ChangedAsync(actor, entry, false, token).ConfigureAwait(false);
        }

        /// <summary>
        /// DELETE /api/v1/conversations/{id}: delete the conversation for the caller (hides every message up to now).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task DeleteAsync(ContentActor actor, string conversationId, CancellationToken token = default)
        {
            string me = _Module.Policy.RequireCaller(actor);
            ConversationEntry entry = await RequireEntryAsync(me, conversationId, token).ConfigureAwait(false);
            DateTime now = _Module.Clock();
            await _Services.Database.Conversations.DeleteForMemberAsync(entry.Conversation.Id, me, now, now.AddDays(_Module.PurgeAfterDays), token).ConfigureAwait(false);
            MessagingTelemetry.Conversation("delete", "ok");
            await _Module.PublishAsync(_Module.Event(RealtimeEventKinds.ConversationUpdated, entry.Conversation.Id, new[] { me }), token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/conversations/changes/enumerate (section 5.2): the caller's conversations whose state changed after
        /// the sync token, ordered by change time then id. Nonterminal pages carry continuation tokens; the final page
        /// carries a separate syncToken watermark. Tokens older than messaging.syncTokenDays are 400 InvalidContinuationToken.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query with an optional sync watermark and ordinary page continuation.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 429 RateLimited.</exception>
        public async Task<ConversationChangesResult> ChangesAsync(ContentActor actor, ConversationChangesQuery? query, CancellationToken token = default)
        {
            string me = _Module.Policy.RequireCaller(actor);
            _Services.Limits.EnforceMember(actor, MessageService.ReadBucket, _Services.Settings.Messaging.ReadsPerMinute, RateLimitService.Minute);
            ConversationChangesQuery q = query ?? new ConversationChangesQuery();
            if (String.IsNullOrWhiteSpace(q.ContinuationToken)) q.ContinuationToken = q.SyncToken;
            if (q.MaxResults == null) q.MaxResults = 100;
            EnumerationRequest request = _Services.Enumeration.Prepare(q, MessagingCollections.Changes, false, "changes|" + me, 100);
            DateTime now = _Module.Clock();
            if (request.HasPosition)
            {
                if (request.AfterKey == null || !DateTime.TryParse(request.AfterKey, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime position)
                    || position < now.AddDays(-_Services.Settings.Messaging.SyncTokenDays)) throw ContinuationTokens.Invalid();
            }
            EnumerationResult<ConversationMember> page = await _Services.Database.Conversations.EnumerateAsync(me, request, new ConversationListFilter { Changes = true }, token).ConfigureAwait(false);
            Dictionary<string, ConversationEntry> entries = await _Services.Database.Conversations.ReadEntriesAsync(me, page.Objects.Select(m => m.ConversationId), token).ConfigureAwait(false);
            List<ConversationEntry> ordered = page.Objects.Where(m => entries.ContainsKey(m.ConversationId)).Select(m => entries[m.ConversationId]).ToList();
            List<DirectConversationView> views = await _Module.Views.ConversationsAsync(actor, ordered, token).ConfigureAwait(false);
            HashSet<string> blocked = await _Services.Blocks.BlockedByAsync(me, token).ConfigureAwait(false);
            List<ConversationChangeView> changes = new List<ConversationChangeView>();
            for (int i = 0; i < ordered.Count; i++)
            {
                ConversationEntry entry = ordered[i];
                bool removed = entry.Mine.DeletedUtc != null || entry.Mine.LeftUtc != null || entry.Mine.RequestState == ConversationRequestStateEnum.Declined
                    || entry.Others.Any(o => blocked.Contains(o.UserId)) || (entry.Conversation.LastSeq <= entry.Mine.HiddenBeforeSeq && entry.Conversation.CreatedByUserId != me);
                changes.Add(new ConversationChangeView { Conversation = views[i], Change = removed ? ConversationChangeEnum.Removed : ConversationChangeEnum.Updated });
            }
            ConversationChangesResult result = new ConversationChangesResult
            {
                Success = page.Success, MaxResults = page.MaxResults, Skip = page.Skip,
                ContinuationToken = page.ContinuationToken, EndOfResults = page.EndOfResults,
                TotalRecords = page.TotalRecords, RecordsRemaining = page.RecordsRemaining,
                TimestampUtc = page.TimestampUtc, Objects = changes
            };
            if (result.ContinuationToken == null)
            {
                // Final page: the sync token. Its position advances to a minute before now (a write that committed later
                // than its own timestamp is still caught), never behind the last change returned.
                DateTime floor = now.AddMinutes(-1);
                ConversationMember? last = page.Objects.LastOrDefault();
                string? afterKey = request.AfterKey;
                string afterId = request.AfterId ?? MessagingIdPrefixes.ConversationMember;
                if (last != null) { afterKey = last.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture); afterId = last.Id; }
                DateTime current = afterKey != null && DateTime.TryParse(afterKey, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed) ? parsed : DateTime.MinValue;
                if (current < floor) { afterKey = floor.ToString("O", CultureInfo.InvariantCulture); afterId = MessagingIdPrefixes.ConversationMember; }
                result.SyncToken = ContinuationTokens.Encode(request.Fingerprint, request.Ordering.Name, afterKey, afterId);
            }
            return result;
        }

        /// <summary>
        /// GET /api/v1/me/messaging/unread-count.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts.</returns>
        /// <exception cref="ApiErrorException">402 MembershipRequired.</exception>
        public async Task<MessagingUnreadCountsView> UnreadCountAsync(ContentActor actor, CancellationToken token = default)
        {
            string me = _Module.Policy.RequireCaller(actor);
            MessagingUnreadCounts counts = await _Services.Database.Conversations.UnreadSummaryAsync(me, token).ConfigureAwait(false);
            return new MessagingUnreadCountsView { Conversations = counts.Conversations, Messages = counts.Messages, Requests = counts.Requests };
        }

        /// <summary>
        /// GET /api/v1/me/messaging/settings (account route: signed in).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Settings.</returns>
        public async Task<MessagingSettingsView> GetSettingsAsync(ContentActor actor, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Module.Policy.RequireFeature();
            string me = actor.RequireUserId();
            return View(await _Module.Policy.SettingsOfAsync(me, token).ConfigureAwait(false));
        }

        /// <summary>
        /// PUT /api/v1/me/messaging/settings (partial; omitted fields keep their value).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Settings.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public async Task<MessagingSettingsView> UpdateSettingsAsync(ContentActor actor, MessagingSettingsRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            _Module.Policy.RequireFeature();
            string me = actor.RequireUserId();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            MemberMessagingSettings settings = await _Module.Policy.SettingsOfAsync(me, token).ConfigureAwait(false);
            if (request.DmPolicy != null)
            {
                if (Enum.TryParse(request.DmPolicy.Trim(), true, out DmPolicyEnum policy) && Enum.IsDefined(policy) && !Int32.TryParse(request.DmPolicy, out int _)) settings.DmPolicy = policy;
                else errors["dmPolicy"] = "Use Everyone, Known, or Nobody.";
            }
            if (request.PushPreview != null)
            {
                if (Enum.TryParse(request.PushPreview.Trim(), true, out PushPreviewEnum preview) && Enum.IsDefined(preview) && !Int32.TryParse(request.PushPreview, out int _)) settings.PushPreview = preview;
                else errors["pushPreview"] = "Use SenderOnly, Full, or Hidden.";
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            if (request.ReadReceipts != null) settings.ReadReceipts = request.ReadReceipts.Value;
            if (request.TypingIndicators != null) settings.TypingIndicators = request.TypingIndicators.Value;
            if (request.ExemptFromNotificationHours != null) settings.IgnoreNotificationHours = request.ExemptFromNotificationHours.Value;
            settings.UserId = me;
            settings.UpdatedUtc = _Module.Clock();
            await _Services.Database.Conversations.WriteSettingsAsync(settings, token).ConfigureAwait(false);
            return View(settings);
        }

        /// <summary>
        /// The direct key of a pair: the two user ids sorted (ordinal) and joined with a colon.
        /// </summary>
        /// <param name="first">First member.</param>
        /// <param name="second">Second member.</param>
        /// <returns>Key.</returns>
        public static string DirectKey(string first, string second)
        {
            return String.CompareOrdinal(first, second) <= 0 ? first + ":" + second : second + ":" + first;
        }

        #endregion

        #region Private-Methods

        private void EnforceNewConversationLimit(ContentActor actor)
        {
            User? user = actor.User;
            bool newAccount = user != null && _Services.Settings.Messaging.NewAccountHours > 0 && user.CreatedUtc > _Module.Clock().AddHours(-_Services.Settings.Messaging.NewAccountHours);
            if (newAccount)
            {
                try
                {
                    _Services.Limits.EnforceMember(actor, NewConversationBucket + ".new", _Services.Settings.Messaging.NewAccountConversationsPerDay, RateLimitService.Day);
                }
                catch (ApiErrorException e) when (e.StatusCode == 429)
                {
                    MessagingTelemetry.Deny("restricted");
                    throw new ApiErrorException(403, "MessagingRestricted", "New accounts can start a few conversations a day. Try again later.");
                }
            }
            _Services.Limits.EnforceMember(actor, NewConversationBucket, _Services.Settings.Messaging.NewConversationsPerDay, RateLimitService.Day);
        }

        private async Task<List<DirectConversationView>> ViewsAsync(ContentActor actor, string me, List<ConversationMember> rows, CancellationToken token)
        {
            Dictionary<string, ConversationEntry> entries = await _Services.Database.Conversations.ReadEntriesAsync(me, rows.Select(r => r.ConversationId), token).ConfigureAwait(false);
            List<ConversationEntry> ordered = rows.Where(r => entries.ContainsKey(r.ConversationId)).Select(r => entries[r.ConversationId]).ToList();
            return await _Module.Views.ConversationsAsync(actor, ordered, token).ConfigureAwait(false);
        }

        private async Task<DirectConversationView> ChangedAsync(ContentActor actor, ConversationEntry before, bool everyone, CancellationToken token)
        {
            IEnumerable<string> recipients = everyone ? new[] { before.Mine.UserId }.Concat(before.Others.Select(o => o.UserId)) : new[] { before.Mine.UserId };
            await _Module.PublishAsync(_Module.Event(RealtimeEventKinds.ConversationUpdated, before.Conversation.Id, recipients), token).ConfigureAwait(false);
            ConversationEntry entry = await _Services.Database.Conversations.ReadEntryAsync(before.Conversation.Id, before.Mine.UserId, token).ConfigureAwait(false) ?? before;
            return await _Module.Views.ConversationAsync(actor, entry, token).ConfigureAwait(false);
        }

        private static long RequireSeq(ConversationSeqRequest request)
        {
            if (request.Seq == null || request.Seq.Value < 0) throw ApiErrorException.Field("seq", "Required: a sequence number of 0 or more.");
            return request.Seq.Value;
        }

        private static ConversationMember NewMember(string conversationId, string userId, ConversationFolderEnum folder, ConversationRequestStateEnum? state, DateTime now)
        {
            return new ConversationMember
            {
                Id = IdGenerator.Generate(MessagingIdPrefixes.ConversationMember),
                ConversationId = conversationId,
                UserId = userId,
                Role = ConversationMemberRoleEnum.Member,
                Folder = folder,
                RequestState = state,
                LastActivityUtc = now,
                JoinedUtc = now,
                UpdatedUtc = now
            };
        }

        private static MessagingSettingsView View(MemberMessagingSettings settings)
        {
            return new MessagingSettingsView
            {
                DmPolicy = settings.DmPolicy,
                ReadReceipts = settings.ReadReceipts,
                TypingIndicators = settings.TypingIndicators,
                PushPreview = settings.PushPreview,
                ExemptFromNotificationHours = settings.IgnoreNotificationHours,
                UpdatedUtc = settings.UpdatedUtc
            };
        }

        #endregion
    }
}
