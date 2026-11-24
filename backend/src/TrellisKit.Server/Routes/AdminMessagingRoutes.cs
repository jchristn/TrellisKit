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
    /// The Messaging moderation routes (docs/PHASE5_CONTRACT.md sections 3.9 and 11), for SystemAdmin and Messaging
    /// moderators only: the report queue, the audited snapshot of a report, Remove and Restore of the live message, and
    /// RevokeMessaging. No route lists, searches, or opens a conversation.
    /// </summary>
    public class AdminMessagingRoutes
    {
        #region Private-Members

        private const string _Tag = "Moderation";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public AdminMessagingRoutes(PropertyDirectory directory)
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
            string b = Constants.ApiPrefix + "/admin/moderation/messaging";
            post.Static.Add(HttpMethod.POST, b + "/reports/enumerate", QueueAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Direct message reports", _Tag, "Reports of messages and conversations (scope Messaging): filters status (Open default, ActionTaken, Dismissed) and targetType (Message, Conversation). Rows carry the snapshot id, never a link into the conversation. SystemAdmin and Messaging moderators.")
                    .Body("EnumerationQuery", false).Enumeration("{ reportId, targetType, targetId, reason, details, status, reporter, targetAuthor, snapshotId, labels, tags, createdUtc }", "CreatedDescending, CreatedAscending", "status, targetType", false)
                    .Resp(400, "ValidationFailed or InvalidContinuationToken").Resp(403, "Forbidden"));
            post.Parameter.Add(HttpMethod.GET, b + "/reports/{reportId}/snapshot", SnapshotAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Report snapshot", _Tag, "The frozen copy of the reported message and up to messaging.reportContextMessages earlier messages the reporter could see. Every view is audited (message.snapshot.view). SystemAdmin and Messaging moderators.")
                    .PathParam("reportId", "Report identifier").Bounded("{ id, reportId, conversationId, reportedMessageId, reporter, capturedUtc, messages }", "messages at most messaging.reportContextMessages + 1 (51)")
                    .Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, b + "/messages/{id}/remove", RemoveAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Remove a reported message", _Tag, "The message becomes Removed for both members; its open reports resolve as ActionTaken. Audited. Only messages with a report snapshot.")
                    .PathParam("id", "Message identifier").Body("{ reason? }", false).Returns("MessageModerationView").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "NotChanged"));
            post.Parameter.Add(HttpMethod.POST, b + "/messages/{id}/restore", RestoreAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Restore a removed message", _Tag, "The message becomes Visible again for both members. Audited.")
                    .PathParam("id", "Message identifier").Body("{ reason? }", false).Returns("MessageModerationView").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "NotChanged"));
            post.Parameter.Add(HttpMethod.POST, b + "/members/{userId}/revoke-messaging", RevokeAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Revoke direct messaging", _Tag, "RevokeMessaging: sets the member's canSendDirectMessages false (they can still read, delete their own messages, report, and block). Audited messaging.revoke.")
                    .PathParam("userId", "Member identifier").Body("{ reason? }", false).Returns("{ userId, canSendDirectMessages }").Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, b + "/members/{userId}/restore-messaging", RestoreMessagingAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Restore direct messaging", _Tag, "Sets canSendDirectMessages true again. Audited messaging.restore.")
                    .PathParam("userId", "Member identifier").Body("{ reason? }", false).Returns("{ userId, canSendDirectMessages }").Resp(403, "Forbidden").Resp(404, "NotFound"));
        }

        #endregion

        #region Private-Methods

        private async Task<ContentActor> ActorAsync(HttpContextBase ctx)
        {
            return await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
        }

        private RequestInfo Info(HttpContextBase ctx)
        {
            return RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
        }

        private async Task QueueAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Messaging.Reports.QueueAsync(actor, RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task SnapshotAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Messaging.Reports.SnapshotViewAsync(actor, Info(ctx), RouteHelper.Param(ctx, "reportId"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private Task RemoveAsync(HttpContextBase ctx)
        {
            return ModerateAsync(ctx, true);
        }

        private Task RestoreAsync(HttpContextBase ctx)
        {
            return ModerateAsync(ctx, false);
        }

        private async Task ModerateAsync(HttpContextBase ctx, bool remove)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Messaging.Reports.ModerateAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), remove,
                RouteHelper.ReadJson<MessagingModerationRequest>(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private Task RevokeAsync(HttpContextBase ctx)
        {
            return FlagAsync(ctx, false);
        }

        private Task RestoreMessagingAsync(HttpContextBase ctx)
        {
            return FlagAsync(ctx, true);
        }

        private async Task FlagAsync(HttpContextBase ctx, bool allowed)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Messaging.Reports.SetCanSendAsync(actor, Info(ctx), RouteHelper.Param(ctx, "userId"), allowed,
                RouteHelper.ReadJson<MessagingModerationRequest>(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
