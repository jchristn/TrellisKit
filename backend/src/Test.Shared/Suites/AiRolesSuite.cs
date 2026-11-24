namespace Test.Shared.Suites
{
    using PolyPrompt.Models;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared.Fakes;
    using Touchstone.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services.Ai;
    using TrellisKit.Server.Services.Jobs;

    /// <summary>
    /// AI task roles and the call ledger (docs/INTEGRATION.md): executor hierarchies with fall
    /// through, validation (harnesses only on agentic background roles), ledger rows with role, position, and
    /// escalation, model cost attributed to job items, the Anthropic no-sampling rule, and administrator-only access.
    /// </summary>
    public static class AiRolesSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "AiRoles";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "AI task roles and call ledger (" + TestEnvironment.Provider + ")", new[]
            {
                AiTestKit.Case(Id, "Hierarchy.FallsThrough", "A failing first executor falls through to the second; the test reports the answering executor and the escalation, and the ledger records both calls with role, position, and reason", FallThroughAsync),
                AiTestKit.Case(Id, "Hierarchy.Validation", "Harness executors are refused on interactive and API-only roles and accepted on agentic roles, wrong-kind and missing endpoints are 400, more than 8 executors is 400, unknown roles are 404, and members are refused", ValidationAsync),
                AiTestKit.Case(Id, "Ledger.JobCostAttribution", "A job whose handler calls a role carries the model cost on its item and run, and the ledger rows name the job, run, item, and role", JobCostAsync),
                AiTestKit.Case(Id, "Hierarchy.NotConfigured", "A role without executors answers NoExecutors without calling a model, and harness-only hierarchies are not ready on API instances", NotConfiguredAsync),
                AiTestKit.Case(Id, "Ledger.AssistantChat", "Assistant chat calls are in the ledger with their purpose and assistant", AssistantLedgerAsync),
                SuiteCases.Http(Id, "Options.AnthropicNoSampling", "Anthropic requests never carry temperature or top_p, other providers keep them, and a role's reasoning effort overrides the endpoint's", OptionsAsync)
            });
        }

        #endregion

        #region Private-Methods

        private const string Roles = "/api/v1/admin/ai/roles";

        private static async Task<string> EndpointAsync(HttpFixture f, FakeModelServer model, string name, string kind = "Completion", object? extra = null)
        {
            return await AiTestKit.EndpointAsync(f, AiTestKit.OllamaEndpoint(model, name + " " + Guid.NewGuid().ToString("N").Substring(0, 6), kind, extra), CancellationToken.None).ConfigureAwait(false);
        }

        private static async Task FallThroughAsync(HttpFixture f, FakeModelServer model, CancellationToken ct)
        {
            string first = await EndpointAsync(f, model, "First").ConfigureAwait(false);
            string second = await EndpointAsync(f, model, "Second").ConfigureAwait(false);
            JsonElement saved = await Api.ExpectAsync(f, HttpMethod.Put, Roles + "/Extraction", HttpFixture.AdminToken, new
            {
                reasoningEffort = "Low",
                exhaustionPolicy = "Defer",
                executors = new object[] { new { runtime = "ApiEndpoint", endpointId = first }, new { runtime = "ApiEndpoint", endpointId = second } }
            }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2, saved.GetProperty("executors").GetArrayLength(), "Two executors");
            Check.True(saved.GetProperty("ready").GetBoolean(), "Ready");
            model.FailNext = 1;
            model.FailStatus = 500;
            JsonElement test = await Api.ExpectAsync(f, HttpMethod.Post, Roles + "/Extraction/test", HttpFixture.AdminToken, new { prompt = "Extract the community center name from: Example Community Center, Reno NV" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(test.GetProperty("success").GetBoolean(), "Answered");
            Check.Equal(1, test.GetProperty("executorPosition").GetInt32(), "The second executor answered");
            Check.Equal(second, Api.Str(test, "endpointId"), "Second endpoint");
            List<string> escalations = test.GetProperty("escalations").EnumerateArray().Select(e => e.GetString() ?? String.Empty).ToList();
            Check.Equal(1, escalations.Count, "One escalation");
            Check.True(escalations[0].StartsWith("#0", StringComparison.Ordinal), "The first executor was passed over");

            JsonElement calls = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/calls/enumerate", HttpFixture.AdminToken, new { filters = new { role = new[] { "Extraction" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<JsonElement> rows = calls.GetProperty("objects").EnumerateArray().ToList();
            Check.Equal(2, rows.Count, "Both attempts are in the ledger");
            JsonElement ok = rows.First(r => Api.Str(r, "outcome") == "Ok");
            JsonElement failed = rows.First(r => Api.Str(r, "outcome") != "Ok");
            Check.Equal(1, ok.GetProperty("executorPosition").GetInt32(), "Answering position");
            Check.True(Api.Str(ok, "escalationReason").Contains("#0", StringComparison.Ordinal), "Escalation reason recorded");
            Check.Equal(0, failed.GetProperty("executorPosition").GetInt32(), "Failed position");
            Check.Equal("Task", Api.Str(ok, "purpose"), "Task purpose");
            Check.True(ok.GetProperty("isTest").GetBoolean(), "Test traffic");
            Check.True(model.Requests.Any(r => r.Body.Contains(Json(ScopeGuard.Floor), StringComparison.Ordinal)), "Role calls carry the scope guardrail");
            Check.True(await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM auditrecords WHERE action = 'ai.role.update';", ct).ConfigureAwait(false) >= 1, "Audited");
        }

        private static async Task ValidationAsync(HttpFixture f, FakeModelServer model, CancellationToken ct)
        {
            string chat = await EndpointAsync(f, model, "Chat").ConfigureAwait(false);
            string embed = await EndpointAsync(f, model, "Embed", "Embedding").ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, Roles + "/SearchParse", HttpFixture.AdminToken, new { executors = new object[] { new { runtime = "ClaudeCode" } } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, Roles + "/Extraction", HttpFixture.AdminToken, new { executors = new object[] { new { runtime = "Codex" } } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, Roles + "/Extraction", HttpFixture.AdminToken, new { executors = new object[] { new { runtime = "ApiEndpoint", endpointId = embed } } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, Roles + "/Extraction", HttpFixture.AdminToken, new { executors = new object[] { new { runtime = "ApiEndpoint", endpointId = "mep_missing" } } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, Roles + "/Extraction", HttpFixture.AdminToken, new { executors = new object[] { new { runtime = "ApiEndpoint", endpointId = chat, model = "other" } } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, Roles + "/Extraction", HttpFixture.AdminToken, new { executors = Enumerable.Range(0, 9).Select(_ => new { runtime = "ApiEndpoint", endpointId = chat }).ToArray() }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, Roles + "/Extraction", HttpFixture.AdminToken, new { reasoningEffort = "Extreme", executors = new object[0] }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, Roles + "/NoSuchRole", HttpFixture.AdminToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            JsonElement embedding = await Api.ExpectAsync(f, HttpMethod.Put, Roles + "/Embedding", HttpFixture.AdminToken, new { executors = new object[] { new { runtime = "ApiEndpoint", endpointId = embed } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(embedding.GetProperty("ready").GetBoolean(), "Embedding role takes an embedding endpoint");

            JsonElement research = await Api.ExpectAsync(f, HttpMethod.Put, Roles + "/Research", HttpFixture.AdminToken, new
            {
                executors = new object[] { new { runtime = "ClaudeCode", model = "sonnet" }, new { runtime = "ApiEndpoint", endpointId = chat } }
            }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<JsonElement> executors = research.GetProperty("executors").EnumerateArray().ToList();
            Check.Equal("ClaudeCode", Api.Str(executors[0], "runtime"), "Harness first");
            Check.True(Api.Str(executors[0], "problem").Contains("worker", StringComparison.Ordinal), "Harnesses are flagged on API instances");
            Check.True(research.GetProperty("ready").GetBoolean(), "Ready through the API executor");

            JsonElement list = await Api.ExpectAsync(f, HttpMethod.Get, Roles, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(TaskRoleCatalog.All.Count, list.GetProperty("objects").GetArrayLength(), "Every catalog role is listed");
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, Roles, member.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/ai/calls/enumerate", member.AccessToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
        }

        private static async Task JobCostAsync(HttpFixture f, FakeModelServer model, CancellationToken ct)
        {
            string priced = await EndpointAsync(f, model, "Priced", "Completion", new { pricing = new { inputPerMillionTokens = 1000000, outputPerMillionTokens = 1000000 } }).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, Roles + "/Extraction", HttpFixture.AdminToken, new { executors = new object[] { new { runtime = "ApiEndpoint", endpointId = priced } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            TestJobHandler handler = new TestJobHandler { Targets = new List<string> { "site-1" } };
            handler.Behavior = c =>
            {
                RoleOutcome outcome = c.Services.Ai.Roles.PromptAsync("Extraction", "Extract the name from " + c.Item.TargetKey, null, 50, CancellationToken.None).GetAwaiter().GetResult();
                return outcome.Success ? JobItemOutcome.Ok("{\"name\":\"x\"}") : JobItemOutcome.Fail(outcome.ErrorCode ?? "failed");
            };
            f.Services.Jobs.Registry.Register(handler);
            JsonElement run = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/jobs/" + handler.Key + "/run", HttpFixture.AdminToken, new { }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            string runId = Api.Str(run, "id");
            await f.Services.Jobs.ProcessAsync(ct).ConfigureAwait(false);
            JobRun finished = (await f.Services.Database.Jobs.ReadRunAsync(runId, ct).ConfigureAwait(false))!;
            Check.Equal(JobRunStatusEnum.Succeeded, finished.Status, "The job finished");
            Check.True(finished.CostMicros > 0, "Model cost reached the run (" + finished.CostMicros + ")");
            JsonElement calls = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/calls/enumerate", HttpFixture.AdminToken, new { filters = new { jobRunId = runId } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<JsonElement> rows = calls.GetProperty("objects").EnumerateArray().ToList();
            Check.Equal(1, rows.Count, "One call for the run");
            Check.Equal(handler.Key, Api.Str(rows[0], "jobKey"), "Job key");
            Check.Equal("Extraction", Api.Str(rows[0], "role"), "Role");
            Check.True(Api.Str(rows[0], "jobItemId").StartsWith("jbi_", StringComparison.Ordinal), "Job item");
            long ledger = (long)Math.Round(rows[0].GetProperty("costUsd").GetDouble() * 1000000d);
            Check.Equal(ledger, finished.CostMicros, "Run cost equals the ledger cost");
        }

        private static async Task NotConfiguredAsync(HttpFixture f, FakeModelServer model, CancellationToken ct)
        {
            model.ClearRequests();
            RoleOutcome outcome = await f.Services.Ai.Roles.PromptAsync("ContentAssistant", "Draft a post", null, 50, ct).ConfigureAwait(false);
            Check.True(!outcome.Success, "No answer");
            Check.Equal("NoExecutors", outcome.ErrorCode ?? String.Empty, "NoExecutors");
            Check.Equal(0, model.Requests.Count, "No model call");
            JsonElement saved = await Api.ExpectAsync(f, HttpMethod.Put, Roles + "/Research", HttpFixture.AdminToken, new { exhaustionPolicy = "Defer", executors = new object[] { new { runtime = "ClaudeCode" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!saved.GetProperty("ready").GetBoolean(), "A harness-only hierarchy is not ready on an API instance");
            f.Services.Ai.Roles.Invalidate();
            RoleOutcome deferred = await f.Services.Ai.Roles.PromptAsync("Research", "Write an entry", null, 50, ct).ConfigureAwait(false);
            Check.Equal("ExecutorsExhausted", deferred.ErrorCode ?? String.Empty, "Exhausted");
            Check.True(deferred.Retryable, "Defer makes the failure retryable");
            Check.True(deferred.Escalations.Single().Contains("worker", StringComparison.Ordinal), "The harness was passed over on an API instance");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, Roles + "/Embedding/test", HttpFixture.AdminToken, new { prompt = "x" }, HttpStatusCode.BadRequest, "NotTestable", ct).ConfigureAwait(false);
        }

        private static async Task AssistantLedgerAsync(HttpFixture f, FakeModelServer model, CancellationToken ct)
        {
            string assistant = await AiTestKit.ReadyAsync(f, model, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await AiTestKit.StartAsync(f, assistant, member.AccessToken, "How do I change my profile photo?", ct).ConfigureAwait(false);
            JsonElement calls = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/calls/enumerate", HttpFixture.AdminToken, new { filters = new { assistantId = assistant, purpose = new[] { "Chat" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<JsonElement> rows = calls.GetProperty("objects").EnumerateArray().ToList();
            Check.True(rows.Count >= 1, "The chat call is in the ledger");
            Check.True(rows[0].GetProperty("role").ValueKind == JsonValueKind.Null, "Assistant traffic has no task role");
            Check.Equal("fake-chat", Api.Str(rows[0], "model"), "Model recorded");
        }

        private static Task OptionsAsync(HttpFixture f, CancellationToken ct)
        {
            ModelEndpoint anthropic = new ModelEndpoint { ApiType = ModelApiTypeEnum.Anthropic, Model = "claude-sonnet-5-5", Temperature = 0.7, TopP = 0.9, ReasoningEffort = ModelReasoningEffortEnum.Low };
            CompletionOptions a = ModelInvoker.OptionsFor(anthropic, 100, 0.5, 0.8, 8192);
            Check.True(a.Temperature == null, "No temperature to Anthropic");
            Check.True(a.TopP == null, "No top_p to Anthropic");
            Check.Equal(ReasoningEffortLevel.Low, a.ReasoningEffort!.Level, "Endpoint effort");
            CompletionOptions high = ModelInvoker.OptionsFor(anthropic, 100, null, null, 8192, ModelReasoningEffortEnum.High);
            Check.Equal(ReasoningEffortLevel.High, high.ReasoningEffort!.Level, "The role's effort overrides the endpoint's");
            ModelEndpoint ollama = new ModelEndpoint { ApiType = ModelApiTypeEnum.Ollama, Model = "gpt-oss", Temperature = 0.7 };
            CompletionOptions o = ModelInvoker.OptionsFor(ollama, 100, null, null, 8192);
            Check.True(o.Temperature != null && Math.Abs(o.Temperature.Value - 0.7) < 1e-9, "Other providers keep temperature");
            return Task.CompletedTask;
        }

        private static string Json(string text)
        {
            string encoded = JsonSerializer.Serialize(text);
            return encoded.Substring(1, encoded.Length - 2);
        }

        #endregion
    }
}
