namespace TrellisKit.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Messaging;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Direct message routes (docs/PHASE5_CONTRACT.md sections 3.1, 3.5 and as-built note B): edit once, delete for
    /// everyone, reactions, and full text search over your own conversations.
    /// </summary>
    public class MessageRoutes
    {
        #region Private-Members

        private const string _Tag = "Messages";
        private const string _Message = "Message { id, conversationId, seq, clientMessageId, author, body, originalBody, isEdited, editedUtc, attachments, linkPreview, replyTo, state, reactionCounts, reactionTotal, myReactions, labels, tags, viewer }";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public MessageRoutes(PropertyDirectory directory)
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
            string b = Constants.ApiPrefix + "/messages";
            post.Static.Add(HttpMethod.POST, b + "/search/enumerate", SearchAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Search your messages", _Tag, "Full text search over conversations you belong to (search required, 2 to 200 characters): messages you can still see, not tombstones, removed messages, or conversations with members you blocked. Relevance (default), CreatedDescending, CreatedAscending; filters conversationId, withUserId; at most messaging.searchMaxResults per page.")
                    .Body("EnumerationQuery").Enumeration("{ message: Message, conversationId, otherMember, headline }", MessagingCollections.SearchOrderings, MessagingCollections.SearchFilters, false)
                    .Resp(400, "ValidationFailed or InvalidContinuationToken").Resp(429, "RateLimited"));
            post.Parameter.Add(HttpMethod.PUT, b + "/{id}", EditAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Edit a message", _Tag, "The author may edit once within messaging.editWindowMinutes; originalBody keeps the previous text (shown as edit history).")
                    .PathParam("id", "Message identifier").Body("{ body }").Returns(_Message).Resp(400, "ValidationFailed, MessageEmpty, ContentFiltered").Resp(403, "Forbidden (not the author)").Resp(404, "NotFound").Resp(409, "EditWindowClosed").Resp(429, "RateLimited"));
            post.Parameter.Add(HttpMethod.DELETE, b + "/{id}", DeleteAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete a message for everyone", _Tag, "The author may delete any time; both members see a tombstone. Idempotent.")
                    .PathParam("id", "Message identifier").Returns(_Message).Resp(403, "Forbidden (not the author)").Resp(404, "NotFound").Resp(429, "RateLimited"));
            post.Parameter.Add(HttpMethod.PUT, b + "/{id}/reactions/{key}", ReactAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("React to a message", _Tag, "The property's reaction set. Idempotent. No reactions across blocks; never notifies.")
                    .PathParam("id", "Message identifier").PathParam("key", "Reaction key").Returns("{ reactionCounts, reactionTotal, myReactions }").Resp(400, "UnknownReaction").Resp(403, "MessagingNotAllowed or Muted").Resp(404, "NotFound").Resp(429, "RateLimited"));
            post.Parameter.Add(HttpMethod.DELETE, b + "/{id}/reactions/{key}", UnreactAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Remove a reaction from a message", _Tag, "Idempotent.")
                    .PathParam("id", "Message identifier").PathParam("key", "Reaction key").Returns("{ reactionCounts, reactionTotal, myReactions }").Resp(400, "UnknownReaction").Resp(403, "MessagingNotAllowed or Muted").Resp(404, "NotFound").Resp(429, "RateLimited"));
            post.Parameter.Add(HttpMethod.POST, b + "/{id}/reactions/enumerate", ReactionsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate who reacted to a message", _Tag, "Members of the conversation only; filter key.")
                    .PathParam("id", "Message identifier").Body("EnumerationQuery", false).Enumeration("{ id, user, key, createdUtc, labels, tags }", "CreatedDescending, CreatedAscending", "key", false)
                    .Resp(400, "ValidationFailed, UnknownReaction, or InvalidContinuationToken").Resp(404, "NotFound"));
        }

        #endregion

        #region Private-Methods

        private async Task<ContentActor> ActorAsync(HttpContextBase ctx)
        {
            return await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
        }

        private async Task SearchAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Messaging.Messages.SearchAsync(actor, RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task EditAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Messaging.Messages.EditAsync(actor, RouteHelper.Param(ctx, "id"), RouteHelper.ReadJson<MessageEditRequest>(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Messaging.Messages.DeleteAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private Task ReactAsync(HttpContextBase ctx)
        {
            return ChangeAsync(ctx, true);
        }

        private Task UnreactAsync(HttpContextBase ctx)
        {
            return ChangeAsync(ctx, false);
        }

        private async Task ChangeAsync(HttpContextBase ctx, bool add)
        {
            ApplicationServices s = _Directory.For(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            string id = RouteHelper.Param(ctx, "id");
            string key = RouteHelper.Param(ctx, "key");
            ReactionState state = add
                ? await s.Reactions.AddAsync(actor, TargetTypeEnum.Message, id, key, ctx.Token).ConfigureAwait(false)
                : await s.Reactions.RemoveAsync(actor, TargetTypeEnum.Message, id, key, ctx.Token).ConfigureAwait(false);
            await s.Messaging.Messages.AfterReactionAsync(actor, id, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, state).ConfigureAwait(false);
        }

        private async Task ReactionsAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Reactions.ListAsync(actor, TargetTypeEnum.Message, RouteHelper.Param(ctx, "id"), RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
