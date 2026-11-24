namespace TrellisKit.Server.Services.Ai
{
    using PolyPrompt.Models;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Task roles and their executor hierarchies (docs/INTEGRATION.md). Features call a role, never
    /// a model: the gateway walks the role's executors in order under its availability and exhaustion policies, sends
    /// the role's reasoning effort, and records every attempt in the call ledger with the executor position and the
    /// escalation reason. The scope guardrail applies to every call (owner requirement R7). CLI agent harness executors
    /// run only on worker instances (jobs.runHarnesses) and only inside a job item, never in request paths; elsewhere
    /// they are passed over with the reason, which the next executor's ledger row records (docs/HARNESSES.md).
    /// <see cref="RunAgentAsync"/> runs an agentic task with typed job tools on either kind of executor.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class TaskRoleService
    {
        #region Public-Members

        /// <summary>
        /// Seconds the saved hierarchies are reused before reloading. Default 30, minimum 1, maximum 3600.
        /// </summary>
        public int CacheSeconds
        {
            get { return _CacheSeconds; }
            set { _CacheSeconds = Math.Clamp(value, 1, 3600); }
        }

        /// <summary>
        /// Most executors in one hierarchy.
        /// </summary>
        public const int MaxExecutors = 8;

        /// <summary>Bounded role catalog for this property, including its optional ai/roles.json definitions.</summary>
        public IReadOnlyList<TaskRoleDefinition> Definitions { get; }

        /// <summary>Find a role in this property catalog, or return null.</summary>
        /// <param name="key">Role key.</param>
        /// <returns>Definition or null.</returns>
        public TaskRoleDefinition? FindDefinition(string? key)
        {
            return Definitions.FirstOrDefault(d => String.Equals(d.Role, key, StringComparison.Ordinal));
        }

        #endregion

        #region Private-Members

        private readonly AiServices _Ai;
        private readonly SemaphoreSlim _Load = new SemaphoreSlim(1, 1);
        private Dictionary<string, AiTaskRoleConfig>? _Configs = null;
        private DateTime _LoadedUtc = DateTime.MinValue;
        private int _CacheSeconds = 30;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="ai">AI services of the property.</param>
        /// <exception cref="ArgumentNullException">ai is null.</exception>
        public TaskRoleService(AiServices ai)
        {
            _Ai = ai ?? throw new ArgumentNullException(nameof(ai));
            Definitions = TaskRoleCatalog.Load(ai.App.Property.Property.Directory);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Complete a single prompt through a role.
        /// </summary>
        /// <param name="role">Role key.</param>
        /// <param name="prompt">User prompt (treated as data by the scope guardrail).</param>
        /// <param name="systemPrompt">System prompt, or null.</param>
        /// <param name="maxTokens">Output cap, or null for the endpoint's.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="isTest">Test traffic (admin consoles). Default false.</param>
        /// <returns>Outcome; never throws for provider failures.</returns>
        /// <exception cref="ArgumentNullException">prompt is null.</exception>
        public Task<RoleOutcome> PromptAsync(string role, string prompt, string? systemPrompt, int? maxTokens, CancellationToken token, bool isTest = false)
        {
            ArgumentNullException.ThrowIfNull(prompt);
            List<ChatMessage> messages = new List<ChatMessage>();
            if (!String.IsNullOrWhiteSpace(systemPrompt)) messages.Add(ChatMessage.System(systemPrompt));
            messages.Add(ChatMessage.User(prompt));
            return CompleteAsync(role, messages, maxTokens, token, isTest);
        }

        /// <summary>
        /// Complete a conversation through a role's executor hierarchy.
        /// </summary>
        /// <param name="role">Role key.</param>
        /// <param name="messages">Messages.</param>
        /// <param name="maxTokens">Output cap, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="isTest">Test traffic. Default false.</param>
        /// <returns>Outcome; never throws for provider failures.</returns>
        /// <exception cref="ArgumentNullException">messages is null.</exception>
        public async Task<RoleOutcome> CompleteAsync(string role, List<ChatMessage> messages, int? maxTokens, CancellationToken token, bool isTest = false)
        {
            ArgumentNullException.ThrowIfNull(messages);
            Stopwatch watch = Stopwatch.StartNew();
            RoleOutcome result = new RoleOutcome();
            TaskRoleDefinition? definition = FindDefinition(role);
            if (definition == null || definition.Kind != ModelEndpointKindEnum.Completion)
            {
                result.ErrorCode = "UnknownRole";
                return result;
            }
            if (!_Ai.IsAvailable)
            {
                result.ErrorCode = "NotAvailable";
                return result;
            }

            AiTaskRoleConfig config = await ConfigAsync(definition.Role, token).ConfigureAwait(false);
            List<AiExecutor> executors = config.Executors.Where(e => e.Enabled).OrderBy(e => e.Position).ToList();
            if (executors.Count == 0)
            {
                result.ErrorCode = "NoExecutors";
                result.LatencyMs = watch.ElapsedMilliseconds;
                return result;
            }

            int rounds = config.ExhaustionPolicy == AiExhaustionPolicyEnum.Wait ? 3 : 1;
            bool sawBusy = false;
            for (int round = 0; round < rounds; round++)
            {
                if (round > 0) await Task.Delay(TimeSpan.FromSeconds(2 * round), token).ConfigureAwait(false);
                foreach (AiExecutor executor in executors)
                {
                    token.ThrowIfCancellationRequested();
                    string? skip = await SkipReasonAsync(definition, executor, false, token).ConfigureAwait(false);
                    if (skip != null)
                    {
                        result.Escalations.Add(Line(executor, skip));
                        continue;
                    }

                    if (executor.Runtime != AiExecutorRuntimeEnum.ApiEndpoint)
                    {
                        string prompt = HarnessPrompt(messages, out string? extraSystem);
                        HarnessCallResult harness;
                        using (AiCallScope.BeginRole(definition.Role, executor.Position, Escalation(result.Escalations)))
                        {
                            harness = await _Ai.Harnesses.RunAsync(new HarnessCall
                            {
                                Runtime = executor.Runtime,
                                Model = executor.Model,
                                MuxEndpoint = executor.EndpointId,
                                Prompt = prompt,
                                SystemPrompt = await HarnessSystemAsync(null, extraSystem, token).ConfigureAwait(false),
                                PermissionMode = CliPermissionMode.Restricted,
                                TimeoutMs = HarnessTimeoutMs(null),
                                IsTest = isTest
                            }, token).ConfigureAwait(false);
                        }
                        result.CostMicros += harness.CostMicros;
                        if (harness.Success)
                        {
                            result.Success = true;
                            result.Text = harness.Text;
                            result.ExecutorPosition = executor.Position;
                            result.Runtime = executor.Runtime.ToString();
                            result.EndpointId = executor.EndpointId;
                            result.Model = harness.Model;
                            result.PromptTokens = harness.PromptTokens;
                            result.CompletionTokens = harness.CompletionTokens;
                            result.LatencyMs = watch.ElapsedMilliseconds;
                            return result;
                        }
                        sawBusy |= harness.Busy;
                        result.Escalations.Add(Line(executor, harness.ErrorCode ?? "HarnessFailed"));
                        continue;
                    }

                    ModelEndpoint endpoint = (await _Ai.App.Database.Ai.Endpoints.ReadAsync(executor.EndpointId!, token).ConfigureAwait(false))!;
                    int tries = config.AvailabilityPolicy == AiAvailabilityPolicyEnum.Wait ? 3 : 1;
                    for (int attempt = 0; attempt < tries; attempt++)
                    {
                        if (attempt > 0) await Task.Delay(TimeSpan.FromMilliseconds(750 * attempt), token).ConfigureAwait(false);
                        CompletionOutcome outcome;
                        using (AiCallScope.BeginRole(definition.Role, executor.Position, Escalation(result.Escalations)))
                        {
                            outcome = await _Ai.Invoker.CompleteAsync(new CompletionCall
                            {
                                Endpoint = endpoint,
                                Messages = messages,
                                MaxTokens = maxTokens,
                                Stream = false,
                                Purpose = AssistantPurposeEnum.Task,
                                IsTest = isTest,
                                ReasoningEffort = config.ReasoningEffort ?? definition.DefaultEffort
                            }, null, null, token).ConfigureAwait(false);
                        }
                        result.CostMicros += outcome.CostMicros;
                        if (outcome.Success)
                        {
                            result.Success = true;
                            result.Text = outcome.Text;
                            result.ExecutorPosition = executor.Position;
                            result.Runtime = AiExecutorRuntimeEnum.ApiEndpoint.ToString();
                            result.EndpointId = endpoint.Id;
                            result.Model = endpoint.CallModel;
                            result.PromptTokens = outcome.PromptTokens;
                            result.CompletionTokens = outcome.CompletionTokens;
                            result.LatencyMs = watch.ElapsedMilliseconds;
                            return result;
                        }
                        if (outcome.ErrorCode == "Cancelled") throw new OperationCanceledException(token);
                        bool busy = outcome.ErrorCode == "ModelBusy" || outcome.StatusCode == 429;
                        sawBusy |= busy;
                        if (!busy || attempt == tries - 1)
                        {
                            result.Escalations.Add(Line(executor, outcome.ErrorCode ?? "ModelError"));
                            break;
                        }
                    }
                }
            }

            result.ErrorCode = "ExecutorsExhausted";
            result.Retryable = config.ExhaustionPolicy == AiExhaustionPolicyEnum.Defer || sawBusy;
            result.LatencyMs = watch.ElapsedMilliseconds;
            return result;
        }

        /// <summary>
        /// Embed inputs through the Embedding role's hierarchy (first executor that succeeds).
        /// </summary>
        /// <param name="inputs">Inputs.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One vector per input, and the endpoint used (vector dimensions are recorded per endpoint).</returns>
        /// <exception cref="ArgumentNullException">inputs is null.</exception>
        /// <exception cref="ApiErrorException">404 NotAvailable, 503 NoExecutors, 503 ExecutorsExhausted.</exception>
        public async Task<KeyValuePair<string, List<float[]>>> EmbedAsync(List<string> inputs, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(inputs);
            _Ai.EnsureAvailable();
            TaskRoleDefinition definition = FindDefinition("Embedding")!;
            AiTaskRoleConfig config = await ConfigAsync(definition.Role, token).ConfigureAwait(false);
            List<AiExecutor> executors = config.Executors.Where(e => e.Enabled).OrderBy(e => e.Position).ToList();
            if (executors.Count == 0) throw new ApiErrorException(503, "NoExecutors", "The Embedding role has no executors. Configure it in the admin console.");
            List<string> escalations = new List<string>();
            foreach (AiExecutor executor in executors)
            {
                string? skip = await SkipReasonAsync(definition, executor, false, token).ConfigureAwait(false);
                if (skip != null)
                {
                    escalations.Add(Line(executor, skip));
                    continue;
                }
                ModelEndpoint endpoint = (await _Ai.App.Database.Ai.Endpoints.ReadAsync(executor.EndpointId!, token).ConfigureAwait(false))!;
                try
                {
                    using (AiCallScope.BeginRole(definition.Role, executor.Position, escalations.Count == 0 ? null : String.Join(" | ", escalations)))
                    {
                        return new KeyValuePair<string, List<float[]>>(endpoint.Id, await _Ai.Invoker.EmbedAsync(endpoint, inputs, null, false, token).ConfigureAwait(false));
                    }
                }
                catch (ApiErrorException e)
                {
                    escalations.Add(Line(executor, e.Error));
                }
            }

            throw new ApiErrorException(503, "ExecutorsExhausted", "No Embedding executor answered: " + String.Join("; ", escalations));
        }

        /// <summary>
        /// Run an agentic task through a role's executor hierarchy with typed job tools (docs/HARNESSES.md). A CLI agent
        /// harness executor (worker instances, inside a job item) reaches the tools through a job-scoped MCP server; an
        /// API endpoint executor runs them in the engine's tool loop. Either way every call goes through the same
        /// validation and handlers, and the outcome lists every call. An executor that fails is passed over with its
        /// reason, like <see cref="CompleteAsync"/>; handlers must store results idempotently, because a later executor
        /// may report the same result again.
        /// </summary>
        /// <param name="role">Role key.</param>
        /// <param name="request">Task.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Outcome; never throws for provider or harness failures.</returns>
        /// <exception cref="ArgumentNullException">request is null.</exception>
        /// <exception cref="ArgumentException">Empty or too long instructions, a permission mode other than Restricted or ReadOnly, or invalid tools.</exception>
        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        public async Task<AgentOutcome> RunAgentAsync(string role, AgentRequest request, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(request);
            Stopwatch watch = Stopwatch.StartNew();
            AgentOutcome result = new AgentOutcome();
            string instructions = (request.Instructions ?? String.Empty).Trim();
            if (instructions.Length == 0 || instructions.Length > AgentRequest.MaxInstructionCharacters)
                throw new ArgumentException("Instructions are required (at most " + AgentRequest.MaxInstructionCharacters + " characters).", nameof(request));
            if (request.PermissionMode != CliPermissionMode.Restricted && request.PermissionMode != CliPermissionMode.ReadOnly)
                throw new ArgumentException("Agent tasks run in Restricted or ReadOnly mode only.", nameof(request));
            AgentToolSet tools = new AgentToolSet(request.Tools, request.MaxToolCalls ?? _Ai.App.Settings.Jobs.HarnessMaxToolCalls, _Ai.App.Logger);
            TaskRoleDefinition? definition = FindDefinition(role);
            if (definition == null || definition.Kind != ModelEndpointKindEnum.Completion)
            {
                result.ErrorCode = "UnknownRole";
                return result;
            }
            if (!_Ai.IsAvailable)
            {
                result.ErrorCode = "NotAvailable";
                return result;
            }

            AiTaskRoleConfig config = await ConfigAsync(definition.Role, token).ConfigureAwait(false);
            List<AiExecutor> executors = config.Executors.Where(e => e.Enabled).OrderBy(e => e.Position).ToList();
            if (executors.Count == 0)
            {
                result.ErrorCode = "NoExecutors";
                result.LatencyMs = watch.ElapsedMilliseconds;
                return result;
            }

            ModelReasoningEffortEnum? effort = config.ReasoningEffort ?? definition.DefaultEffort;
            int rounds = config.ExhaustionPolicy == AiExhaustionPolicyEnum.Wait ? 3 : 1;
            bool sawBusy = false;
            for (int round = 0; round < rounds; round++)
            {
                if (round > 0) await Task.Delay(TimeSpan.FromSeconds(2 * round), token).ConfigureAwait(false);
                foreach (AiExecutor executor in executors)
                {
                    token.ThrowIfCancellationRequested();
                    string? skip = await SkipReasonAsync(definition, executor, true, token).ConfigureAwait(false);
                    if (skip != null)
                    {
                        result.Escalations.Add(Line(executor, skip));
                        continue;
                    }

                    if (executor.Runtime != AiExecutorRuntimeEnum.ApiEndpoint)
                    {
                        HarnessCallResult harness;
                        using (AiCallScope.BeginRole(definition.Role, executor.Position, Escalation(result.Escalations)))
                        {
                            harness = await _Ai.Harnesses.RunAsync(new HarnessCall
                            {
                                Runtime = executor.Runtime,
                                Model = executor.Model,
                                MuxEndpoint = executor.EndpointId,
                                Prompt = instructions,
                                SystemPrompt = await HarnessSystemAsync(tools, request.SystemPrompt, token).ConfigureAwait(false),
                                PermissionMode = request.PermissionMode,
                                TimeoutMs = HarnessTimeoutMs(request.TimeoutSeconds),
                                Tools = tools,
                                AllowWebResearch = request.AllowWebResearch
                            }, token).ConfigureAwait(false);
                        }
                        result.CostMicros += harness.CostMicros;
                        if (harness.Success)
                        {
                            result.Success = true;
                            result.Text = harness.Text;
                            result.Runtime = executor.Runtime.ToString();
                            result.ExecutorPosition = executor.Position;
                            result.EndpointId = executor.EndpointId;
                            result.Model = harness.Model;
                            result.PromptTokens = harness.PromptTokens;
                            result.CompletionTokens = harness.CompletionTokens;
                            result.ToolCalls = tools.Records();
                            result.LatencyMs = watch.ElapsedMilliseconds;
                            return result;
                        }
                        sawBusy |= harness.Busy;
                        result.Escalations.Add(Line(executor, harness.ErrorCode ?? "HarnessFailed"));
                        continue;
                    }

                    ModelEndpoint endpoint = (await _Ai.App.Database.Ai.Endpoints.ReadAsync(executor.EndpointId!, token).ConfigureAwait(false))!;
                    List<ChatMessage> messages = new List<ChatMessage>
                    {
                        ChatMessage.System(AgentRules(tools, false) + (String.IsNullOrWhiteSpace(request.SystemPrompt) ? String.Empty : "\n\n" + request.SystemPrompt!.Trim())),
                        ChatMessage.User(instructions)
                    };
                    int tries = config.AvailabilityPolicy == AiAvailabilityPolicyEnum.Wait ? 3 : 1;
                    for (int attempt = 0; attempt < tries; attempt++)
                    {
                        if (attempt > 0) await Task.Delay(TimeSpan.FromMilliseconds(750 * attempt), token).ConfigureAwait(false);
                        tools.BeginAttempt(executor.Position, AiExecutorRuntimeEnum.ApiEndpoint);
                        CompletionOutcome outcome;
                        using (AiCallScope.BeginRole(definition.Role, executor.Position, Escalation(result.Escalations)))
                        {
                            outcome = await _Ai.AgentLoop.RunAsync(endpoint, messages, tools, request.MaxRounds, request.MaxTokens, effort, false, token).ConfigureAwait(false);
                        }
                        result.CostMicros += outcome.CostMicros;
                        if (outcome.Success)
                        {
                            result.Success = true;
                            result.Text = outcome.Text;
                            result.Runtime = AiExecutorRuntimeEnum.ApiEndpoint.ToString();
                            result.ExecutorPosition = executor.Position;
                            result.EndpointId = endpoint.Id;
                            result.Model = endpoint.CallModel;
                            result.PromptTokens = outcome.PromptTokens;
                            result.CompletionTokens = outcome.CompletionTokens;
                            result.ToolCalls = tools.Records();
                            result.LatencyMs = watch.ElapsedMilliseconds;
                            return result;
                        }
                        if (outcome.ErrorCode == "Cancelled") throw new OperationCanceledException(token);
                        bool busy = outcome.ErrorCode == "ModelBusy" || outcome.StatusCode == 429;
                        sawBusy |= busy;
                        if (!busy || attempt == tries - 1)
                        {
                            result.Escalations.Add(Line(executor, outcome.ErrorCode ?? "ModelError"));
                            break;
                        }
                    }
                }
            }

            result.ErrorCode = "ExecutorsExhausted";
            result.Retryable = config.ExhaustionPolicy == AiExhaustionPolicyEnum.Defer || sawBusy;
            result.ToolCalls = tools.Records();
            result.LatencyMs = watch.ElapsedMilliseconds;
            return result;
        }

        /// <summary>
        /// Whether a role has at least one enabled executor (background jobs check this before planning work, so rows
        /// wait instead of failing while a role is not configured).
        /// </summary>
        /// <param name="role">Role key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the role is in the catalog and has an enabled executor.</returns>
        public async Task<bool> HasExecutorsAsync(string role, CancellationToken token = default)
        {
            TaskRoleDefinition? definition = FindDefinition(role);
            if (definition == null) return false;
            AiTaskRoleConfig config = await ConfigAsync(definition.Role, token).ConfigureAwait(false);
            return config.Executors.Any(e => e.Enabled);
        }

        /// <summary>
        /// Drop the cached hierarchies (after a save).
        /// </summary>
        public void Invalidate()
        {
            _Configs = null;
        }

        /// <summary>
        /// GET /api/v1/admin/ai/roles (SystemAdmin).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Every role of the catalog.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden.</exception>
        public async Task<EnumerationResult<AiTaskRoleView>> EnumerateAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            RequireAdmin(actor);
            EnumerationRequest request = _Ai.App.Enumeration.Prepare(query, AiCollections.AdminRoles, true);
            List<AiTaskRoleView> source = await ListCatalogAsync(actor, token).ConfigureAwait(false);
            return CatalogEnumeration.Page(request, source, item => item.Role, search: item => item.Role + " " + item.Title + " " + item.Description, labels: item => item.Labels, tags: item => item.Tags);
        }

        private async Task<List<AiTaskRoleView>> ListCatalogAsync(ContentActor actor, CancellationToken token = default)
        {
            RequireAdmin(actor);
            Dictionary<string, AiTaskRoleConfig> saved = await _Ai.App.Database.Ai.Roles.ReadAllAsync(token).ConfigureAwait(false);
            Dictionary<string, ModelEndpoint> endpoints = await EndpointsAsync(saved.Values.SelectMany(c => c.Executors), token).ConfigureAwait(false);
            return Definitions.Select(d => View(d, saved.TryGetValue(d.Role, out AiTaskRoleConfig? c) ? c : null, endpoints)).ToList();
        }

        /// <summary>
        /// GET /api/v1/admin/ai/roles/{role} (SystemAdmin).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="role">Role key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Role.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<AiTaskRoleView> GetAsync(ContentActor actor, string role, CancellationToken token = default)
        {
            RequireAdmin(actor);
            TaskRoleDefinition definition = Require(role);
            Dictionary<string, AiTaskRoleConfig> saved = await _Ai.App.Database.Ai.Roles.ReadAllAsync(token).ConfigureAwait(false);
            saved.TryGetValue(definition.Role, out AiTaskRoleConfig? config);
            Dictionary<string, ModelEndpoint> endpoints = await EndpointsAsync(config?.Executors ?? new List<AiExecutor>(), token).ConfigureAwait(false);
            return View(definition, config, endpoints);
        }

        /// <summary>
        /// PUT /api/v1/admin/ai/roles/{role} (SystemAdmin): replace the role's hierarchy and policies. Audited as
        /// ai.role.update.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="role">Role key.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Role.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound.</exception>
        public async Task<AiTaskRoleView> SaveAsync(ContentActor actor, RequestInfo info, string role, AiTaskRoleRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            RequireAdmin(actor);
            TaskRoleDefinition definition = Require(role);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            AiTaskRoleConfig config = new AiTaskRoleConfig { Role = definition.Role, UpdatedByUserId = actor.UserId, UpdatedUtc = DateTime.UtcNow };
            if (!String.IsNullOrWhiteSpace(request.ReasoningEffort))
            {
                if (Enum.TryParse(request.ReasoningEffort, true, out ModelReasoningEffortEnum effort) && Enum.IsDefined(effort)) config.ReasoningEffort = effort;
                else errors["reasoningEffort"] = "Minimal, Low, Medium, or High.";
            }
            if (!String.IsNullOrWhiteSpace(request.AvailabilityPolicy))
            {
                if (Enum.TryParse(request.AvailabilityPolicy, true, out AiAvailabilityPolicyEnum a) && Enum.IsDefined(a)) config.AvailabilityPolicy = a;
                else errors["availabilityPolicy"] = "FallThrough or Wait.";
            }
            if (!String.IsNullOrWhiteSpace(request.ExhaustionPolicy))
            {
                if (Enum.TryParse(request.ExhaustionPolicy, true, out AiExhaustionPolicyEnum x) && Enum.IsDefined(x)) config.ExhaustionPolicy = x;
                else errors["exhaustionPolicy"] = "Fail, Defer, or Wait.";
            }
            List<AiExecutorRequest> executors = request.Executors ?? new List<AiExecutorRequest>();
            if (request.Executors == null) errors["executors"] = "Required (an empty list clears the hierarchy).";
            if (executors.Count > MaxExecutors) errors["executors"] = "At most " + MaxExecutors + " executors.";
            for (int i = 0; i < executors.Count && i < MaxExecutors; i++)
            {
                AiExecutorRequest e = executors[i] ?? new AiExecutorRequest();
                string field = "executors[" + i + "]";
                if (!Enum.TryParse(e.Runtime, true, out AiExecutorRuntimeEnum runtime) || !Enum.IsDefined(runtime) || String.IsNullOrWhiteSpace(e.Runtime))
                {
                    errors[field + ".runtime"] = "ApiEndpoint, ClaudeCode, Codex, GeminiCli, CursorCli, Mux, OpenCode, or Pi.";
                    continue;
                }
                if (runtime != AiExecutorRuntimeEnum.ApiEndpoint && !definition.AllowsHarness)
                {
                    errors[field + ".runtime"] = definition.Interactive ? "Interactive roles use API endpoints only; harnesses never run in request paths." : "This role uses API endpoints only.";
                    continue;
                }
                string? model = String.IsNullOrWhiteSpace(e.Model) ? null : e.Model.Trim();
                if (model != null && model.Length > 200) errors[field + ".model"] = "At most 200 characters.";
                if (runtime == AiExecutorRuntimeEnum.ApiEndpoint)
                {
                    if (model != null) errors[field + ".model"] = "API endpoint executors use the endpoint's model; set the model on the endpoint.";
                    ModelEndpoint? endpoint = String.IsNullOrWhiteSpace(e.EndpointId) ? null : await _Ai.App.Database.Ai.Endpoints.ReadAsync(e.EndpointId.Trim(), token).ConfigureAwait(false);
                    if (endpoint == null || endpoint.DeletedUtc != null) errors[field + ".endpointId"] = "An existing model endpoint is required.";
                    else if (endpoint.Kind != definition.Kind) errors[field + ".endpointId"] = "This role needs a " + definition.Kind + " endpoint.";
                }
                else if (runtime != AiExecutorRuntimeEnum.Mux && !String.IsNullOrWhiteSpace(e.EndpointId))
                {
                    errors[field + ".endpointId"] = "Only API endpoint and Mux executors take an endpoint.";
                }
                config.Executors.Add(new AiExecutor
                {
                    Position = i,
                    Runtime = runtime,
                    EndpointId = String.IsNullOrWhiteSpace(e.EndpointId) ? null : e.EndpointId.Trim(),
                    Model = runtime == AiExecutorRuntimeEnum.ApiEndpoint ? null : model,
                    Enabled = e.Enabled
                });
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            await _Ai.App.Database.Ai.Roles.SaveAsync(config, token).ConfigureAwait(false);
            Invalidate();
            await _Ai.App.Audit.WriteAsync(info, "ai.role.update", "AiTaskRole", definition.Role, config.Executors.Count + " executors", token).ConfigureAwait(false);
            return await GetAsync(actor, definition.Role, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/ai/roles/{role}/test (SystemAdmin): send a test prompt through the hierarchy (recorded as
        /// test traffic).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="role">Role key.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentNullException">request is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 400 NotTestable, 403 Forbidden, 404 NotFound, 404 NotAvailable.</exception>
        public async Task<AiRoleTestResult> TestAsync(ContentActor actor, string role, AiRoleTestRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            RequireAdmin(actor);
            TaskRoleDefinition definition = Require(role);
            if (definition.Kind != ModelEndpointKindEnum.Completion) throw new ApiErrorException(400, "NotTestable", "Embedding roles are tested from the endpoint's own test.");
            string prompt = (request.Prompt ?? String.Empty).Trim();
            if (prompt.Length < 1 || prompt.Length > 4000) throw ApiErrorException.Field("prompt", "1 to 4000 characters.");
            _Ai.EnsureAvailable();
            Invalidate();
            RoleOutcome outcome = await PromptAsync(definition.Role, prompt, null, 400, token, true).ConfigureAwait(false);
            return new AiRoleTestResult
            {
                Success = outcome.Success,
                Text = outcome.Success ? outcome.Text : null,
                ExecutorPosition = outcome.ExecutorPosition,
                EndpointId = outcome.EndpointId,
                Model = outcome.Model,
                Escalations = outcome.Escalations,
                ErrorCode = outcome.ErrorCode,
                LatencyMs = outcome.LatencyMs,
                CostUsd = Math.Round(outcome.CostMicros / 1000000d, 6)
            };
        }

        /// <summary>
        /// POST /api/v1/admin/ai/calls/enumerate (SystemAdmin): the call ledger, newest first.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden.</exception>
        public async Task<EnumerationResult<AiCallView>> EnumerateCallsAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            RequireAdmin(actor);
            EnumerationRequest request = _Ai.App.Enumeration.Prepare(query, AiCollections.AdminCalls, true);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            AiCallFilter filter = new AiCallFilter
            {
                Roles = DirectoryFilters.Strings(request.Query, "role"),
                Purposes = DirectoryFilters.Enums<AssistantPurposeEnum>(request.Query, "purpose", errors),
                Outcomes = DirectoryFilters.Enums<AiUsageOutcomeEnum>(request.Query, "outcome", errors),
                EndpointId = request.Query.FilterString("endpointId"),
                JobKey = request.Query.FilterString("jobKey"),
                JobRunId = request.Query.FilterString("jobRunId"),
                AssistantId = request.Query.FilterString("assistantId")
            };
            DirectoryFilters.ThrowIfAny(errors);
            EnumerationResult<AiCallRecord> page = await _Ai.App.Database.Ai.Calls.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            return page.With(page.Objects.Select(c => WithDetail(c, new AiCallView
            {
                Id = c.Id,
                CreatedUtc = c.OccurredUtc,
                Purpose = c.Purpose.ToString(),
                Role = c.Role,
                AssistantId = c.AssistantId,
                EndpointId = c.EndpointId,
                Runtime = c.Runtime.ToString(),
                Model = c.Model,
                ExecutorPosition = c.ExecutorPosition,
                EscalationReason = c.EscalationReason,
                Outcome = c.Outcome.ToString(),
                ErrorCode = c.ErrorCode,
                IsFallback = c.IsFallback,
                IsTest = c.IsTest,
                PromptTokens = c.PromptTokens,
                CompletionTokens = c.CompletionTokens,
                ReasoningTokens = c.ReasoningTokens,
                LatencyMs = c.LatencyMs,
                CostUsd = Math.Round(c.CostMicros / 1000000d, 6),
                JobKey = c.JobKey,
                JobRunId = c.JobRunId,
                JobItemId = c.JobItemId
            })).ToList());
        }

        #endregion

        #region Private-Methods

        private static AiCallView WithDetail(AiCallRecord record, AiCallView view)
        {
            record.Detail.CopyTo(view);
            return view;
        }

        private static void RequireAdmin(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (!actor.IsSystemAdmin) throw new ApiErrorException(403, "Forbidden", "Only administrators can read or change AI task roles.");
        }

        private TaskRoleDefinition Require(string role)
        {
            return FindDefinition(role) ?? throw ApiErrorException.NotFound("Task role");
        }

        private async Task<AiTaskRoleConfig> ConfigAsync(string role, CancellationToken token)
        {
            Dictionary<string, AiTaskRoleConfig>? configs = _Configs;
            if (configs == null || (DateTime.UtcNow - _LoadedUtc).TotalSeconds >= _CacheSeconds)
            {
                await _Load.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    configs = _Configs;
                    if (configs == null || (DateTime.UtcNow - _LoadedUtc).TotalSeconds >= _CacheSeconds)
                    {
                        configs = await _Ai.App.Database.Ai.Roles.ReadAllAsync(token).ConfigureAwait(false);
                        _Configs = configs;
                        _LoadedUtc = DateTime.UtcNow;
                    }
                }
                finally
                {
                    _Load.Release();
                }
            }

            return configs.TryGetValue(role, out AiTaskRoleConfig? config) ? config : new AiTaskRoleConfig { Role = role };
        }

        private async Task<string?> SkipReasonAsync(TaskRoleDefinition definition, AiExecutor executor, bool withTools, CancellationToken token)
        {
            if (executor.Runtime != AiExecutorRuntimeEnum.ApiEndpoint)
            {
                if (!definition.AllowsHarness) return "this role does not allow harnesses";
                string? refusal = _Ai.Harnesses.RefusalReason();
                if (refusal != null) return refusal;
                return withTools ? HarnessLaunchPlanner.ToolsUnsupportedReason(executor.Runtime) : null;
            }
            if (String.IsNullOrWhiteSpace(executor.EndpointId)) return "no endpoint";
            ModelEndpoint? endpoint = await _Ai.App.Database.Ai.Endpoints.ReadAsync(executor.EndpointId, token).ConfigureAwait(false);
            if (endpoint == null || endpoint.DeletedUtc != null) return "endpoint deleted";
            if (!endpoint.Enabled) return "endpoint disabled";
            if (endpoint.Kind != definition.Kind) return "endpoint is not a " + definition.Kind + " endpoint";
            if (!ModelInvoker.IsRoutable(endpoint)) return "endpoint unhealthy";
            return null;
        }

        private static string Line(AiExecutor executor, string reason)
        {
            return "#" + executor.Position + " " + executor.Runtime + (executor.EndpointId == null ? String.Empty : " " + executor.EndpointId) + ": " + reason;
        }

        private static string? Escalation(List<string> escalations)
        {
            return escalations.Count == 0 ? null : String.Join(" | ", escalations);
        }

        private int HarnessTimeoutMs(int? seconds)
        {
            return Math.Clamp(seconds ?? _Ai.App.Settings.Jobs.HarnessTimeoutSeconds, 30, 3600) * 1000;
        }

        private async Task<string> HarnessSystemAsync(AgentToolSet? tools, string? extra, CancellationToken token)
        {
            // Owner requirement R7: the locked floor and the scope guardrail lead every harness's instructions.
            StringBuilder system = new StringBuilder(await _Ai.Prompts.GuardAsync(token).ConfigureAwait(false) ?? ScopeGuard.Floor);
            system.Append("\n\n").Append(AgentRules(tools, true));
            if (!String.IsNullOrWhiteSpace(extra)) system.Append("\n\n").Append(extra.Trim());
            return system.ToString();
        }

        private static string AgentRules(AgentToolSet? tools, bool harness)
        {
            StringBuilder rules = new StringBuilder();
            if (harness) rules.Append("You are running as a background agent for one job item, in an empty, throwaway working directory. Do not create, change, or delete files, and do not run shell commands. ");
            else rules.Append("You are running as a background agent for one job item. ");
            if (tools != null)
            {
                rules.Append("Report every result by calling the provided tools").Append(harness ? " of the " + JobToolServer.ServerName + " MCP server" : String.Empty)
                    .Append(" (").Append(String.Join(", ", tools.Tools.Select(t => t.Name))).Append("); anything you only write in text is discarded. ")
                    .Append("If a tool refuses a call, read its message, correct the arguments, and call it again. ");
            }
            rules.Append("Finish with a short summary of what you did.");
            return rules.ToString();
        }

        private static string HarnessPrompt(List<ChatMessage> messages, out string? system)
        {
            List<string> systems = messages.Where(m => String.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase) && !String.IsNullOrWhiteSpace(m.Text)).Select(m => m.Text!).ToList();
            List<ChatMessage> rest = messages.Where(m => !String.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase) && !String.IsNullOrWhiteSpace(m.Text)).ToList();
            system = systems.Count == 0 ? null : String.Join("\n\n", systems);
            if (rest.Count == 0) return "Follow the instructions above.";
            if (rest.Count == 1) return rest[0].Text!;
            StringBuilder prompt = new StringBuilder("Conversation so far:\n");
            foreach (ChatMessage m in rest.Take(rest.Count - 1)) prompt.Append('\n').Append(String.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "Assistant" : "User").Append(": ").Append(m.Text);
            prompt.Append("\n\nCurrent request:\n").Append(rest[rest.Count - 1].Text);
            return prompt.ToString();
        }

        private async Task<Dictionary<string, ModelEndpoint>> EndpointsAsync(IEnumerable<AiExecutor> executors, CancellationToken token)
        {
            Dictionary<string, ModelEndpoint> result = new Dictionary<string, ModelEndpoint>(StringComparer.Ordinal);
            foreach (string id in executors.Where(e => e.Runtime == AiExecutorRuntimeEnum.ApiEndpoint && e.EndpointId != null).Select(e => e.EndpointId!).Distinct().Take(200))
            {
                ModelEndpoint? endpoint = await _Ai.App.Database.Ai.Endpoints.ReadAsync(id, token).ConfigureAwait(false);
                if (endpoint != null) result[id] = endpoint;
            }

            return result;
        }

        private AiTaskRoleView View(TaskRoleDefinition d, AiTaskRoleConfig? config, Dictionary<string, ModelEndpoint> endpoints)
        {
            AiTaskRoleView view = new AiTaskRoleView
            {
                Role = d.Role,
                Title = d.Title,
                Description = d.Description,
                Kind = d.Kind.ToString(),
                AllowsHarness = d.AllowsHarness,
                Interactive = d.Interactive,
                Suggested = d.Suggested,
                ReasoningEffort = (config?.ReasoningEffort ?? d.DefaultEffort)?.ToString(),
                AvailabilityPolicy = (config?.AvailabilityPolicy ?? AiAvailabilityPolicyEnum.FallThrough).ToString(),
                ExhaustionPolicy = (config?.ExhaustionPolicy ?? AiExhaustionPolicyEnum.Fail).ToString(),
                UpdatedByUserId = config?.UpdatedByUserId,
                UpdatedUtc = config?.UpdatedUtc
            };
            foreach (AiExecutor e in (config?.Executors ?? new List<AiExecutor>()).OrderBy(x => x.Position))
            {
                AiExecutorView ev = new AiExecutorView { Position = e.Position, Runtime = e.Runtime.ToString(), EndpointId = e.EndpointId, Model = e.Model, Enabled = e.Enabled };
                if (e.Runtime == AiExecutorRuntimeEnum.ApiEndpoint)
                {
                    if (e.EndpointId != null && endpoints.TryGetValue(e.EndpointId, out ModelEndpoint? endpoint) && endpoint.DeletedUtc == null)
                    {
                        ev.EndpointName = endpoint.Name;
                        ev.Model = endpoint.CallModel;
                        ev.Health = endpoint.HealthStatus.ToString();
                        if (!endpoint.Enabled) ev.Problem = "Endpoint disabled";
                        else if (!ModelInvoker.IsRoutable(endpoint)) ev.Problem = "Endpoint unhealthy";
                    }
                    else
                    {
                        ev.Problem = "Endpoint deleted";
                    }
                }
                else
                {
                    ev.Problem = _Ai.App.Settings.Jobs.RunHarnesses ? null : "Harnesses run only on worker instances";
                }
                view.Executors.Add(ev);
            }
            view.Ready = view.Executors.Any(x => x.Enabled && x.Problem == null && x.Runtime == "ApiEndpoint");
            return view;
        }

        #endregion
    }
}
