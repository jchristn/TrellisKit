namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Ai;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Admin assistant routes (docs/AI_CONTRACT.md sections 5.3, 5.4, and 7): CRUD, duplicate, the streaming test
    /// console, knowledge preview, and knowledge ingestion status. SystemAdmin or the admin token.
    /// </summary>
    public class AdminAiAssistantRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/admin/ai/assistants";
        private const string _Tools = Constants.ApiPrefix + "/admin/ai/tools";
        private const string _Tag = "Admin: AI assistants";
        private const string _Admin = " SystemAdmin or the admin token.";
        private const string _Orderings = "NameAscending, CreatedDescending, CreatedAscending, UpdatedDescending";
        private const string _Filters = "enabled, audience, endpointId, embedEnabled";
        private const string _Body = "{ name, description?, enabled?, title?, avatar?, systemPrompt?, models: { chat, chatFallback?, titleGeneration?, queryRewrite?, embedding?, rerank?, moderation?, toolRouting? }, generation?, audience?, knowledge?: { enabled, mode, maxContextItems, maxContextCharacters, citations, sources }, tools?: { enabled, allowed, maxToolIterations, maxToolCallsPerTurn, toolCallTimeoutMs, maxToolOutputCharacters, maxTokensPerTurn, showToolActivity }, limits?, streaming?, showReasoning?, starterPrompts?, disclaimer?, embed?, moderationMode?, budget?: { monthlyUsd, hardStop }, labels?, tags? }";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public AdminAiAssistantRoutes(PropertyDirectory directory)
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
            post.Static.Add(HttpMethod.GET, _Base, ListAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("List assistants (admin)", _Tag, "Every assistant with its configuration and 30-day stats." + _Admin)
                    .Enumeration("Assistant", _Orderings, _Filters).Resp(404, "NotAvailable"));
            post.Static.Add(HttpMethod.POST, _Base + "/enumerate", EnumerateAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate assistants (admin)", _Tag, "EnumerationQuery body." + _Admin)
                    .Body("EnumerationQuery", false).Enumeration("Assistant", _Orderings, _Filters, false).Resp(404, "NotAvailable"));
            post.Static.Add(HttpMethod.POST, _Base, CreateAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Create an assistant", _Tag, "Every referenced endpoint is checked for its kind. Audited." + _Admin)
                    .Body(_Body).Returns("Assistant", 201).Resp(400, "ValidationFailed or EndpointKindMismatch").Resp(409, "Conflict"));
            post.Parameter.Add(HttpMethod.GET, _Base + "/{id}", GetAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read an assistant (admin)", _Tag, _Admin.Trim()).PathParam("id", "Assistant identifier").Returns("Assistant").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/{id}", UpdateAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Update an assistant", _Tag, "Partial; nested objects merge; knowledge.sources, tools.allowed, starterPrompts, and embed.allowedOrigins replace their lists when present. Audited." + _Admin)
                    .PathParam("id", "Assistant identifier").Body(_Body).Returns("Assistant").Resp(400, "ValidationFailed or EndpointKindMismatch").Resp(404, "NotFound").Resp(409, "Conflict"));
            post.Parameter.Add(HttpMethod.DELETE, _Base + "/{id}", DeleteAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete an assistant", _Tag, "Its knowledge collection is deleted; conversations are kept for ai.deletedAssistantRetentionDays. Audited." + _Admin)
                    .PathParam("id", "Assistant identifier").Resp(204, "Deleted").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/duplicate", DuplicateAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Duplicate an assistant", _Tag, "Body { name? }. The copy is disabled. Audited." + _Admin)
                    .PathParam("id", "Assistant identifier").Body("AssistantDuplicateRequest", false).Returns("Assistant", 201).Resp(404, "NotFound").Resp(409, "Conflict"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/test", TestAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Test an assistant", _Tag, "Body { messages: [{ role, content }] (1 to 20), useDraft? }. Accept: text/event-stream streams the chat events plus debug events (system prompt, retrieved knowledge, tool calls with arguments and truncated outputs, errors); tools run as the administrator; otherwise 201 { userMessage, reply } with full telemetry. useDraft tests unsaved fields. Nothing is stored except usage rollups marked test. Rate limited." + _Admin)
                    .PathParam("id", "Assistant identifier").Body("AssistantTestRequest").File("text/event-stream", "SSE stream of chat and debug events").Returns("MessageExchange", 201)
                    .Resp(400, "ValidationFailed").Resp(404, "NotFound").Resp(429, "RateLimited"));
            EnumerationRoutes.Add(post, _Base + "/{id}/knowledge/preview", AdministrativeCatalogs.KnowledgePreview, "KnowledgePreviewItem",
                () => Doc("Enumerate knowledge preview", _Tag, "filters.query is the retrieval query; filters.asUserId optionally selects a member. Pages enumerate the finite context shortlist (at most maxContextItems, capped at 50), not the whole corpus. totalCandidates and mode are context; default ordering RankAscending. Projection timestamps use Unix epoch because retrieval rows have no source creation timestamp. Metadata filters apply to each visible source target." + _Admin),
                PreviewAsync, "id");
            post.Parameter.Add(HttpMethod.GET, _Base + "/{id}/knowledge/status", StatusAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read knowledge ingestion status", _Tag, "Vector index state in RecallDB: status, collection, dimensions, item and chunk counts, last run, last error." + _Admin)
                    .PathParam("id", "Assistant identifier").Returns("KnowledgeIndex").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/knowledge/reindex", ReindexAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Reindex knowledge", _Tag, "Rebuilds the assistant's vector collection in the background." + _Admin)
                    .PathParam("id", "Assistant identifier").Returns("KnowledgeIndex", 202).Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/knowledge/items/enumerate", ItemsAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate knowledge items", _Tag, "Ingestion state per content item, most recently updated first (filters status, targetType)." + _Admin)
                    .PathParam("id", "Assistant identifier").Body("EnumerationQuery", false).Enumeration("KnowledgeItem", "CreatedDescending, CreatedAscending", "status, targetType", false).Resp(404, "NotFound"));
            post.Static.Add(HttpMethod.POST, _Tools + "/enumerate", ToolsAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate assistant tools", _Tag, "The engine tool registry of the property (name, description, activity labels, aliases, worksSignedOut, parameters schema) with each tool's property-wide kill switch. Search matches name, description, label, and aliases; filter disabled." + _Admin)
                    .Body("EnumerationQuery", false).Enumeration("AssistantTool", "NameAscending", "disabled", false).Resp(404, "NotAvailable"));
            post.Parameter.Add(HttpMethod.PUT, _Tools + "/{name}", SwitchToolAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Switch an assistant tool on or off", _Tag, "Body { disabled, reason? }. The property-wide kill switch: a disabled tool is never offered to any assistant and calls to it are rejected. Aliases accepted (findGyms). Audited as ai.tool.disable or ai.tool.enable." + _Admin)
                    .PathParam("name", "Tool name or alias").Body("AssistantToolSwitchRequest").Returns("AssistantTool").Resp(400, "ValidationFailed").Resp(404, "NotFound"));
        }

        #endregion

        #region Private-Methods

        private RequestInfo Info(HttpContextBase ctx)
        {
            return RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
        }

        private async Task ListAsync(HttpContextBase ctx)
        {
            EnumerationQuery query = RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ai.Assistants.EnumerateAdminAsync(query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task EnumerateAsync(HttpContextBase ctx)
        {
            EnumerationQuery query = RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ai.Assistants.EnumerateAdminAsync(query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ToolsAsync(HttpContextBase ctx)
        {
            EnumerationQuery query = RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ai.ToolService.EnumerateAsync(query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task SwitchToolAsync(HttpContextBase ctx)
        {
            AssistantToolSwitchRequest request = RouteHelper.ReadJson<AssistantToolSwitchRequest>(ctx);
            ApplicationServices s = _Directory.For(ctx);
            ContentActor actor = await s.Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await s.Ai.ToolService.SwitchAsync(actor, RouteHelper.Param(ctx, "name"), request, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase ctx)
        {
            AssistantRequest request = RouteHelper.ReadJson<AssistantRequest>(ctx);
            ApplicationServices s = _Directory.For(ctx);
            ContentActor actor = await s.Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await s.Ai.Assistants.CreateAsync(actor, request, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GetAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ai.Assistants.GetAdminAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase ctx)
        {
            AssistantRequest request = RouteHelper.ReadJson<AssistantRequest>(ctx, out HashSet<string> present);
            ApplicationServices s = _Directory.For(ctx);
            ContentActor actor = await s.Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await s.Ai.Assistants.UpdateAsync(actor, RouteHelper.Param(ctx, "id"), request, present, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            await _Directory.For(ctx).Ai.Assistants.DeleteAsync(RouteHelper.Param(ctx, "id"), Info(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task DuplicateAsync(HttpContextBase ctx)
        {
            AssistantDuplicateRequest request = RouteHelper.ReadJson<AssistantDuplicateRequest>(ctx);
            ApplicationServices s = _Directory.For(ctx);
            ContentActor actor = await s.Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await s.Ai.Assistants.DuplicateAsync(actor, RouteHelper.Param(ctx, "id"), request, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task TestAsync(HttpContextBase ctx)
        {
            AssistantTestRequest request = RouteHelper.ReadJson<AssistantTestRequest>(ctx);
            ApplicationServices s = _Directory.For(ctx);
            AssistantCaller caller = await AdminCallerAsync(ctx, s).ConfigureAwait(false);
            Assistant saved = await s.Ai.Assistants.RequireAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            Assistant assistant = await s.Ai.Assistants.DraftAsync(saved, request.UseDraft, ctx.Token).ConfigureAwait(false);
            PreparedExchange prepared = await s.Ai.Chat.PrepareTestAsync(caller, assistant, request, ctx.Token).ConfigureAwait(false);
            if (!AiRouteHelper.WantsStream(ctx))
            {
                await RouteHelper.SendJsonAsync(ctx, 201, await s.Ai.Chat.RunAsync(prepared, NullChatEventSink.Instance, false).ConfigureAwait(false)).ConfigureAwait(false);
                return;
            }
            SseWriter writer = new SseWriter(ctx, s.Settings.Ai.SseHeartbeatSeconds);
            await using (writer.ConfigureAwait(false))
            {
                await writer.StartAsync().ConfigureAwait(false);
                await s.Ai.Chat.RunAsync(prepared, writer, true).ConfigureAwait(false);
                await writer.CompleteAsync().ConfigureAwait(false);
            }
        }

        private async Task<object> PreviewAsync(HttpContextBase ctx, EnumerationQuery query)
        {
            ApplicationServices s = _Directory.For(ctx);
            EnumerationRequest enumeration = s.Enumeration.Prepare(query, AdministrativeCatalogs.KnowledgePreview, true, RouteHelper.Param(ctx, "id"), 50);
            KnowledgePreviewRequest request = new KnowledgePreviewRequest { Query = enumeration.Query.FilterString("query"), AsUserId = enumeration.Query.FilterString("asUserId") };
            Assistant assistant = await s.Ai.Assistants.RequireAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(request.Query)) throw ApiErrorException.Field("filters.query", "Required.");
            AssistantCaller caller;
            if (!String.IsNullOrWhiteSpace(request.AsUserId))
            {
                User? user = await s.Database.Users.ReadAsync(request.AsUserId.Trim(), ctx.Token).ConfigureAwait(false);
                if (user == null) throw ApiErrorException.NotFound("Member");
                ContentActor member = await s.Actors.ForUserAsync(user.Id, ctx.Token).ConfigureAwait(false);
                caller = new AssistantCaller { UserId = user.Id, Actor = member, DisplayName = user.DisplayName, HasMembership = member.HasMembership, SourceIp = Info(ctx).SourceIp };
            }
            else
            {
                // An anonymous Members-audience caller: a member without staff rights or blocks.
                caller = new AssistantCaller { UserId = "usr_preview", Actor = new ContentActor { UserId = "usr_preview", HasMembership = true, AgeAttested = true, IsBreakGlass = false }, HasMembership = true, SourceIp = Info(ctx).SourceIp };
            }
            Dictionary<string, ModelEndpoint> endpoints = await s.Ai.Assistants.EndpointsAsync(assistant, ctx.Token).ConfigureAwait(false);
            KnowledgePreviewView result = await s.Ai.Knowledge.PreviewAsync(assistant, endpoints, caller, request.Query.Trim(), ctx.Token).ConfigureAwait(false);
            List<KnowledgePreviewItem> items = result.Items.GroupBy(item => item.Id, StringComparer.Ordinal).Select(group => group.First()).ToList();
            foreach (IGrouping<string, KnowledgePreviewItem> group in items.GroupBy(item => item.TargetType))
                if (Enum.TryParse(group.Key, out TargetTypeEnum type)) await s.Metadata.AttachAsync(type, group.ToList(), item => item.TargetId, true, ctx.Token).ConfigureAwait(false);
            return CatalogEnumerationResponse<KnowledgePreviewItem>.From(CatalogEnumeration.Page(enumeration, items, item => item.Id,
                search: item => item.Title + " " + item.Excerpt, labels: item => item.Labels, tags: item => item.Tags,
                orderKey: item => enumeration.Ordering.Name == "RankAscending" ? item.Rank.ToString(CultureInfo.InvariantCulture) : item.Id),
                new Dictionary<string, object?> { ["totalCandidates"] = result.TotalCandidates, ["mode"] = result.Mode });
        }

        private async Task StatusAsync(HttpContextBase ctx)
        {
            ApplicationServices s = _Directory.For(ctx);
            Assistant assistant = await s.Ai.Assistants.RequireAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await s.Ai.Indexer.StatusAsync(assistant, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ReindexAsync(HttpContextBase ctx)
        {
            ApplicationServices s = _Directory.For(ctx);
            Assistant assistant = await s.Ai.Assistants.RequireAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 202, await s.Ai.Indexer.ReindexAsync(assistant, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ItemsAsync(HttpContextBase ctx)
        {
            ApplicationServices s = _Directory.For(ctx);
            Assistant assistant = await s.Ai.Assistants.RequireAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            EnumerationQuery query = RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await s.Ai.Indexer.ItemsAsync(assistant, query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task<AssistantCaller> AdminCallerAsync(HttpContextBase ctx, ApplicationServices s)
        {
            ContentActor actor = await s.Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            return new AssistantCaller
            {
                UserId = actor.UserId,
                Actor = actor,
                DisplayName = actor.User?.DisplayName ?? "Administrator",
                IsSystemAdmin = true,
                HasMembership = true,
                Surface = AiConversationSurfaceEnum.AdminTest,
                SourceIp = Info(ctx).SourceIp,
                Context = ctx.Metadata as Core.Security.RequestContext
            };
        }

        #endregion
    }
}
