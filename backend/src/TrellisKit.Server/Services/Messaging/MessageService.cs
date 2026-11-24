namespace TrellisKit.Server.Services.Messaging
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services.Realtime;

    /// <summary>
    /// Direct messages (docs/PHASE5_CONTRACT.md sections 3.3 to 3.5 and as-built note B): send with server assigned,
    /// contiguous sequence numbers and idempotent retries, one edit inside the edit window with the original kept, delete
    /// for everyone as a tombstone (content kept internally), reactions, history with keyset paging over seq, and full
    /// text search over the caller's own conversations. Message text is never logged.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class MessageService
    {
        #region Public-Members

        /// <summary>
        /// Rate limit bucket of sends per minute.
        /// </summary>
        public const string SendMinuteBucket = "dm.send.minute";

        /// <summary>
        /// Rate limit bucket of sends per day.
        /// </summary>
        public const string SendDayBucket = "dm.send.day";

        /// <summary>
        /// Rate limit bucket of edits and deletes.
        /// </summary>
        public const string EditBucket = "dm.edit";

        /// <summary>
        /// Rate limit bucket of history, search, and sync reads.
        /// </summary>
        public const string ReadBucket = "dm.read";

        /// <summary>
        /// Longest client message id. Default 100.
        /// </summary>
        public const int MaxClientMessageIdLength = 100;

        /// <summary>
        /// Default history page size. Default 50 (maximum 100).
        /// </summary>
        public const int DefaultHistoryPage = 50;

        /// <summary>
        /// Shortest message search text. Default 2.
        /// </summary>
        public const int MinSearchLength = 2;

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
        public MessageService(ApplicationServices services, MessagingModule module)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            _Module = module ?? throw new ArgumentNullException(nameof(module));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// POST /api/v1/conversations/{id}/messages.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The message and whether it was created.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, MessageEmpty, ContentFiltered, MediaLimitExceeded; 403 MessagingNotAllowed,
        /// RecipientUnavailable, RequestLimitReached, MessagingRestricted, MediaNotAllowed; 404 NotFound; 409 MediaNotAttachable; 429 RateLimited.</exception>
        public async Task<MessageSendResult> SendAsync(ContentActor actor, string conversationId, DirectMessageSendRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            string me = _Module.Policy.RequireCaller(actor);
            ConversationEntry entry = await _Module.Conversations.RequireEntryAsync(me, conversationId, token).ConfigureAwait(false);
            return await SendInAsync(actor, entry, request, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Validate a message body before a conversation is created for it (so a refused first message never leaves an
        /// empty conversation behind).
        /// </summary>
        /// <param name="request">Message.</param>
        /// <exception cref="ArgumentNullException">The request is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or MessageEmpty.</exception>
        public void Validate(DirectMessageSendRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            Normalize(request, out string _, out string _);
        }

        /// <summary>
        /// Send a message in a conversation the caller belongs to (shared by the send and start routes).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="entry">Conversation from the caller's side.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The message and whether it was created.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">See <see cref="SendAsync"/>.</exception>
        public async Task<MessageSendResult> SendInAsync(ContentActor actor, ConversationEntry entry, DirectMessageSendRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentNullException.ThrowIfNull(request);
            string me = actor.RequireUserId();
            Stopwatch watch = Stopwatch.StartNew();
            using (Activity? activity = TrellisKitTelemetry.StartSpan("messaging.send"))
            {
                activity?.SetTag("conversation.id", entry.Conversation.Id);
                Normalize(request, out string clientMessageId, out string body);
                List<string> mediaIds = (request.MediaIds ?? new List<string>()).Select(i => (i ?? String.Empty).Trim()).ToList();
                Message? repeat = await _Services.Database.Messages.ReadByClientIdAsync(entry.Conversation.Id, me, clientMessageId, token).ConfigureAwait(false);
                if (repeat != null)
                {
                    MessagingTelemetry.Message("send", "idempotent", repeat.AttachmentCount > 0);
                    return new MessageSendResult { Message = await ViewAsync(actor, entry, repeat, token).ConfigureAwait(false), Created = false };
                }
                ConversationMember? other = entry.Others.FirstOrDefault(o => o.LeftUtc == null);
                if (other == null) throw _Module.Policy.Refusal(MessagingEligibilityEnum.RecipientUnavailable);
                MessagingPeer peer = await _Module.Policy.PeerAsync(me, other.UserId, token).ConfigureAwait(false);
                MessagingEligibilityEnum outcome = _Module.Policy.Evaluate(actor, peer, entry);
                if (outcome != MessagingEligibilityEnum.Allowed) throw _Module.Policy.Refusal(outcome);
                int limit = _Services.Settings.Messaging.RequestMessageLimit;
                if (entry.Mine.RequestState == ConversationRequestStateEnum.AwaitingAcceptance && entry.Mine.RequestMessageCount >= limit) throw RequestLimit(limit);
                _Services.Limits.EnforceMember(actor, SendMinuteBucket, _Services.Settings.Messaging.MessagesPerMinute, RateLimitService.Minute);
                _Services.Limits.EnforceMember(actor, SendDayBucket, _Services.Settings.Messaging.MessagesPerDay, RateLimitService.Day);
                // Block terms reject; Flag terms do not apply to direct messages (no system reports from private text).
                await _Services.WordFilter.CheckAsync(actor, TargetTypeEnum.Message, new Dictionary<string, string?> { { "body", body } }, token).ConfigureAwait(false);
                List<MediaAttachment> media = await ValidateMediaAsync(actor, mediaIds, entry.Conversation.Id, token).ConfigureAwait(false);
                string? replyTo = String.IsNullOrWhiteSpace(request.ReplyToMessageId) ? null : request.ReplyToMessageId.Trim();
                if (replyTo != null)
                {
                    Message? quoted = await _Services.Database.Messages.ReadAsync(replyTo, token).ConfigureAwait(false);
                    if (quoted == null || quoted.ConversationId != entry.Conversation.Id || quoted.Seq <= entry.Mine.HiddenBeforeSeq)
                        throw ApiErrorException.Field("replyToMessageId", "Must be a message in this conversation.");
                }
                DateTime now = _Module.Clock();
                MessageDraft draft = new MessageDraft
                {
                    Id = IdGenerator.Generate(MessagingIdPrefixes.Message),
                    ConversationId = entry.Conversation.Id,
                    SenderUserId = me,
                    ClientMessageId = clientMessageId,
                    Body = body,
                    Excerpt = MessagingViewBuilder.Excerpt(body),
                    ReplyToMessageId = replyTo,
                    ShowLinkPreview = request.ShowLinkPreview,
                    LinkPreviewId = await _Services.LinkPreviews.ResolveForBodyAsync(body, request.ShowLinkPreview, token).ConfigureAwait(false),
                    MediaIds = media.Select(m => m.Id).ToList(),
                    HasVideo = media.Any(m => m.Kind == MediaKindEnum.Video),
                    RequestMessageLimit = limit,
                    NowUtc = now
                };
                List<string> memberIds = new[] { me }.Concat(entry.Others.Select(o => o.UserId)).ToList();
                RealtimeEvent created = _Module.Event(RealtimeEventKinds.MessageCreated, entry.Conversation.Id, memberIds);
                created.MessageId = draft.Id;
                created.ActorUserId = me;
                string? notify = _Services.RealtimeBus.NotifyStatement(created);
                MessageSendOutcome result = await _Services.Database.Messages.SendAsync(draft, notify, token).ConfigureAwait(false);
                if (result.LimitReached || result.Message == null) throw RequestLimit(limit);
                Message message = result.Message;
                if (!result.Created)
                {
                    MessagingTelemetry.Message("send", "idempotent", message.AttachmentCount > 0);
                    return new MessageSendResult { Message = await ViewAsync(actor, entry, message, token).ConfigureAwait(false), Created = false };
                }
                if (notify == null)
                {
                    created.Seq = message.Seq;
                    await _Module.PublishAsync(created, token).ConfigureAwait(false);
                }
                await AttachMediaAsync(message, token).ConfigureAwait(false);
                bool accepted = entry.Mine.RequestState == ConversationRequestStateEnum.Pending || entry.Mine.RequestState == ConversationRequestStateEnum.Declined;
                if (accepted)
                {
                    MessagingTelemetry.Conversation("accept", "reply");
                    await _Module.PublishAsync(_Module.Event(RealtimeEventKinds.ConversationUpdated, entry.Conversation.Id, memberIds), token).ConfigureAwait(false);
                }
                if (other.RequestState == ConversationRequestStateEnum.Pending) await NotifyRequestAsync(other.UserId, me, entry.Conversation.Id, token).ConfigureAwait(false);
                try
                {
                    await _Module.Push.AfterSendAsync(entry, message, token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _Services.Logger.Warn("[MessageService] push decision failed for " + message.Id + ": " + e.GetType().Name);
                }
                MessagingTelemetry.Message("send", "ok", message.AttachmentCount > 0);
                TrellisKitTelemetry.Record(MessagingTelemetry.SendDuration, watch.Elapsed.TotalSeconds);
                activity?.SetStatus(ActivityStatusCode.Ok);
                ConversationEntry fresh = await _Services.Database.Conversations.ReadEntryAsync(entry.Conversation.Id, me, token).ConfigureAwait(false) ?? entry;
                return new MessageSendResult { Message = await ViewAsync(actor, fresh, message, token).ConfigureAwait(false), Created = true };
            }
        }

        /// <summary>
        /// PUT /api/v1/messages/{id}: one edit by the author inside messaging.editWindowMinutes; the previous text is kept as
        /// the original body.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="messageId">Message identifier.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The edited message.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, MessageEmpty, ContentFiltered; 403 Forbidden; 404 NotFound; 409 EditWindowClosed; 429 RateLimited.</exception>
        public async Task<DirectMessageView> EditAsync(ContentActor actor, string messageId, MessageEditRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            string me = _Module.Policy.RequireCaller(actor);
            MessageContext context = await RequireMessageAsync(me, messageId, token).ConfigureAwait(false);
            Message message = context.Message;
            if (message.AuthorUserId != me) throw ApiErrorException.Forbidden("Only the author can edit this message.");
            if (message.State == MessageStateEnum.Deleted) throw ApiErrorException.NotFound("Message");
            _Services.Limits.EnforceMember(actor, EditBucket, _Services.Settings.Messaging.EditsPerMinute, RateLimitService.Minute);
            string body = NormalizeBody(request.Body);
            if (body.Length == 0 && message.AttachmentCount == 0) throw new ApiErrorException(400, "MessageEmpty", "A message needs text or an attachment.");
            int window = _Services.Settings.Messaging.EditWindowMinutes;
            DateTime now = _Module.Clock();
            DateTime since = now.AddMinutes(-window);
            if (window <= 0 || message.State != MessageStateEnum.Visible || message.EditedUtc != null || message.CreatedUtc < since) throw EditClosed(window);
            await _Services.WordFilter.CheckAsync(actor, TargetTypeEnum.Message, new Dictionary<string, string?> { { "body", body } }, token).ConfigureAwait(false);
            string? preview = await _Services.LinkPreviews.ResolveForBodyAsync(body, message.ShowLinkPreview, token).ConfigureAwait(false);
            Message? edited = await _Services.Database.Messages.EditAsync(message.Id, me, body, MessagingViewBuilder.Excerpt(body), preview, since, now, token).ConfigureAwait(false);
            if (edited == null) throw EditClosed(window);
            await PublishMessageAsync(RealtimeEventKinds.MessageUpdated, context.Entry, edited, token).ConfigureAwait(false);
            MessagingTelemetry.Message("edit", "ok", edited.AttachmentCount > 0);
            return await ViewAsync(actor, context.Entry, edited, token).ConfigureAwait(false);
        }

        /// <summary>
        /// DELETE /api/v1/messages/{id}: delete for everyone (author, any time). Members see a tombstone; the content is kept
        /// internally for moderation. Idempotent.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="messageId">Message identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The tombstone.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden (not the author), 404 NotFound, 429 RateLimited.</exception>
        public async Task<DirectMessageView> DeleteAsync(ContentActor actor, string messageId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string me = _Module.Policy.RequireCaller(actor);
            MessageContext context = await RequireMessageAsync(me, messageId, token).ConfigureAwait(false);
            Message message = context.Message;
            if (message.AuthorUserId != me) throw ApiErrorException.Forbidden("Only the author can delete this message.");
            if (message.State == MessageStateEnum.Deleted) return await ViewAsync(actor, context.Entry, message, token).ConfigureAwait(false);
            _Services.Limits.EnforceMember(actor, EditBucket, _Services.Settings.Messaging.EditsPerMinute, RateLimitService.Minute);
            Message deleted = await _Services.Database.Messages.DeleteForEveryoneAsync(message.Id, me, _Module.Clock(), token).ConfigureAwait(false)
                ?? await _Services.Database.Messages.ReadAsync(message.Id, token).ConfigureAwait(false) ?? message;
            await PublishMessageAsync(RealtimeEventKinds.MessageDeleted, context.Entry, deleted, token).ConfigureAwait(false);
            MessagingTelemetry.Message("delete", "ok", message.AttachmentCount > 0);
            return await ViewAsync(actor, context.Entry, deleted, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/conversations/{id}/messages/enumerate: history (section 3.4 and as-built note B). Also moves the
        /// caller's delivery position to the newest message returned.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <param name="query">Query, or null for defaults.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 404 NotFound, 429 RateLimited.</exception>
        public async Task<EnumerationResult<DirectMessageView>> HistoryAsync(ContentActor actor, string conversationId, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string me = _Module.Policy.RequireCaller(actor);
            ConversationEntry entry = await _Module.Conversations.RequireEntryAsync(me, conversationId, token).ConfigureAwait(false);
            _Services.Limits.EnforceMember(actor, ReadBucket, _Services.Settings.Messaging.ReadsPerMinute, RateLimitService.Minute);
            EnumerationQuery q = query ?? new EnumerationQuery();
            if (q.MaxResults == null) q.MaxResults = DefaultHistoryPage;
            Dictionary<string, string> errors = new Dictionary<string, string>();
            long? beforeSeq = q.FilterLong("beforeSeq", errors);
            long? afterSeq = q.FilterLong("afterSeq", errors);
            long? aroundSeq = q.FilterLong("aroundSeq", errors);
            string? search = String.IsNullOrWhiteSpace(q.Search) ? null : q.Search.Trim();
            if (search != null && search.Length < MinSearchLength) errors["search"] = "Use " + MinSearchLength + " to 200 characters.";
            if (aroundSeq != null && (search != null || beforeSeq != null || afterSeq != null)) errors["filters.aroundSeq"] = "aroundSeq cannot be combined with search, beforeSeq, or afterSeq.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            if (search != null) q.Ordering = "SeqDescending";
            EnumerationRequest request = _Services.Enumeration.Prepare(q, MessagingCollections.History, false, "messages|" + entry.Conversation.Id + "|" + me, 100);
            EnumerationResult<Message> page;
            if (aroundSeq != null)
            {
                page = await _Services.Database.Messages.AroundAsync(entry.Conversation.Id, entry.Mine.HiddenBeforeSeq, aroundSeq.Value, 50, request, token).ConfigureAwait(false);
            }
            else
            {
                page = await _Services.Database.Messages.EnumerateHistoryAsync(entry.Conversation.Id, entry.Mine.HiddenBeforeSeq, request, beforeSeq, afterSeq, search, token).ConfigureAwait(false);
            }
            if (page.Objects.Count > 0)
            {
                long newest = page.Objects.Max(m => m.Seq);
                if (newest > entry.Mine.LastDeliveredSeq) await _Module.Conversations.DeliveredCoreAsync(entry, newest, token).ConfigureAwait(false);
            }
            Dictionary<string, ConversationEntry> entries = new Dictionary<string, ConversationEntry>(StringComparer.Ordinal) { { entry.Conversation.Id, entry } };
            return page.With(await _Module.Views.MessagesAsync(actor, entries, page.Objects, token).ConfigureAwait(false));
        }

        /// <summary>
        /// POST /api/v1/messages/search/enumerate (as-built note B): full text search over the caller's own conversations.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query (search required, 2 to 200 characters).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 429 RateLimited.</exception>
        public async Task<EnumerationResult<MessageSearchHit>> SearchAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string me = _Module.Policy.RequireCaller(actor);
            EnumerationQuery q = query ?? new EnumerationQuery();
            string? search = String.IsNullOrWhiteSpace(q.Search) ? null : q.Search.Trim();
            if (search == null || search.Length < MinSearchLength) throw ApiErrorException.Field("search", "Required: " + MinSearchLength + " to 200 characters.");
            _Services.Limits.EnforceMember(actor, ReadBucket, _Services.Settings.Messaging.ReadsPerMinute, RateLimitService.Minute);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string? conversationId = q.FilterString("conversationId");
            string? withUserId = q.FilterString("withUserId");
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, MessagingCollections.Search, false, "message-search|" + me, _Services.Settings.Messaging.SearchMaxResults);
            EnumerationResult<MessageSearchRow> page = await _Services.Database.Messages.SearchAsync(me, request, conversationId, withUserId, token).ConfigureAwait(false);
            Dictionary<string, ConversationEntry> entries = await _Services.Database.Conversations.ReadEntriesAsync(me, page.Objects.Select(o => o.Message.ConversationId), token).ConfigureAwait(false);
            List<DirectMessageView> views = await _Module.Views.MessagesAsync(actor, entries, page.Objects.Select(o => o.Message).ToList(), token).ConfigureAwait(false);
            Dictionary<string, DirectMessageView> byId = views.ToDictionary(v => v.Id, StringComparer.Ordinal);
            Dictionary<string, UserSummary> others = await _Services.Summaries.BuildAsync(entries.Values.SelectMany(e => e.Others).Select(o => (string?)o.UserId), token).ConfigureAwait(false);
            List<MessageSearchHit> hits = new List<MessageSearchHit>();
            foreach (MessageSearchRow row in page.Objects)
            {
                if (!byId.TryGetValue(row.Message.Id, out DirectMessageView? view) || !entries.TryGetValue(row.Message.ConversationId, out ConversationEntry? entry)) continue;
                ConversationMember? other = entry.Others.FirstOrDefault();
                hits.Add(new MessageSearchHit
                {
                    Message = view,
                    ConversationId = row.Message.ConversationId,
                    OtherMember = other != null && others.TryGetValue(other.UserId, out UserSummary? s) ? s : UserSummary.Deleted(),
                    Headline = row.Headline
                });
            }
            return page.With(hits);
        }

        /// <summary>
        /// The reaction target resolver for direct messages (registered in ReactionService.ExtraTargets): members of the
        /// conversation only, Visible messages for writes, and no reactions across blocks (403 MessagingNotAllowed). The
        /// returned author is the caller, so reactions on direct messages never create Reaction notifications.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Message identifier.</param>
        /// <param name="forWrite">Whether the caller adds or removes a reaction.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Target, or null for other target types.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">404 NotFound, 403 MessagingNotAllowed.</exception>
        public async Task<ReactionTarget?> ReactionTargetAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, bool forWrite, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (targetType != TargetTypeEnum.Message) return null;
            string me = _Module.Policy.RequireCaller(actor);
            MessageContext context = await RequireMessageAsync(me, targetId, token).ConfigureAwait(false);
            if (forWrite)
            {
                if (context.Message.State != MessageStateEnum.Visible) throw ApiErrorException.NotFound("Message");
                HashSet<string> related = await _Services.Blocks.RelatedAsync(me, token).ConfigureAwait(false);
                if (context.Entry.Others.Any(o => related.Contains(o.UserId)))
                {
                    MessagingTelemetry.Deny("block_or_policy");
                    throw new ApiErrorException(403, "MessagingNotAllowed", "You can't react in this conversation.");
                }
            }
            return new ReactionTarget { AuthorUserId = me, Excerpt = null };
        }

        /// <summary>
        /// After a reaction change on a message: recompute the stored total and publish message.updated.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="messageId">Message identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task AfterReactionAsync(ContentActor actor, string messageId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string me = actor.RequireUserId();
            MessageContext context = await RequireMessageAsync(me, messageId, token).ConfigureAwait(false);
            await _Services.Database.Messages.RecomputeReactionTotalAsync(messageId, _Module.Clock(), token).ConfigureAwait(false);
            await PublishMessageAsync(RealtimeEventKinds.MessageUpdated, context.Entry, context.Message, token).ConfigureAwait(false);
            MessagingTelemetry.Message("react", "ok", context.Message.AttachmentCount > 0);
        }

        /// <summary>
        /// A message with its conversation from a member's side: 404 when the message does not exist, the user is not a
        /// member, the conversation was deleted for the user, or the message is in the user's hidden range.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="messageId">Message identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Context.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<MessageContext> RequireMessageAsync(string userId, string messageId, CancellationToken token = default)
        {
            Message? message = String.IsNullOrWhiteSpace(messageId) ? null : await _Services.Database.Messages.ReadAsync(messageId.Trim(), token).ConfigureAwait(false);
            if (message == null) throw ApiErrorException.NotFound("Message");
            ConversationEntry entry = await _Module.Conversations.RequireEntryAsync(userId, message.ConversationId, token).ConfigureAwait(false);
            if (message.Seq <= entry.Mine.HiddenBeforeSeq) throw ApiErrorException.NotFound("Message");
            return new MessageContext { Message = message, Entry = entry };
        }

        /// <summary>
        /// One message payload for a viewer.
        /// </summary>
        /// <param name="actor">Viewer.</param>
        /// <param name="entry">Conversation from the viewer's side.</param>
        /// <param name="message">Message.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Payload.</returns>
        public async Task<DirectMessageView> ViewAsync(ContentActor actor, ConversationEntry entry, Message message, CancellationToken token = default)
        {
            Dictionary<string, ConversationEntry> entries = new Dictionary<string, ConversationEntry>(StringComparer.Ordinal) { { entry.Conversation.Id, entry } };
            List<DirectMessageView> views = await _Module.Views.MessagesAsync(actor, entries, new List<Message> { message }, token).ConfigureAwait(false);
            return views[0];
        }

        /// <summary>
        /// Publish a message event to every member of the conversation.
        /// </summary>
        /// <param name="kind">Event kind.</param>
        /// <param name="entry">Conversation.</param>
        /// <param name="message">Message.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public Task PublishMessageAsync(string kind, ConversationEntry entry, Message message, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentNullException.ThrowIfNull(message);
            RealtimeEvent evt = _Module.Event(kind, entry.Conversation.Id, new[] { entry.Mine.UserId }.Concat(entry.Others.Select(o => o.UserId)));
            evt.MessageId = message.Id;
            evt.Seq = message.Seq;
            evt.ActorUserId = message.AuthorUserId;
            return _Module.PublishAsync(evt, token);
        }

        #endregion

        #region Private-Methods

        private void Normalize(DirectMessageSendRequest request, out string clientMessageId, out string body)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            clientMessageId = (request.ClientMessageId ?? String.Empty).Trim();
            if (clientMessageId.Length == 0) errors["clientMessageId"] = "Required: a client generated UUID reused on every retry.";
            else if (clientMessageId.Length > MaxClientMessageIdLength) errors["clientMessageId"] = "At most " + MaxClientMessageIdLength + " characters.";
            body = String.Empty;
            if (request.Body != null && !MarkdownNormalizer.IsWellFormed(request.Body)) errors["body"] = "Must be valid UTF-8 text.";
            else
            {
                body = NormalizeBody(request.Body);
                if (MarkdownNormalizer.Length(body) > _Services.Settings.Messaging.MaxBodyLength) errors["body"] = "At most " + _Services.Settings.Messaging.MaxBodyLength + " characters.";
            }
            List<string> mediaIds = request.MediaIds ?? new List<string>();
            if (mediaIds.Any(String.IsNullOrWhiteSpace)) errors["mediaIds"] = "Media ids cannot be empty.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            if (body.Length == 0 && mediaIds.Count == 0) throw new ApiErrorException(400, "MessageEmpty", "A message needs text or an attachment.");
        }

        private string NormalizeBody(string? raw)
        {
            if (raw != null && !MarkdownNormalizer.IsWellFormed(raw)) throw ApiErrorException.Field("body", "Must be valid UTF-8 text.");
            string body = MarkdownNormalizer.Normalize(raw);
            if (MarkdownNormalizer.Length(body) > _Services.Settings.Messaging.MaxBodyLength) throw ApiErrorException.Field("body", "At most " + _Services.Settings.Messaging.MaxBodyLength + " characters.");
            return body;
        }

        private async Task<List<MediaAttachment>> ValidateMediaAsync(ContentActor actor, List<string> mediaIds, string conversationId, CancellationToken token)
        {
            if (mediaIds.Count == 0) return new List<MediaAttachment>();
            int max = _Services.Settings.Messaging.MaxAttachments;
            if (!_Services.Settings.Messaging.AllowMedia || max <= 0) throw new ApiErrorException(403, "MediaNotAllowed", "Attachments are turned off for direct messages.");
            // Direct message media is private (docs/PHASE5_CONTRACT.md section 3.7): uploaded with purpose Message and scoped to
            // this conversation, never covered by the member-wide grant cookie.
            List<MediaAttachment> media = await _Services.Media.ValidatePrivateAttachableAsync(actor, mediaIds, MediaPurposeEnum.Message, conversationId, max, 1, token).ConfigureAwait(false);
            if (media.Any(m => m.Kind == MediaKindEnum.Video) && media.Count > 1) throw new ApiErrorException(400, "MediaLimitExceeded", "A message carries up to " + max + " images or one video.");
            if (media.Any(m => m.Kind != MediaKindEnum.Image && m.Kind != MediaKindEnum.Video)) throw new ApiErrorException(400, "MediaLimitExceeded", "Direct messages carry images or one video.");
            return media;
        }

        private async Task AttachMediaAsync(Message message, CancellationToken token)
        {
            if (message.MediaIds.Count == 0) return;
            try
            {
                await _Services.Media.AttachPrivateAsync(MediaPurposeEnum.Message, message.ConversationId, TargetTypeEnum.Message, message.Id, message.MediaIds, token).ConfigureAwait(false);
            }
            catch (ApiErrorException e)
            {
                _Services.Logger.Warn("[MessageService] media of " + message.Id + " could not be attached: " + e.Error);
            }
        }

        private async Task NotifyRequestAsync(string recipientUserId, string senderUserId, string conversationId, CancellationToken token)
        {
            try
            {
                await _Services.Notifications.CreateAsync(new NotificationDraft
                {
                    RecipientUserId = recipientUserId,
                    Type = NotificationTypeEnum.MessageRequest,
                    ActorUserId = senderUserId,
                    TargetType = TargetTypeEnum.Conversation,
                    TargetId = conversationId,
                    GroupKey = "MessageRequest",
                    Path = "/messages/requests",
                    QueuePush = false
                }, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Services.Logger.Warn("[MessageService] message request notification failed: " + e.GetType().Name);
            }
        }

        private static ApiErrorException RequestLimit(int limit)
        {
            MessagingTelemetry.Deny("request_limit");
            return new ApiErrorException(403, "RequestLimitReached", "You can send up to " + limit + " messages until this member accepts your request.");
        }

        private static ApiErrorException EditClosed(int window)
        {
            return new ApiErrorException(409, "EditWindowClosed", window <= 0
                ? "Messages cannot be edited in this community."
                : "A message can be edited once, within " + window + " minutes of sending.");
        }

        #endregion
    }
}
