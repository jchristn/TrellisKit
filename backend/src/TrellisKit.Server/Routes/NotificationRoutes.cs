namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Security;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.Routing;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Notification center (Member), notification preferences and push tokens (Signed in): Phase 2 contract 8.3 to 8.7,
    /// with lists as enumerations (docs/ENUMERATION_AND_METADATA.md). Reads are limited by
    /// rateLimits.memberReadsPerMinute, the unread count by rateLimits.unreadCountPerMinute, writes by
    /// rateLimits.notificationWritesPerMinute, and push token registration by push.registrationsPerHour.
    /// </summary>
    public class NotificationRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/notifications";
        private const string _Preferences = Constants.ApiPrefix + "/me/notification-preferences";
        private const string _Tokens = Constants.ApiPrefix + "/me/push-tokens";
        private const string _Tag = "Notifications";
        private const string _Orderings = "UpdatedDescending (default), CreatedDescending, CreatedAscending";
        private const string _Filters = "unreadOnly, type, category";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public NotificationRoutes(PropertyDirectory directory)
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
            RoutingGroup post = server.Routes.PostAuthentication;
            post.Static.Add(HttpMethod.GET, _Base, ListAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("List notifications", _Tag, "EnumerationResult of Notification plus unreadCount. Member.")
                    .Enumeration("Notification", _Orderings, _Filters).Resp(400, "ValidationFailed or InvalidContinuationToken").Resp(429, "RateLimited"));
            post.Static.Add(HttpMethod.POST, _Base + "/enumerate", EnumerateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate notifications", _Tag, "EnumerationQuery body; filters unreadOnly (boolean), type, category. Member.")
                    .Body("EnumerationQuery", false).Enumeration("Notification", _Orderings, _Filters, withQueryParameters: false).Resp(400, "ValidationFailed or InvalidContinuationToken").Resp(429, "RateLimited"));
            post.Static.Add(HttpMethod.GET, _Base + "/unread-count", UnreadCountAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Unread count", _Tag, "Poll every 60 seconds while visible (contract 8.4). Member.")
                    .Returns("UnreadCount").Resp(429, "RateLimited with Retry-After"));
            post.Static.Add(HttpMethod.POST, _Base + "/read", ReadAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Mark notifications read", _Tag, "Unknown or foreign ids are ignored. Member.")
                    .Body("{ ids: [1 to 100] }").Returns("{ unreadCount }").Resp(400, "ValidationFailed").Resp(429, "RateLimited"));
            post.Static.Add(HttpMethod.POST, _Base + "/unread", UnreadAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Mark notifications unread", _Tag, "Unknown or foreign ids are ignored. Member.")
                    .Body("{ ids: [1 to 100] }").Returns("{ unreadCount }").Resp(400, "ValidationFailed").Resp(429, "RateLimited"));
            post.Static.Add(HttpMethod.POST, _Base + "/read-all", ReadAllAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Mark all notifications read", _Tag, "Marks notifications last updated at or before beforeUtc (default now). Member.")
                    .Body("{ beforeUtc? }", false).Returns("{ unreadCount }").Resp(429, "RateLimited"));

            EnumerationRoutes.Add(post, _Preferences, AdministrativeCatalogs.NotificationPreferences, "CategoryPreferenceView",
                () => Doc("Enumerate notification preferences", _Tag, "Category preferences with pushPaused, autoFollowOnComment and notificationHours context. Declaration timestamps use Unix epoch."),
                async (ctx, query) =>
                {
                    ContentActor actor = Reader(ctx);
                    ApplicationServices services = _Directory.For(ctx);
                    EnumerationRequest request = services.Enumeration.Prepare(query, AdministrativeCatalogs.NotificationPreferences, false, actor.RequireUserId());
                    NotificationPreferencesDocument document = await services.NotificationPreferences.GetAsync(actor.RequireUserId(), ctx.Token).ConfigureAwait(false);
                    return CatalogEnumerationResponse<CategoryPreferenceView>.From(CatalogEnumeration.Page(request, document.Categories, item => item.Category.ToString()), new Dictionary<string, object?> { ["pushPaused"] = document.PushPaused, ["autoFollowOnComment"] = document.AutoFollowOnComment, ["notificationHours"] = document.NotificationHours });
                });
            post.Static.Add(HttpMethod.PUT, _Preferences, PutPreferencesAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Update notification preferences", _Tag, "Any subset: categories[{ category, inApp?, push?, email? }], notificationHours { enabled?, start?, end?, timeZone? }, pushPaused. Signed in.")
                    .Body("Partial preferences").Returns("NotificationPreferences").Resp(400, "ValidationFailed (for example fieldErrors[\"categories[0].inApp\"])").Resp(429, "RateLimited"));

            post.Static.Add(HttpMethod.GET, _Tokens, ListTokensAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("List push tokens", _Tag, "Token values are never returned; at most 10 exist. Signed in.")
                    .Enumeration("PushToken", "LastSeenDescending (default), CreatedDescending, CreatedAscending").Resp(400, "ValidationFailed"));
            post.Static.Add(HttpMethod.POST, _Tokens + "/enumerate", EnumerateTokensAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate push tokens", _Tag, "EnumerationQuery body. Signed in.")
                    .Body("EnumerationQuery", false).Enumeration("PushToken", "LastSeenDescending (default), CreatedDescending, CreatedAscending", withQueryParameters: false).Resp(400, "ValidationFailed"));
            post.Static.Add(HttpMethod.POST, _Tokens, RegisterTokenAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Register a push token", _Tag, "Bound to the calling session; an existing token moves to the caller. The eleventh token deletes the least recently seen. Signed in.")
                    .Body("{ token, platform: Ios | Android, deviceId?, deviceName?, appVersion? }").Resp(201, "PushToken (new)").Returns("PushToken (existing)").Resp(400, "ValidationFailed").Resp(429, "RateLimited"));
            post.Static.Add(HttpMethod.DELETE, _Tokens, DeleteTokenAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete a push token", _Tag, "204 also when unknown. Signed in.").Body("{ token }").Resp(204, "Deleted"));
        }

        #endregion

        #region Private-Methods

        private static ContentActor Caller(HttpContextBase ctx)
        {
            RequestContext? context = ctx.Metadata as RequestContext;
            return new ContentActor { UserId = context?.UserId, IsSystemAdmin = context?.IsAdmin ?? false };
        }

        private ContentActor Reader(HttpContextBase ctx)
        {
            ContentActor actor = Caller(ctx);
            actor.RequireUserId();
            _Directory.For(ctx).Limits.EnforceMember(actor, "member-reads", _Directory.For(ctx).Settings.RateLimits.MemberReadsPerMinute, RateLimitService.Minute);
            return actor;
        }

        private ContentActor Writer(HttpContextBase ctx)
        {
            ContentActor actor = Caller(ctx);
            actor.RequireUserId();
            _Directory.For(ctx).Limits.EnforceMember(actor, "notification-writes", _Directory.For(ctx).Settings.RateLimits.NotificationWritesPerMinute, RateLimitService.Minute);
            return actor;
        }

        private async Task ListAsync(HttpContextBase ctx)
        {
            ContentActor actor = Reader(ctx);
            EnumerationQuery query = RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Notifications.EnumerateAsync(actor, query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task EnumerateAsync(HttpContextBase ctx)
        {
            ContentActor actor = Reader(ctx);
            EnumerationQuery query = RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Notifications.EnumerateAsync(actor, query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UnreadCountAsync(HttpContextBase ctx)
        {
            ContentActor actor = Caller(ctx);
            string userId = actor.RequireUserId();
            _Directory.For(ctx).Limits.EnforceMember(actor, "unread-count", _Directory.For(ctx).Settings.RateLimits.UnreadCountPerMinute, RateLimitService.Minute);
            UnreadCountResponse response = await _Directory.For(ctx).Notifications.UnreadSummaryAsync(userId, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, response).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase ctx)
        {
            ContentActor actor = Writer(ctx);
            NotificationIdsRequest request = RouteHelper.ReadJson<NotificationIdsRequest>(ctx);
            long count = await _Directory.For(ctx).Notifications.MarkReadAsync(actor.RequireUserId(), request.Ids, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, Count(count)).ConfigureAwait(false);
        }

        private async Task UnreadAsync(HttpContextBase ctx)
        {
            ContentActor actor = Writer(ctx);
            NotificationIdsRequest request = RouteHelper.ReadJson<NotificationIdsRequest>(ctx);
            long count = await _Directory.For(ctx).Notifications.MarkUnreadAsync(actor.RequireUserId(), request.Ids, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, Count(count)).ConfigureAwait(false);
        }

        private async Task ReadAllAsync(HttpContextBase ctx)
        {
            ContentActor actor = Writer(ctx);
            ReadAllRequest request = RouteHelper.ReadJson<ReadAllRequest>(ctx);
            long count = await _Directory.For(ctx).Notifications.MarkAllReadAsync(actor.RequireUserId(), request.BeforeUtc, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, Count(count)).ConfigureAwait(false);
        }

        private async Task GetPreferencesAsync(HttpContextBase ctx)
        {
            ContentActor actor = Reader(ctx);
            NotificationPreferencesDocument document = await _Directory.For(ctx).NotificationPreferences.GetAsync(actor.RequireUserId(), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, document).ConfigureAwait(false);
        }

        private async Task PutPreferencesAsync(HttpContextBase ctx)
        {
            ContentActor actor = Writer(ctx);
            NotificationPreferencesRequest request = RouteHelper.ReadJson<NotificationPreferencesRequest>(ctx);
            NotificationPreferencesDocument document = await _Directory.For(ctx).NotificationPreferences.UpdateAsync(actor.RequireUserId(), request, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, document).ConfigureAwait(false);
        }

        private async Task ListTokensAsync(HttpContextBase ctx)
        {
            ContentActor actor = Reader(ctx);
            EnumerationQuery query = RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).PushTokens.EnumerateAsync(actor, query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task EnumerateTokensAsync(HttpContextBase ctx)
        {
            ContentActor actor = Reader(ctx);
            EnumerationQuery query = RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).PushTokens.EnumerateAsync(actor, query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task RegisterTokenAsync(HttpContextBase ctx)
        {
            ContentActor actor = Caller(ctx);
            string userId = actor.RequireUserId();
            _Directory.For(ctx).Limits.EnforceMember(actor, "push-registrations", _Directory.For(ctx).Settings.Push.RegistrationsPerHour, RateLimitService.Hour);
            PushTokenRequest request = RouteHelper.ReadJson<PushTokenRequest>(ctx);
            RequestContext? context = ctx.Metadata as RequestContext;
            PushTokenRegistration registration = await _Directory.For(ctx).PushTokens.RegisterAsync(userId, context?.SessionId, request, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, registration.Created ? 201 : 200, registration.Token).ConfigureAwait(false);
        }

        private async Task DeleteTokenAsync(HttpContextBase ctx)
        {
            ContentActor actor = Writer(ctx);
            PushTokenRequest request = RouteHelper.ReadJson<PushTokenRequest>(ctx);
            await _Directory.For(ctx).PushTokens.DeleteAsync(actor.RequireUserId(), request.Token, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private static Dictionary<string, long> Count(long count)
        {
            return new Dictionary<string, long> { { "unreadCount", count } };
        }

        #endregion
    }
}
