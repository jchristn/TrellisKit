namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Security;

    /// <summary>
    /// The AI models and assistants service graph of one property (docs/AI_CONTRACT.md section 19). Built by
    /// <see cref="ApplicationServices"/>; route registrars and workers take their AI services from here.
    /// Thread safety: every service is safe to share across requests.
    /// </summary>
    public class AiServices
    {
        #region Public-Members

        /// <summary>
        /// Application services of the property.
        /// </summary>
        public ApplicationServices App { get; }

        /// <summary>
        /// Secret protector (secrets.encryptionKey, read live).
        /// </summary>
        public ISecretProtector Protector { get; }

        /// <summary>
        /// PolyPrompt client factory.
        /// </summary>
        public IModelClientFactory Factory { get; }

        /// <summary>
        /// Vector store for knowledge (RecallDB by default). Tests may replace it.
        /// </summary>
        public IVectorStore VectorStore { get; set; }

        /// <summary>
        /// Endpoint concurrency and queue.
        /// </summary>
        public EndpointConcurrencyGate Gate { get; }

        /// <summary>
        /// Usage rollups, cost, budgets, and analytics.
        /// </summary>
        public AiUsageService Usage { get; }

        /// <summary>
        /// Model call wrapper.
        /// </summary>
        public ModelInvoker Invoker { get; }

        /// <summary>
        /// The assistant tool registry (docs/AI_CONTRACT.md section 8.1). Feature modules register their tools here
        /// during construction; findGyms is a built-in alias of findVenues.
        /// </summary>
        public AssistantToolRegistry Tools { get; } = new AssistantToolRegistry();

        /// <summary>
        /// Model endpoints.
        /// </summary>
        public ModelEndpointService Endpoints { get; }

        /// <summary>
        /// Endpoint health checks.
        /// </summary>
        public ModelEndpointHealthService Health { get; }

        /// <summary>
        /// Input and output moderation.
        /// </summary>
        public AssistantModerationService Moderation { get; }

        /// <summary>
        /// Administrator-editable prompts and the scope guard of every model call.
        /// </summary>
        public PromptService Prompts { get; }

        /// <summary>
        /// Task roles and their executor hierarchies, and the call ledger (docs/INTEGRATION.md).
        /// </summary>
        public TaskRoleService Roles { get; }

        /// <summary>
        /// CLI agent harness calls (worker instances, job items only; docs/HARNESSES.md).
        /// </summary>
        public HarnessService Harnesses { get; }

        /// <summary>
        /// Harness profiles: the administrator's model, limits, and credential per harness runtime.
        /// </summary>
        public HarnessProfileService HarnessProfiles { get; }

        /// <summary>
        /// The job tool loop of API endpoint executors (docs/HARNESSES.md).
        /// </summary>
        public AgentToolLoop AgentLoop { get; }

        /// <summary>
        /// Knowledge retrieval.
        /// </summary>
        public KnowledgeRetrievalService Knowledge { get; }

        /// <summary>
        /// Knowledge ingestion into the vector store.
        /// </summary>
        public KnowledgeIndexService Indexer { get; }

        /// <summary>
        /// Assistants.
        /// </summary>
        public AssistantService Assistants { get; }

        /// <summary>
        /// Anonymous visitors of public assistants.
        /// </summary>
        public AssistantVisitorService Visitors { get; }

        /// <summary>
        /// Conversations, messages, feedback, and the administrator conversation browser.
        /// </summary>
        public ConversationService Conversations { get; }

        /// <summary>
        /// The tool registry administrator view and the property-wide kill switch per tool.
        /// </summary>
        public AssistantToolService ToolService { get; }

        /// <summary>
        /// The bounded tool execution loop of a turn.
        /// </summary>
        public AssistantToolLoop ToolLoop { get; }

        /// <summary>
        /// The chat pipeline.
        /// </summary>
        public AssistantChatService Chat { get; }

        /// <summary>
        /// Reports and moderation snapshots of assistant messages.
        /// </summary>
        public AssistantReportService Reports { get; }

        /// <summary>
        /// The live scope benchmark and the prompt publish gate (docs/BENCHMARKS.md).
        /// </summary>
        public Benchmark.ScopeBenchmarkService Benchmarks { get; }

        /// <summary>
        /// Whether AI is available for the property: the property's AI switch (seeded from property.json features.ai,
        /// switched in the admin portal; docs/TRELLISKIT.md section 9.5) and the deployment setting ai.enabled.
        /// </summary>
        public bool IsAvailable
        {
            get { return App.AiSwitch.Enabled && App.Settings.Ai.Enabled; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Build the AI service graph.
        /// </summary>
        /// <param name="app">Application services of the property.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public AiServices(ApplicationServices app)
        {
            App = app ?? throw new ArgumentNullException(nameof(app));
            Protector = new SecretBoxProtector(() => SecretBoxProvider.For(app.Settings.Secrets), () => SecretBox.IsValidKey(app.Settings.Secrets.EncryptionKey));
            Factory = new ModelClientFactory(Protector, () => app.Settings.Ai.CaptureCallDetails);
            VectorStore = new RecallDbVectorStore(app.Settings.Ai);
            Gate = new EndpointConcurrencyGate();
            Usage = new AiUsageService(app);
            Invoker = new ModelInvoker(Factory, Gate, Usage, app.Settings.Ai, app.Logger);
            Prompts = new PromptService(this);
            Roles = new TaskRoleService(this);
            Harnesses = new HarnessService(this);
            HarnessProfiles = new HarnessProfileService(this);
            AgentLoop = new AgentToolLoop(this);
            Invoker.GuardProvider = Prompts.GuardAsync;
            Endpoints = new ModelEndpointService(this);
            Health = new ModelEndpointHealthService(this);
            Moderation = new AssistantModerationService(this);
            Knowledge = new KnowledgeRetrievalService(this);
            Indexer = new KnowledgeIndexService(this);
            Assistants = new AssistantService(this);
            Visitors = new AssistantVisitorService(this);
            Conversations = new ConversationService(this);
            ToolService = new AssistantToolService(this);
            ToolLoop = new AssistantToolLoop(this);
            Chat = new AssistantChatService(this);
            CoreAssistantTools.Register(this);
            Reports = new AssistantReportService(this);
            Benchmarks = new Benchmark.ScopeBenchmarkService(this);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Throw 404 NotAvailable unless AI is available for the property.
        /// </summary>
        /// <exception cref="Core.Exceptions.ApiErrorException">404 NotAvailable.</exception>
        public void EnsureAvailable()
        {
            if (!IsAvailable) throw AiErrors.NotAvailable();
        }

        /// <summary>
        /// Delete every conversation, feedback row, and daily usage row of a member (account deletion).
        /// </summary>
        /// <param name="userId">Member id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public Task EraseForAccountAsync(string userId, CancellationToken token)
        {
            return App.Database.Ai.Conversations.DeleteForUserAsync(userId, token);
        }

        #endregion
    }
}
