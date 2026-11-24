namespace TrellisKit.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// The caller's messaging account routes (docs/PHASE5_CONTRACT.md section 3.1): settings, unread counts, and the
    /// declined requests list for undo. These are /me routes (signed in); unread counts and declined requests still need
    /// a membership where the access model gates messaging (402 MembershipRequired).
    /// </summary>
    public class MessagingSettingsRoutes
    {
        #region Private-Members

        private const string _Tag = "Messages";
        private const string _Settings = "{ dmPolicy, readReceipts, typingIndicators, pushPreview, exemptFromNotificationHours, updatedUtc }";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public MessagingSettingsRoutes(PropertyDirectory directory)
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
            string b = Constants.ApiPrefix + "/me/messaging";
            post.Static.Add(HttpMethod.GET, b + "/settings", GetSettingsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Your messaging settings", _Tag, "Defaults: dmPolicy messaging.defaultDmPolicy, readReceipts and typingIndicators true, pushPreview messaging.defaultPushPreview, exemptFromNotificationHours false. Signed in.").Returns(_Settings));
            post.Static.Add(HttpMethod.PUT, b + "/settings", PutSettingsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Change your messaging settings", _Tag, "Partial: omitted fields keep their value. dmPolicy Everyone, Known, or Nobody; pushPreview SenderOnly, Full, or Hidden; exemptFromNotificationHours lets message pushes arrive outside your notification hours. Signed in.")
                    .Body("{ dmPolicy?, readReceipts?, typingIndicators?, pushPreview?, exemptFromNotificationHours? }").Returns(_Settings).Resp(400, "ValidationFailed"));
            post.Static.Add(HttpMethod.GET, b + "/unread-count", UnreadAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Your unread message counts", _Tag, "Conversations with unread messages and unread messages (not requests), and pending requests.").Returns("{ conversations, messages, requests }").Resp(402, "MembershipRequired"));
            post.Static.Add(HttpMethod.GET, b + "/declined", DeclinedListAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Requests you declined", _Tag, "Newest decline first; undo with POST /api/v1/conversations/{id}/accept.")
                    .Enumeration("Conversation", "DeclinedDescending, CreatedDescending, CreatedAscending").Resp(400, "ValidationFailed or InvalidContinuationToken").Resp(402, "MembershipRequired"));
            post.Static.Add(HttpMethod.POST, b + "/declined/enumerate", DeclinedEnumerateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate requests you declined", _Tag, "EnumerationQuery body; same result as the GET form.")
                    .Body("EnumerationQuery", false).Enumeration("Conversation", "DeclinedDescending, CreatedDescending, CreatedAscending", String.Empty, false).Resp(400, "ValidationFailed or InvalidContinuationToken").Resp(402, "MembershipRequired"));
        }

        #endregion

        #region Private-Methods

        private async Task<ContentActor> ActorAsync(HttpContextBase ctx)
        {
            return await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
        }

        private async Task GetSettingsAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Messaging.Conversations.GetSettingsAsync(actor, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task PutSettingsAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Messaging.Conversations.UpdateSettingsAsync(actor, RouteHelper.ReadJson<MessagingSettingsRequest>(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UnreadAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Messaging.Conversations.UnreadCountAsync(actor, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private Task DeclinedListAsync(HttpContextBase ctx)
        {
            return DeclinedAsync(ctx, RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx));
        }

        private Task DeclinedEnumerateAsync(HttpContextBase ctx)
        {
            return DeclinedAsync(ctx, RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx));
        }

        private async Task DeclinedAsync(HttpContextBase ctx, EnumerationQuery query)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Messaging.Conversations.DeclinedAsync(actor, query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
