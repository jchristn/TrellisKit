namespace TrellisKit.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Messaging;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Direct conversations (docs/PHASE5_CONTRACT.md sections 3.1, 3.2, 5.2): lists, start, read, history, send, read and
    /// delivered positions, requests, folders, mutes, delete for me, and the sync of changes. Messaging feature routes:
    /// never signed out; in gated models they need a membership (resolved decision 26).
    /// </summary>
    public class ConversationRoutes
    {
        #region Private-Members

        private const string _Tag = "Messages";
        private const string _Conversation = "Conversation { id, kind, members, otherMember, folder, requestState, lastMessage, lastSeq, myLastReadSeq, myLastDeliveredSeq, otherLastReadSeq, unreadCount, isMuted, mutedUntilUtc, memberId, labels, tags, viewer }";
        private const string _Message = "Message { id, conversationId, seq, clientMessageId, author, body, originalBody, isEdited, editedUtc, attachments, linkPreview, replyTo, state, reactionCounts, reactionTotal, myReactions, labels, tags, viewer }";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public ConversationRoutes(PropertyDirectory directory)
        {
            _Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="server">Watson server.</param>
        /// <exception cref="ArgumentNullException">The server is null.</exception>
        public void Register(Webserver server)
        {
            ArgumentNullException.ThrowIfNull(server);
            WatsonWebserver.Core.Routing.RoutingGroup post = server.Routes.PostAuthentication;
            string b = Constants.ApiPrefix + "/conversations";
            post.Static.Add(HttpMethod.GET, b, ListAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("List your conversations", _Tag, "LastMessageDescending (default) or created orderings; folder (Inbox default, Requests, Archived, All), unreadOnly, withUserId; search over the other member's display name and handle; label and tag filters apply to your own ConversationMember metadata.")
                    .Enumeration(_Conversation, MessagingCollections.ConversationOrderings, MessagingCollections.ConversationFilters).Resp(400, "ValidationFailed or InvalidContinuationToken").Resp(402, "MembershipRequired"));
            post.Static.Add(HttpMethod.POST, b + "/enumerate", EnumerateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate your conversations", _Tag, "EnumerationQuery body; same result as the GET form.")
                    .Body("EnumerationQuery", false).Enumeration(_Conversation, MessagingCollections.ConversationOrderings, MessagingCollections.ConversationFilters, false).Resp(400, "ValidationFailed or InvalidContinuationToken"));
            post.Static.Add(HttpMethod.POST, b, StartAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Start a conversation", _Tag, "One direct conversation per pair: 201 when new, 200 with the existing one (restored for you if you deleted it). A non-contact lands in the recipient's Requests. Optional first message.")
                    .Body("{ recipientUserId, message?: { clientMessageId, body, mediaIds, replyToMessageId, showLinkPreview } }").Returns("{ conversation: Conversation, message: Message or null }", 201)
                    .Resp(400, "ValidationFailed, CannotMessageSelf, MessageEmpty, ContentFiltered").Resp(403, "MessagingNotAllowed, RecipientUnavailable, MessagingRestricted, RequestLimitReached").Resp(404, "NotFound").Resp(429, "RateLimited"));
            post.Static.Add(HttpMethod.POST, b + "/changes/enumerate", ChangesAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Sync conversation changes", _Tag, "Conversations changed after syncToken, ordered by change time then id, at most 100. continuationToken pages the current retrieval and is null on its final page; the final response syncToken is the next synchronization watermark. Tokens older than messaging.syncTokenDays are rejected.")
                    .Body("ConversationChangesQuery (EnumerationQuery plus syncToken)", false).Enumeration("{ conversation: Conversation, change: Updated | Removed }", "ChangedAscending", String.Empty, false).Resp(400, "InvalidContinuationToken").Resp(429, "RateLimited"));
            post.Parameter.Add(HttpMethod.GET, b + "/{id}", GetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a conversation", _Tag, "Members only; 404 for anyone else (ids never leak).").PathParam("id", "Conversation identifier").Returns(_Conversation).Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.GET, b + "/{id}/messages", HistoryGetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Conversation history", _Tag, "SeqDescending (default) or SeqAscending; filters beforeSeq, afterSeq (gap fill), aroundSeq (no continuation); search uses full text over your own messages here. maxResults default 50, max 100. Tombstones are included; messages you deleted for yourself are not.")
                    .PathParam("id", "Conversation identifier").Enumeration(_Message, MessagingCollections.HistoryOrderings, MessagingCollections.HistoryFilters).Resp(400, "ValidationFailed or InvalidContinuationToken").Resp(404, "NotFound").Resp(429, "RateLimited"));
            post.Parameter.Add(HttpMethod.POST, b + "/{id}/messages/enumerate", HistoryPostAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate conversation history", _Tag, "EnumerationQuery body; same result as the GET form.")
                    .PathParam("id", "Conversation identifier").Body("EnumerationQuery", false).Enumeration(_Message, MessagingCollections.HistoryOrderings, MessagingCollections.HistoryFilters, false)
                    .Resp(400, "ValidationFailed or InvalidContinuationToken").Resp(404, "NotFound").Resp(429, "RateLimited"));
            post.Parameter.Add(HttpMethod.POST, b + "/{id}/messages", SendAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Send a message", _Tag, "Idempotent per clientMessageId: a retry returns 200 with the stored message. Plain text up to messaging.maxBodyLength; up to messaging.maxAttachments images or one video.")
                    .PathParam("id", "Conversation identifier").Body("{ clientMessageId, body, mediaIds, replyToMessageId, showLinkPreview }").Returns(_Message, 201)
                    .Resp(400, "ValidationFailed, MessageEmpty, ContentFiltered, MediaLimitExceeded").Resp(403, "MessagingNotAllowed, RecipientUnavailable, RequestLimitReached, MessagingRestricted, MediaNotAllowed")
                    .Resp(404, "NotFound").Resp(409, "MediaNotAttachable").Resp(429, "RateLimited"));
            post.Parameter.Add(HttpMethod.POST, b + "/{id}/read", ReadAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Mark read", _Tag, "Monotonic: a lower seq is ignored.").PathParam("id", "Conversation identifier").Body("{ seq }").Returns("{ myLastReadSeq, unreadCount }").Resp(400, "ValidationFailed").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, b + "/{id}/delivered", DeliveredAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Mark delivered", _Tag, "REST twin of the WebSocket ack; monotonic.").PathParam("id", "Conversation identifier").Body("{ seq }").Resp(204, "Recorded").Resp(400, "ValidationFailed").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, b + "/{id}/accept", AcceptAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Accept a request", _Tag, "Accepts a pending request, or undoes a decline (Settings, Messaging, Declined requests).").PathParam("id", "Conversation identifier").Returns(_Conversation).Resp(404, "NotFound").Resp(409, "NotPendingRequest"));
            post.Parameter.Add(HttpMethod.POST, b + "/{id}/decline", DeclineAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Decline a request", _Tag, "The conversation leaves your lists; the sender is not told.").PathParam("id", "Conversation identifier").Returns(_Conversation).Resp(404, "NotFound").Resp(409, "NotPendingRequest"));
            post.Parameter.Add(HttpMethod.PUT, b + "/{id}/folder", FolderAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Move to a folder", _Tag, "Inbox or Archived.").PathParam("id", "Conversation identifier").Body("{ folder }").Returns(_Conversation).Resp(400, "ValidationFailed").Resp(404, "NotFound").Resp(409, "RequestPending"));
            post.Parameter.Add(HttpMethod.PUT, b + "/{id}/mute", MuteAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Mute a conversation", _Tag, "No pushes or unread emails while muted; untilUtc null mutes indefinitely.").PathParam("id", "Conversation identifier").Body("{ untilUtc? }", false).Returns(_Conversation).Resp(400, "ValidationFailed").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.DELETE, b + "/{id}/mute", UnmuteAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Unmute a conversation", _Tag, "Idempotent.").PathParam("id", "Conversation identifier").Returns(_Conversation).Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.DELETE, b + "/{id}", DeleteAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete a conversation for yourself", _Tag, "Hides every message before now for you only; a new message from the other member brings it back with only newer messages.")
                    .PathParam("id", "Conversation identifier").Resp(204, "Deleted for you").Resp(404, "NotFound"));
        }

        #endregion

        #region Private-Methods

        private async Task<ContentActor> ActorAsync(HttpContextBase ctx)
        {
            return await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
        }

        private MessagingModule Module(HttpContextBase ctx)
        {
            return _Directory.For(ctx).Messaging;
        }

        private Task ListAsync(HttpContextBase ctx)
        {
            return PageAsync(ctx, RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx));
        }

        private Task EnumerateAsync(HttpContextBase ctx)
        {
            return PageAsync(ctx, RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx));
        }

        private async Task PageAsync(HttpContextBase ctx, EnumerationQuery query)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await Module(ctx).Conversations.EnumerateAsync(actor, query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task StartAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            ConversationStartOutcome outcome = await Module(ctx).Conversations.StartAsync(actor, RouteHelper.ReadJson<DirectConversationStartRequest>(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, outcome.Created ? 201 : 200, outcome.Response).ConfigureAwait(false);
        }

        private async Task ChangesAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await Module(ctx).Conversations.ChangesAsync(actor, RouteHelper.ReadEnumerationBody<ConversationChangesQuery>(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GetAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await Module(ctx).Conversations.GetAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private Task HistoryGetAsync(HttpContextBase ctx)
        {
            return HistoryAsync(ctx, RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx, "id"));
        }

        private Task HistoryPostAsync(HttpContextBase ctx)
        {
            return HistoryAsync(ctx, RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx));
        }

        private async Task HistoryAsync(HttpContextBase ctx, EnumerationQuery query)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await Module(ctx).Messages.HistoryAsync(actor, RouteHelper.Param(ctx, "id"), query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task SendAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            MessageSendResult result = await Module(ctx).Messages.SendAsync(actor, RouteHelper.Param(ctx, "id"), RouteHelper.ReadJson<DirectMessageSendRequest>(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, result.Created ? 201 : 200, result.Message).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await Module(ctx).Conversations.ReadAsync(actor, RouteHelper.Param(ctx, "id"), RouteHelper.ReadJson<ConversationSeqRequest>(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeliveredAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await Module(ctx).Conversations.DeliveredAsync(actor, RouteHelper.Param(ctx, "id"), RouteHelper.ReadJson<ConversationSeqRequest>(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task AcceptAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await Module(ctx).Conversations.AcceptAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeclineAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await Module(ctx).Conversations.DeclineAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task FolderAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await Module(ctx).Conversations.SetFolderAsync(actor, RouteHelper.Param(ctx, "id"), RouteHelper.ReadJson<ConversationFolderRequest>(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task MuteAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            ConversationMuteRequest request = RouteHelper.ReadJson<ConversationMuteRequest>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await Module(ctx).Conversations.MuteAsync(actor, RouteHelper.Param(ctx, "id"), true, request.UntilUtc, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UnmuteAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await Module(ctx).Conversations.MuteAsync(actor, RouteHelper.Param(ctx, "id"), false, null, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await Module(ctx).Conversations.DeleteAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        #endregion
    }
}
