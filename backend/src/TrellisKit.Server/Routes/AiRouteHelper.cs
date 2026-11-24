namespace TrellisKit.Server.Routes
{
    using System;
    using System.Globalization;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Security;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Ai;
    using WatsonWebserver.Core;

    /// <summary>
    /// Shared pieces of the AI route registrars: caller resolution for the member chat routes (which run before the
    /// authentication stage so Public assistants work signed out with a visitor token), the client surface, and an
    /// exception handler that also sets Retry-After on 503 ModelBusy.
    /// </summary>
    public static class AiRouteHelper
    {
        #region Public-Methods

        /// <summary>
        /// Exception handler for AI routes: Retry-After on 503 responses that carry one, then the shared handler.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="exception">Exception.</param>
        /// <returns>Task.</returns>
        public static Task HandleExceptionAsync(HttpContextBase ctx, Exception exception)
        {
            if (exception is ApiErrorException api && api.StatusCode == 503 && api.RetryAfterSeconds != null)
                ctx.Response.Headers["Retry-After"] = api.RetryAfterSeconds.Value.ToString(CultureInfo.InvariantCulture);
            return RouteHelper.HandleExceptionAsync(ctx, exception);
        }

        /// <summary>
        /// Resolve the chat caller: a bearer token (member, delegated administrator, or the admin token), else a visitor
        /// token when visitors are allowed. An invalid bearer token is 401; signed out without a visitor is 401 unless a
        /// visitor may be created.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="services">Property services.</param>
        /// <param name="allowVisitor">Whether a visitor may call.</param>
        /// <param name="createVisitor">Whether a missing visitor is created (the X-TK-Visitor-Token response header carries it).</param>
        /// <returns>Caller.</returns>
        /// <exception cref="ApiErrorException">401 Unauthorized, 403 AgeAttestationRequired, 404 NotAvailable.</exception>
        public static async Task<AssistantCaller> CallerAsync(HttpContextBase ctx, ApplicationServices services, bool allowVisitor, bool createVisitor)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            ArgumentNullException.ThrowIfNull(services);
            services.Ai.EnsureAvailable();
            RequestInfo info = RequestInfo.From(ctx, services.Settings.Rest);
            AiConversationSurfaceEnum surface = Surface(ctx);
            string? header = ctx.Request.RetrieveHeaderValue("Authorization");
            if (!String.IsNullOrWhiteSpace(header))
            {
                RequestContext? context = services.Authentication.Authenticate(header);
                if (context == null && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    SessionPrincipal? principal = await services.Tokens.ValidateAccessTokenAsync(header.Substring(7).Trim(), ctx.Token).ConfigureAwait(false);
                    if (principal != null)
                    {
                        bool delegated = principal.PrincipalKind == PrincipalKindEnum.Delegated;
                        context = new RequestContext
                        {
                            IsAuthenticated = true,
                            PrincipalKind = principal.PrincipalKind,
                            UserId = delegated ? null : principal.UserId,
                            PlatformAdminId = delegated ? principal.UserId : null,
                            SessionId = principal.SessionId,
                            Email = principal.Email,
                            PrincipalName = principal.DisplayName,
                            IsAdmin = principal.IsSystemAdmin || delegated,
                            IsBreakGlass = delegated,
                            IsModerator = principal.IsModerator,
                            AgeAttested = principal.AgeAttested || delegated,
                            HasMembership = principal.HasMembership || delegated,
                            ClientType = principal.ClientType,
                            Scheme = delegated ? "Delegated" : "Session"
                        };
                    }
                }
                if (context == null) throw new ApiErrorException(401, "Unauthorized", "A valid bearer token is required.");
                context.PropertyId = services.PropertyId;
                ctx.Metadata = context;
                ContentActor actor = await services.Actors.ResolveAsync(context, ctx.Token).ConfigureAwait(false);
                if (!context.IsAdmin && !context.AgeAttested)
                    throw new ApiErrorException(403, "AgeAttestationRequired", "Confirm you are " + services.Authorization.MinimumAge + " or older to continue.");
                return new AssistantCaller
                {
                    UserId = actor.UserId,
                    Actor = actor,
                    DisplayName = actor.User?.DisplayName ?? context.PrincipalName ?? String.Empty,
                    IsSystemAdmin = actor.IsSystemAdmin || context.IsAdmin,
                    HasMembership = actor.HasMembership || context.IsAdmin,
                    Surface = surface,
                    SourceIp = info.SourceIp,
                    Context = context
                };
            }
            if (!allowVisitor) throw new ApiErrorException(401, "Unauthorized", "A valid bearer token is required.");
            AssistantVisitor? visitor = await services.Ai.Visitors.ResolveAsync(ctx.Request.RetrieveHeaderValue(AiConstants.VisitorTokenHeader), ctx.Token).ConfigureAwait(false);
            if (visitor == null)
            {
                if (!createVisitor) throw new ApiErrorException(401, "Unauthorized", "Sign in, or send the " + AiConstants.VisitorTokenHeader + " header of a visitor session.");
                VisitorSessionView session = await services.Ai.Visitors.CreateAsync(info.SourceIp, surface, ctx.Token).ConfigureAwait(false);
                ctx.Response.Headers[AiConstants.VisitorTokenHeader] = session.VisitorToken;
                visitor = new AssistantVisitor { Id = session.VisitorId, Surface = surface };
            }
            return new AssistantCaller
            {
                VisitorId = visitor.Id,
                Actor = services.Actors.Anonymous(info.SourceIp),
                DisplayName = String.Empty,
                Surface = surface,
                SourceIp = info.SourceIp
            };
        }

        /// <summary>
        /// The client surface from X-Client: mobile (or tablet), widget, admin, else web.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <returns>Surface.</returns>
        public static AiConversationSurfaceEnum Surface(HttpContextBase ctx)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            string client = (ctx.Request.RetrieveHeaderValue(Constants.ClientHeader) ?? String.Empty).Trim().ToLowerInvariant();
            if (client.StartsWith("mobile", StringComparison.Ordinal) || client.StartsWith("ios", StringComparison.Ordinal) || client.StartsWith("android", StringComparison.Ordinal) || client.StartsWith("tablet", StringComparison.Ordinal))
                return AiConversationSurfaceEnum.Mobile;
            if (client.StartsWith("widget", StringComparison.Ordinal)) return AiConversationSurfaceEnum.Widget;
            return AiConversationSurfaceEnum.Web;
        }

        /// <summary>
        /// Whether the client asked for an SSE stream (Accept: text/event-stream).
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <returns>True when streaming.</returns>
        public static bool WantsStream(HttpContextBase ctx)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            string accept = ctx.Request.RetrieveHeaderValue("Accept") ?? String.Empty;
            return accept.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Run a prepared exchange: an SSE stream when asked for (the exchange is generated detached from the
        /// connection), otherwise 201 with the member message and the finished reply.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="services">Property services.</param>
        /// <param name="prepared">Prepared exchange.</param>
        /// <returns>Task.</returns>
        public static async Task SendExchangeAsync(HttpContextBase ctx, ApplicationServices services, PreparedExchange prepared)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(prepared);
            if (!WantsStream(ctx))
            {
                MessageExchangeView exchange = await services.Ai.Chat.RunAsync(prepared, NullChatEventSink.Instance, false).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 201, exchange).ConfigureAwait(false);
                return;
            }
            SseWriter writer = new SseWriter(ctx, services.Settings.Ai.SseHeartbeatSeconds);
            await using (writer.ConfigureAwait(false))
            {
                await writer.StartAsync().ConfigureAwait(false);
                await services.Ai.Chat.RunAsync(prepared, writer, true).ConfigureAwait(false);
                await writer.CompleteAsync().ConfigureAwait(false);
            }
        }

        #endregion
    }
}
