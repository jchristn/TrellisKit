namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Metrics;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Admin AI analytics, cost, the conversation browser, and the feedback queue (docs/AI_CONTRACT.md section 14 and
    /// resolved decisions 6, 7, and 14). SystemAdmin or the admin token. Opening a conversation is audited as
    /// ai.conversation.read.
    /// </summary>
    public class AdminAiAnalyticsRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/admin/ai";
        private const string _Tag = "Admin: AI analytics";
        private const string _Admin = " SystemAdmin or the admin token.";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public AdminAiAnalyticsRoutes(PropertyDirectory directory)
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
            post.Static.Add(HttpMethod.GET, _Base + "/analytics/overview", OverviewAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("AI overview", _Tag, "Totals over days (1 to 365, default 30), optionally for one assistant or endpoint: conversations, messages, members, visitors, feedback, requests, errors, tokens, cost, latency p50 and p95 from the histograms, fallback rate, withheld and failed replies, and the property budget." + _Admin)
                    .QueryParam("days", "1 to 365 (default 30)").QueryParam("assistantId", "Assistant").QueryParam("endpointId", "Endpoint").Returns("AiOverview").Resp(400, "ValidationFailed").Resp(404, "NotAvailable"));
            post.Static.Add(HttpMethod.GET, _Base + "/analytics/timeseries", TimeseriesAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("AI timeseries", _Tag, "metric: requests, errors, promptTokens, completionTokens, tokens, cost, latencyAvgMs, latencyP95Ms; granularity Hour (up to 7 days) or Day; at most 400 points." + _Admin)
                    .QueryParam("days", "1 to 365").QueryParam("metric", "Metric").QueryParam("granularity", "Hour or Day").QueryParam("assistantId", "Assistant").QueryParam("endpointId", "Endpoint")
                    .Enumeration("AiTimeseriesPoint", "CreatedDescending, CreatedAscending", "days, metric, granularity, assistantId, endpointId", true).Resp(400, "ValidationFailed").Resp(404, "NotAvailable"));
            post.Static.Add(HttpMethod.POST, _Base + "/analytics/timeseries/enumerate", TimeseriesAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("AI timeseries", _Tag, "metric: requests, errors, promptTokens, completionTokens, tokens, cost, latencyAvgMs, latencyP95Ms; granularity Hour (up to 7 days) or Day; at most 400 points." + _Admin)
                    .QueryParam("days", "1 to 365").QueryParam("metric", "Metric").QueryParam("granularity", "Hour or Day").QueryParam("assistantId", "Assistant").QueryParam("endpointId", "Endpoint")
                    .Body("EnumerationQuery",false).Enumeration("AiTimeseriesPoint", "CreatedDescending, CreatedAscending", "days, metric, granularity, assistantId, endpointId", true).Resp(400, "ValidationFailed").Resp(404, "NotAvailable"));
            post.Static.Add(HttpMethod.POST, _Base + "/analytics/endpoints/enumerate", EndpointsAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Usage and cost per endpoint", _Tag, "Requests, errors, p95 latency, tokens, cost, and requests per purpose over filters.windowDays (default 30); deleted endpoints included." + _Admin)
                    .Body("EnumerationQuery", false).Enumeration("{ endpoint, requests, errors, errorRate, latencyP95Ms, promptTokens, completionTokens, costUsd, purposes }", "NameAscending, CreatedDescending, CreatedAscending", "windowDays, assistantId", false).Resp(404, "NotAvailable"));
            post.Static.Add(HttpMethod.POST, _Base + "/analytics/assistants/enumerate", AssistantsAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Usage and cost per assistant", _Tag, "Conversations, messages, thumbs up rate, requests, error rate, p95 latency, tokens, cost, budget, and month-to-date spend over filters.windowDays (default 30)." + _Admin)
                    .Body("EnumerationQuery", false).Enumeration("{ assistant, conversations, messages, thumbsUpRate, requests, errorRate, latencyP95Ms, promptTokens, completionTokens, costUsd, monthlyBudgetUsd, monthToDateCostUsd }", "NameAscending, CreatedDescending, CreatedAscending", "windowDays", false).Resp(404, "NotAvailable"));
            post.Static.Add(HttpMethod.POST, _Base + "/analytics/members/enumerate", MembersAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Usage and cost per member", _Tag, "Messages, tokens, and cost per member or visitor over filters.windowDays (default 30); filters assistantId and subjectType (User or Visitor)." + _Admin)
                    .Body("EnumerationQuery", false).Enumeration("{ subjectType, subjectId, member, messages, promptTokens, completionTokens, costUsd, lastActiveUtc }", "CostDescending, MessagesDescending, CreatedDescending, CreatedAscending", "windowDays, assistantId, subjectType", false).Resp(404, "NotAvailable"));
            post.Static.Add(HttpMethod.POST, _Base + "/conversations/enumerate", ConversationsAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate conversations (admin)", _Tag, "Every conversation of the property with its owner; members are told in the chat UI that administrators can read conversations. Filters assistantId, userId, visitorId, ownerType (Member, Visitor), status, archived, surface." + _Admin)
                    .Body("EnumerationQuery", false).Enumeration("Conversation (with owner)", "LastMessageDescending, CreatedDescending, CreatedAscending", "assistantId, userId, visitorId, ownerType, status, archived, surface", false).Resp(400, "ValidationFailed").Resp(404, "NotAvailable"));
            post.Parameter.Add(HttpMethod.GET, _Base + "/conversations/{id}", ConversationAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a conversation (admin)", _Tag, "With its owner and the 20 newest messages with full telemetry (stages, retrieval, tokens, timings, cost). Audited as ai.conversation.read." + _Admin)
                    .PathParam("id", "Conversation identifier").Returns("Conversation").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/conversations/{id}/messages/enumerate", ConversationMessagesAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate conversation messages (admin)", _Tag, "Messages with full telemetry. Audited as ai.conversation.read." + _Admin)
                    .PathParam("id", "Conversation identifier").Body("EnumerationQuery", false).Enumeration("AssistantMessage (with telemetry)", "CreatedAscending, CreatedDescending", "", false).Resp(404, "NotFound"));
            post.Static.Add(HttpMethod.POST, _Base + "/feedback/enumerate", FeedbackAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate feedback", _Tag, "Rated replies with the member question they answered. Filters assistantId, rating, hasNote, reviewed." + _Admin)
                    .Body("EnumerationQuery", false).Enumeration("Feedback", "CreatedDescending, CreatedAscending", "assistantId, rating, hasNote, reviewed", false).Resp(400, "ValidationFailed").Resp(404, "NotAvailable"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/feedback/{id}/review", ReviewAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Mark feedback reviewed", _Tag, "Body { note? }. Audited as ai.feedback.review." + _Admin)
                    .PathParam("id", "Feedback identifier").Body("FeedbackReviewRequest", false).Resp(204, "Reviewed").Resp(404, "NotFound"));
        }

        #endregion

        #region Private-Methods

        private async Task OverviewAsync(HttpContextBase ctx)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            int days = RouteHelper.QueryInt(ctx, "days", errors) ?? 30;
            if (days < 1 || days > 365) errors["days"] = "Must be 1 to 365.";
            RouteHelper.ThrowIfErrors(errors);
            ApplicationServices s = _Directory.For(ctx);
            s.Ai.EnsureAvailable();
            await RouteHelper.SendJsonAsync(ctx, 200, await s.Ai.Usage.OverviewAsync(days, RouteHelper.Query(ctx, "assistantId"), RouteHelper.Query(ctx, "endpointId"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task TimeseriesAsync(HttpContextBase ctx)
        {
            EnumerationQuery query=ctx.Request.Method==HttpMethod.GET?RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx):RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            int days=AggregateEnumeration.Days(query,1);
            Dictionary<string,string> errors=new Dictionary<string,string>();
            AiUsageGranularityEnum granularity=query.FilterEnum<AiUsageGranularityEnum>("granularity",errors)??AiUsageGranularityEnum.Day;
            RouteHelper.ThrowIfErrors(errors);
            ApplicationServices s=_Directory.For(ctx);s.Ai.EnsureAvailable();
            TrellisKit.Core.Responses.AiTimeseriesView source=await s.Ai.Usage.TimeseriesAsync(days,query.FilterString("metric"),granularity,query.FilterString("assistantId"),query.FilterString("endpointId"),ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx,200,AggregateEnumeration.Ai(s,query,source)).ConfigureAwait(false);
        }

        private async Task EndpointsAsync(HttpContextBase ctx)
        {
            EnumerationQuery query = RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            ApplicationServices s = _Directory.For(ctx);
            s.Ai.EnsureAvailable();
            await RouteHelper.SendJsonAsync(ctx, 200, await s.Ai.Usage.EndpointsAsync(query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task AssistantsAsync(HttpContextBase ctx)
        {
            EnumerationQuery query = RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            ApplicationServices s = _Directory.For(ctx);
            s.Ai.EnsureAvailable();
            await RouteHelper.SendJsonAsync(ctx, 200, await s.Ai.Usage.AssistantsAsync(query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task MembersAsync(HttpContextBase ctx)
        {
            EnumerationQuery query = RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            ApplicationServices s = _Directory.For(ctx);
            s.Ai.EnsureAvailable();
            await RouteHelper.SendJsonAsync(ctx, 200, await s.Ai.Usage.MembersAsync(query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ConversationsAsync(HttpContextBase ctx)
        {
            EnumerationQuery query = RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ai.Conversations.EnumerateAdminAsync(query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ConversationAsync(HttpContextBase ctx)
        {
            ApplicationServices s = _Directory.For(ctx);
            RequestInfo info = RequestInfo.From(ctx, s.Settings.Rest);
            await RouteHelper.SendJsonAsync(ctx, 200, await s.Ai.Conversations.GetAdminAsync(RouteHelper.Param(ctx, "id"), info, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ConversationMessagesAsync(HttpContextBase ctx)
        {
            EnumerationQuery query = RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            ApplicationServices s = _Directory.For(ctx);
            RequestInfo info = RequestInfo.From(ctx, s.Settings.Rest);
            await RouteHelper.SendJsonAsync(ctx, 200, await s.Ai.Conversations.EnumerateAdminMessagesAsync(RouteHelper.Param(ctx, "id"), query, info, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task FeedbackAsync(HttpContextBase ctx)
        {
            EnumerationQuery query = RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ai.Conversations.EnumerateFeedbackAsync(query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ReviewAsync(HttpContextBase ctx)
        {
            FeedbackReviewRequest request = RouteHelper.ReadJson<FeedbackReviewRequest>(ctx);
            ApplicationServices s = _Directory.For(ctx);
            ContentActor actor = await s.Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await s.Ai.Conversations.ReviewFeedbackAsync(actor, RouteHelper.Param(ctx, "id"), request, RequestInfo.From(ctx, s.Settings.Rest), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        #endregion
    }
}
