namespace TrellisKit.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Ai;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Member and visitor assistant routes (docs/AI_CONTRACT.md section 6): assistants the caller may use,
    /// conversations, streaming messages over SSE, cancel, regenerate, and feedback. Registered before authentication:
    /// each handler resolves the caller itself (a bearer token, or an X-TK-Visitor-Token for Public assistants) and the
    /// assistant's audience decides access (401, 402 MembershipRequired, or 404). Every route is 404 NotAvailable unless
    /// the property has features.ai and ai.enabled is on.
    /// </summary>
    public class AssistantRoutes
    {
        #region Private-Members

        private const string _Assistants = Constants.ApiPrefix + "/assistants";
        private const string _Conversations = Constants.ApiPrefix + "/assistant-conversations";
        private const string _Tag = "Assistants";
        private const string _Who = " A member bearer token, or X-TK-Visitor-Token for Public assistants (401 Unauthorized when it is missing or no longer resolves; only POST /api/v1/assistant-visitors and conversation start issue a token); 404 NotAvailable when AI is off.";
        private const string _ConversationOrderings = "LastMessageDescending, CreatedDescending, CreatedAscending";
        private const string _Conversation = "Conversation { id, assistant, title, messageCount, lastMessageUtc, lastMessagePreview, isArchived, status, surface, labels, tags }";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public AssistantRoutes(PropertyDirectory directory)
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
            WatsonWebserver.Core.Routing.RoutingGroup pre = server.Routes.PreAuthentication;
            pre.Static.Add(HttpMethod.GET, _Assistants, ListAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("List assistants", _Tag, "Enabled assistants the caller may use (audience Public for visitors; SignedIn and Members by membership)." + _Who)
                    .Enumeration("AssistantSummary", "NameAscending").Resp(401, "Unauthorized").Resp(404, "NotAvailable"));
            pre.Static.Add(HttpMethod.POST, _Assistants + "/enumerate", EnumerateAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate assistants", _Tag, "EnumerationQuery body; same result as the GET form." + _Who)
                    .Body("EnumerationQuery", false).Enumeration("AssistantSummary", "NameAscending", "", false).Resp(404, "NotAvailable"));
            pre.Static.Add(HttpMethod.POST, Constants.ApiPrefix + "/assistant-visitors", CreateVisitorAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Create a visitor session", _Tag, "For Public assistants signed out (website /assistants/{id} and the widget): returns an opaque token to send as X-TK-Visitor-Token. Rate limited per IP. Anonymous.")
                    .Returns("VisitorSession", 201).Resp(404, "NotAvailable").Resp(429, "RateLimited"));
            pre.Parameter.Add(HttpMethod.GET, _Assistants + "/{id}", GetAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read an assistant", _Tag, "AssistantSummary with the caller's remaining messages today and the privacy disclosure." + _Who)
                    .PathParam("id", "Assistant identifier").Returns("AssistantSummary").Resp(401, "Unauthorized").Resp(402, "MembershipRequired").Resp(404, "NotFound"));
            pre.Parameter.Add(HttpMethod.POST, _Assistants + "/{id}/conversations", StartAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Start a conversation", _Tag, "Body { message?, clientMessageId? }. Without message: 201 Conversation. With message: the same as sending it (SSE with Accept: text/event-stream, else 201 { userMessage, reply }). A visitor whose X-TK-Visitor-Token is missing or no longer resolves gets a new visitor and its token in the X-TK-Visitor-Token response header (the only route besides POST /assistant-visitors that issues one)." + _Who)
                    .PathParam("id", "Assistant identifier").Body("ConversationStartRequest", false).Returns("Conversation", 201)
                    .Resp(400, "ValidationFailed, MessageTooLong, ContentFiltered, MessageBlocked").Resp(401, "Unauthorized").Resp(402, "MembershipRequired").Resp(403, "Muted").Resp(404, "NotFound").Resp(429, "AssistantLimitReached or RateLimited"));
            pre.Parameter.Add(HttpMethod.POST, _Assistants + "/{id}/conversations/enumerate", EnumerateAssistantConversationsAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate my conversations with an assistant", _Tag, "The caller's own conversations; search over title and last message." + _Who)
                    .PathParam("id", "Assistant identifier").Body("EnumerationQuery", false).Enumeration(_Conversation, _ConversationOrderings, "archived", false).Resp(404, "NotFound"));
            pre.Static.Add(HttpMethod.POST, _Conversations + "/enumerate", EnumerateConversationsAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate my conversations", _Tag, "Across assistants (filters.assistantId, filters.archived)." + _Who)
                    .Body("EnumerationQuery", false).Enumeration(_Conversation, _ConversationOrderings, "assistantId, archived", false).Resp(404, "NotAvailable"));
            pre.Parameter.Add(HttpMethod.GET, _Conversations + "/{id}", GetConversationAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a conversation", _Tag, "With recentMessages (at most 20, newest last, and the enumerate path)." + _Who)
                    .PathParam("id", "Conversation identifier").Returns("Conversation").Resp(404, "NotFound"));
            pre.Parameter.Add(HttpMethod.PUT, _Conversations + "/{id}", UpdateConversationAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Update a conversation", _Tag, "Body { title?, isArchived?, labels?, tags? }. Rename, archive, or relabel." + _Who)
                    .PathParam("id", "Conversation identifier").Body("ConversationUpdateRequest").Returns("Conversation").Resp(400, "ValidationFailed or ContentFiltered").Resp(404, "NotFound"));
            pre.Parameter.Add(HttpMethod.DELETE, _Conversations + "/{id}", DeleteConversationAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete a conversation", _Tag, "Hard delete of its messages." + _Who).PathParam("id", "Conversation identifier").Resp(204, "Deleted").Resp(404, "NotFound"));
            pre.Parameter.Add(HttpMethod.POST, _Conversations + "/{id}/messages", SendAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Send a message", _Tag, "Body { content, clientMessageId? }. Accept: text/event-stream streams message.created, reply.started, reply.delta, reply.citations, then exactly one of reply.completed, reply.blocked, or error; otherwise 201 { userMessage, reply } after the reply completes. clientMessageId makes a repeat within 24 hours return the stored exchange." + _Who)
                    .PathParam("id", "Conversation identifier").Body("MessageSendRequest").File("text/event-stream", "SSE stream of chat events").Returns("MessageExchange", 201)
                    .Resp(400, "ValidationFailed, MessageTooLong, ContentFiltered, MessageBlocked").Resp(403, "Muted").Resp(404, "NotFound").Resp(409, "ReplyInProgress or ConversationFull")
                    .Resp(429, "AssistantLimitReached or RateLimited").Resp(503, "BudgetExceeded"));
            pre.Parameter.Add(HttpMethod.POST, _Conversations + "/{id}/messages/enumerate", EnumerateMessagesAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate messages", _Tag, "Oldest first by default." + _Who)
                    .PathParam("id", "Conversation identifier").Body("EnumerationQuery", false).Enumeration("AssistantMessage", "CreatedAscending, CreatedDescending", "", false).Resp(404, "NotFound"));
            pre.Parameter.Add(HttpMethod.GET, _Conversations + "/{id}/messages/{messageId}", GetMessageAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a message", _Tag, "Used to resume after a dropped stream (poll until status is final)." + _Who)
                    .PathParam("id", "Conversation identifier").PathParam("messageId", "Message identifier").Returns("AssistantMessage").Resp(404, "NotFound"));
            pre.Parameter.Add(HttpMethod.POST, _Conversations + "/{id}/messages/{messageId}/cancel", CancelAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Stop a reply", _Tag, "Partial content is kept with status Cancelled." + _Who)
                    .PathParam("id", "Conversation identifier").PathParam("messageId", "Reply identifier").Returns("AssistantMessage").Resp(404, "NotFound").Resp(409, "Conflict (already finished)"));
            pre.Parameter.Add(HttpMethod.POST, _Conversations + "/{id}/messages/{messageId}/regenerate", RegenerateAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Regenerate the latest reply", _Tag, "Same response as sending; the old reply is kept with isSuperseded true." + _Who)
                    .PathParam("id", "Conversation identifier").PathParam("messageId", "Reply identifier").File("text/event-stream", "SSE stream of chat events").Returns("MessageExchange", 201)
                    .Resp(404, "NotFound").Resp(409, "Conflict or ReplyInProgress").Resp(429, "RateLimited"));
            pre.Parameter.Add(HttpMethod.PUT, _Conversations + "/{id}/messages/{messageId}/feedback", SetFeedbackAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Rate a reply", _Tag, "Body { rating: Up | Down, note?, reasons? }. Giving feedback shares the rated question and reply with administrators." + _Who)
                    .PathParam("id", "Conversation identifier").PathParam("messageId", "Reply identifier").Body("MessageFeedbackRequest").Returns("AssistantMessage")
                    .Resp(400, "ValidationFailed or ContentFiltered").Resp(404, "NotFound").Resp(429, "RateLimited"));
            pre.Parameter.Add(HttpMethod.DELETE, _Conversations + "/{id}/messages/{messageId}/feedback", ClearFeedbackAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Remove my rating", _Tag, _Who.Trim()).PathParam("id", "Conversation identifier").PathParam("messageId", "Reply identifier").Returns("AssistantMessage").Resp(404, "NotFound"));
        }

        #endregion

        #region Private-Methods

        private async Task ListAsync(HttpContextBase ctx)
        {
            ApplicationServices s = _Directory.For(ctx);
            AssistantCaller caller = await AiRouteHelper.CallerAsync(ctx, s, true, false).ConfigureAwait(false);
            EnumerationQuery query = RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await s.Ai.Assistants.EnumerateForCallerAsync(caller, query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task EnumerateAsync(HttpContextBase ctx)
        {
            ApplicationServices s = _Directory.For(ctx);
            AssistantCaller caller = await AiRouteHelper.CallerAsync(ctx, s, true, false).ConfigureAwait(false);
            EnumerationQuery query = RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await s.Ai.Assistants.EnumerateForCallerAsync(caller, query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task CreateVisitorAsync(HttpContextBase ctx)
        {
            ApplicationServices s = _Directory.For(ctx);
            RequestInfo info = RequestInfo.From(ctx, s.Settings.Rest);
            VisitorSessionView session = await s.Ai.Visitors.CreateAsync(info.SourceIp, AiRouteHelper.Surface(ctx), ctx.Token).ConfigureAwait(false);
            ctx.Response.Headers[AiConstants.VisitorTokenHeader] = session.VisitorToken;
            ctx.Response.Headers["Cache-Control"] = "no-store";
            await RouteHelper.SendJsonAsync(ctx, 201, session).ConfigureAwait(false);
        }

        private async Task GetAsync(HttpContextBase ctx)
        {
            ApplicationServices s = _Directory.For(ctx);
            AssistantCaller caller = await AiRouteHelper.CallerAsync(ctx, s, true, false).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await s.Ai.Assistants.GetForCallerAsync(caller, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task StartAsync(HttpContextBase ctx)
        {
            ApplicationServices s = _Directory.For(ctx);
            ConversationStartRequest request = RouteHelper.ReadJson<ConversationStartRequest>(ctx);
            AssistantCaller caller = await AiRouteHelper.CallerAsync(ctx, s, true, true).ConfigureAwait(false);
            Assistant assistant = await s.Ai.Assistants.RequireUsableAsync(caller, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(request.Message))
            {
                AssistantConversation created = await s.Ai.Conversations.CreateAsync(caller, assistant, ctx.Token).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 201, await s.Ai.Conversations.ViewAsync(created, false, false, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
                return;
            }
            AssistantConversation conversation = await s.Ai.Conversations.CreateAsync(caller, assistant, ctx.Token).ConfigureAwait(false);
            PreparedExchange prepared;
            try
            {
                prepared = await s.Ai.Chat.PrepareFirstAsync(caller, assistant, conversation, new MessageSendRequest { Content = request.Message, ClientMessageId = request.ClientMessageId }, ctx.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The message was rejected: do not leave an empty conversation behind.
                await s.Database.Ai.Conversations.DeleteConversationAsync(conversation.Id, ctx.Token).ConfigureAwait(false);
                throw;
            }
            await AiRouteHelper.SendExchangeAsync(ctx, s, prepared).ConfigureAwait(false);
        }

        private async Task EnumerateAssistantConversationsAsync(HttpContextBase ctx)
        {
            ApplicationServices s = _Directory.For(ctx);
            AssistantCaller caller = await AiRouteHelper.CallerAsync(ctx, s, true, false).ConfigureAwait(false);
            Assistant assistant = await s.Ai.Assistants.RequireUsableAsync(caller, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            EnumerationQuery query = RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await s.Ai.Conversations.EnumerateAsync(caller, assistant.Id, query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task EnumerateConversationsAsync(HttpContextBase ctx)
        {
            ApplicationServices s = _Directory.For(ctx);
            AssistantCaller caller = await AiRouteHelper.CallerAsync(ctx, s, true, false).ConfigureAwait(false);
            EnumerationQuery query = RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await s.Ai.Conversations.EnumerateAsync(caller, null, query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GetConversationAsync(HttpContextBase ctx)
        {
            ApplicationServices s = _Directory.For(ctx);
            AssistantCaller caller = await AiRouteHelper.CallerAsync(ctx, s, true, false).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await s.Ai.Conversations.GetAsync(caller, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateConversationAsync(HttpContextBase ctx)
        {
            ApplicationServices s = _Directory.For(ctx);
            ConversationUpdateRequest request = RouteHelper.ReadJson<ConversationUpdateRequest>(ctx);
            AssistantCaller caller = await AiRouteHelper.CallerAsync(ctx, s, true, false).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await s.Ai.Conversations.UpdateAsync(caller, RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteConversationAsync(HttpContextBase ctx)
        {
            ApplicationServices s = _Directory.For(ctx);
            AssistantCaller caller = await AiRouteHelper.CallerAsync(ctx, s, true, false).ConfigureAwait(false);
            await s.Ai.Conversations.DeleteAsync(caller, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task SendAsync(HttpContextBase ctx)
        {
            ApplicationServices s = _Directory.For(ctx);
            MessageSendRequest request = RouteHelper.ReadJson<MessageSendRequest>(ctx);
            AssistantCaller caller = await AiRouteHelper.CallerAsync(ctx, s, true, false).ConfigureAwait(false);
            PreparedExchange prepared = await s.Ai.Chat.PrepareAsync(caller, RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false);
            await AiRouteHelper.SendExchangeAsync(ctx, s, prepared).ConfigureAwait(false);
        }

        private async Task EnumerateMessagesAsync(HttpContextBase ctx)
        {
            ApplicationServices s = _Directory.For(ctx);
            AssistantCaller caller = await AiRouteHelper.CallerAsync(ctx, s, true, false).ConfigureAwait(false);
            EnumerationQuery query = RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await s.Ai.Conversations.EnumerateMessagesAsync(caller, RouteHelper.Param(ctx, "id"), query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GetMessageAsync(HttpContextBase ctx)
        {
            ApplicationServices s = _Directory.For(ctx);
            AssistantCaller caller = await AiRouteHelper.CallerAsync(ctx, s, true, false).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await s.Ai.Conversations.GetMessageAsync(caller, RouteHelper.Param(ctx, "id"), RouteHelper.Param(ctx, "messageId"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task CancelAsync(HttpContextBase ctx)
        {
            ApplicationServices s = _Directory.For(ctx);
            AssistantCaller caller = await AiRouteHelper.CallerAsync(ctx, s, true, false).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await s.Ai.Chat.CancelAsync(caller, RouteHelper.Param(ctx, "id"), RouteHelper.Param(ctx, "messageId"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task RegenerateAsync(HttpContextBase ctx)
        {
            ApplicationServices s = _Directory.For(ctx);
            AssistantCaller caller = await AiRouteHelper.CallerAsync(ctx, s, true, false).ConfigureAwait(false);
            PreparedExchange prepared = await s.Ai.Chat.PrepareRegenerateAsync(caller, RouteHelper.Param(ctx, "id"), RouteHelper.Param(ctx, "messageId"), ctx.Token).ConfigureAwait(false);
            await AiRouteHelper.SendExchangeAsync(ctx, s, prepared).ConfigureAwait(false);
        }

        private async Task SetFeedbackAsync(HttpContextBase ctx)
        {
            ApplicationServices s = _Directory.For(ctx);
            MessageFeedbackRequest request = RouteHelper.ReadJson<MessageFeedbackRequest>(ctx);
            AssistantCaller caller = await AiRouteHelper.CallerAsync(ctx, s, true, false).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await s.Ai.Conversations.SetFeedbackAsync(caller, RouteHelper.Param(ctx, "id"), RouteHelper.Param(ctx, "messageId"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ClearFeedbackAsync(HttpContextBase ctx)
        {
            ApplicationServices s = _Directory.For(ctx);
            AssistantCaller caller = await AiRouteHelper.CallerAsync(ctx, s, true, false).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await s.Ai.Conversations.ClearFeedbackAsync(caller, RouteHelper.Param(ctx, "id"), RouteHelper.Param(ctx, "messageId"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
