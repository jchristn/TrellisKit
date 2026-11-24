namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Concurrent;
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
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services.Ai;

    /// <summary>
    /// Per-call telemetry of the AI call ledger (docs/AI_TELEMETRY.md; owner requirement "every model interaction stores
    /// full telemetry"): streaming chat rows carry time to first and last token, streaming time, tokens per second,
    /// chunks, finish reason, status, sizes, and the provider's timings; cached tokens are stored and priced with the
    /// endpoint's optional cache prices; tool time is attributed to the model call that asked for the tools; retries,
    /// embeddings, endpoint tests, and model health checks each get a row; and the probe measures only its own call.
    /// </summary>
    public static class AiTelemetrySuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "AiTelemetry";

        #endregion

        #region Private-Members

        private const string Roles = "/api/v1/admin/ai/roles";
        private const string Calls = "/api/v1/admin/ai/calls/enumerate";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "AI call telemetry (" + TestEnvironment.Provider + ")", new[]
            {
                AiTestKit.Case(Id, "Ledger.StreamingChat", "A streamed chat reply stores time to first token, time to last token, streaming time, tokens per second, chunks, finish reason, status, request size, queue time, the provider's model and timings, and input, output, and total tokens; the non-streamed title call stores its response size and no first-token time", StreamingChatAsync),
                AiTestKit.Case(Id, "Ledger.CachedTokensAndCost", "Cached tokens are stored per call and priced with the endpoint's optional cache prices: a subset of the prompt on the OpenAI wire, additional to it on the Anthropic wire, at the input price when no cache price is set, and no cost at all without prices", CachedTokensAsync),
                AiTestKit.Case(Id, "Ledger.ToolTime", "A model call that asks for tools stores how many it asked for and how long the engine ran them; the answering call stores none", ToolTimeAsync),
                AiTestKit.Case(Id, "Ledger.RetriesEmbeddingsTestsAndHealth", "A retried call has one row per attempt with its status and retry number; embeddings, an administrator's endpoint test, and a health check that calls the model each get a row, and tests and health checks stay out of the usage rollups", OtherPathsAsync),
                SuiteCases.Unit(Id, "Cost.AndProbe", "Cost prices cache read and cache creation tokens per provider semantics and is zero without prices; tokens per second prefers the provider's generation time; the transfer probe counts only declared lengths and restores its parent", UnitAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static async Task<List<JsonElement>> RowsAsync(HttpFixture f, object filters, CancellationToken ct)
        {
            JsonElement page = await Api.ExpectAsync(f, HttpMethod.Post, Calls, HttpFixture.AdminToken, new { maxResults = 100, filters }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            return page.GetProperty("objects").EnumerateArray().ToList();
        }

        private static long? Long(JsonElement row, string name)
        {
            return row.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : null;
        }

        private static bool IsNull(JsonElement row, string name)
        {
            return row.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Null;
        }

        private static async Task<string> RoleAsync(HttpFixture f, string role, string endpointId, CancellationToken ct)
        {
            await Api.ExpectAsync(f, HttpMethod.Put, Roles + "/" + role, HttpFixture.AdminToken, new { executors = new object[] { new { runtime = "ApiEndpoint", endpointId } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            f.Services.Ai.Roles.Invalidate();
            return endpointId;
        }

        private static async Task StreamingChatAsync(HttpFixture f, FakeModelServer model, CancellationToken ct)
        {
            model.FirstChunkDelayMs = 80;
            model.ChunkDelayMs = 15;
            string assistant = await AiTestKit.ReadyAsync(f, model, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement conversation = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/assistants/" + assistant + "/conversations", member.AccessToken, new { }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            List<SseEvent> events = await AiTestKit.SseAsync(f, "/api/v1/assistant-conversations/" + Api.Str(conversation, "id") + "/messages", member.AccessToken, new { content = "How do I change my profile photo?", clientMessageId = Guid.NewGuid().ToString() }, ct).ConfigureAwait(false);
            Check.True(events.Any(e => e.Name == "reply.completed"), "The reply completed");

            JsonElement chat = (await RowsAsync(f, new { assistantId = assistant, purpose = new[] { "Chat" } }, ct).ConfigureAwait(false)).First();
            Check.Equal("Ok", Api.Str(chat, "outcome"), "Chat outcome");
            Check.True(chat.GetProperty("streamed").GetBoolean(), "The chat call streamed");
            long ttft = Long(chat, "timeToFirstTokenMs") ?? -1;
            long ttlt = Long(chat, "timeToLastTokenMs") ?? -1;
            long streaming = Long(chat, "streamingMs") ?? -1;
            long latency = Long(chat, "latencyMs") ?? -1;
            Check.True(ttft >= 60, "Time to first token reflects the first chunk delay (" + ttft + " ms)");
            Check.True(ttlt >= ttft, "Time to last token is not before the first (" + ttlt + " ms)");
            Check.True(streaming == ttlt - ttft && streaming > 0, "Streaming time is last minus first (" + streaming + " ms)");
            Check.True(latency >= ttlt, "Overall latency covers the stream (" + latency + " ms)");
            Check.True((Long(chat, "chunks") ?? 0) > 1, "Chunks counted");
            Check.Equal(200L, Long(chat, "statusCode") ?? 0, "HTTP status");
            Check.Equal("stop", Api.Str(chat, "finishReason"), "Finish reason");
            Check.Equal(0L, Long(chat, "retries") ?? -1, "First attempt");
            Check.True((Long(chat, "queueMs") ?? -1) >= 0, "Queue time recorded");
            Check.True((Long(chat, "requestBytes") ?? 0) > 50, "Request size measured");
            Check.True(IsNull(chat, "responseBytes"), "A streamed response declares no length");
            Check.Equal("fake-chat", Api.Str(chat, "providerModel"), "The provider's model");
            Check.True(chat.GetProperty("tokensPerSecond").GetDouble() > 0, "Tokens per second");
            int prompt = chat.GetProperty("promptTokens").GetInt32();
            int completion = chat.GetProperty("completionTokens").GetInt32();
            Check.True(prompt > 0 && completion > 0, "Prompt and completion tokens");
            Check.Equal(prompt, chat.GetProperty("inputTokens").GetInt32(), "Input tokens are the prompt tokens");
            Check.Equal(completion, chat.GetProperty("outputTokens").GetInt32(), "Output tokens are the completion tokens");
            Check.Equal((long)(prompt + completion + chat.GetProperty("reasoningTokens").GetInt32()), Long(chat, "totalTokens") ?? 0, "Total tokens");
            Check.Equal(FakeModelServer.OllamaLoadNs / 1000000, Long(chat, "loadMs") ?? -1, "Ollama load time");
            Check.Equal(FakeModelServer.OllamaPromptEvalNs / 1000000, Long(chat, "promptEvalMs") ?? -1, "Ollama prompt evaluation time");
            Check.Equal(FakeModelServer.OllamaEvalNs / 1000000, Long(chat, "evalMs") ?? -1, "Ollama generation time");
            Check.True(IsNull(chat, "cachedReadTokens") && IsNull(chat, "cacheCreationTokens"), "Ollama reports no cache tokens, so they are null");
            Check.True(IsNull(chat, "toolCalls") && IsNull(chat, "toolExecMs"), "No tools were offered");
            Check.True(IsNull(chat, "exitCode"), "No exit code for an API call");
            Check.Equal(0d, chat.GetProperty("costUsd").GetDouble(), "No prices, no cost");

            JsonElement title = (await RowsAsync(f, new { assistantId = assistant, purpose = new[] { "TitleGeneration" } }, ct).ConfigureAwait(false)).First();
            Check.True(!title.GetProperty("streamed").GetBoolean(), "The title call did not stream");
            Check.True(IsNull(title, "timeToFirstTokenMs") && IsNull(title, "streamingMs") && IsNull(title, "chunks"), "A call that does not stream has no first-token or streaming time");
            Check.True((Long(title, "timeToLastTokenMs") ?? -1) >= 0, "Time to last token of a whole response");
            Check.True((Long(title, "responseBytes") ?? 0) > 20, "A whole response declares its length");
            Check.Equal(200L, Long(title, "statusCode") ?? 0, "Title status");
        }

        private static async Task CachedTokensAsync(HttpFixture f, FakeModelServer model, CancellationToken ct)
        {
            model.CachedTokens = 40;
            model.CacheCreationTokens = 10;
            // OpenAI wire: cached tokens are part of the prompt tokens.
            string openAi = await AiTestKit.EndpointAsync(f, new
            {
                name = "Cached OpenAI wire", kind = "Completion", apiType = "OpenAICompatible", model = "fake-chat", endpointUrl = model.BaseUrl + "/v1",
                parameters = new { streaming = false, timeoutMs = 10000 }, limits = new { maxRetries = 0 }, healthCheck = new { enabled = false },
                pricing = new { inputPerMillionTokens = 1000000, outputPerMillionTokens = 500000, cacheReadPerMillionTokens = 100000, cacheWritePerMillionTokens = 250000 }
            }, ct).ConfigureAwait(false);
            JsonElement saved = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/ai/endpoints/" + openAi, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(100000d, saved.GetProperty("pricing").GetProperty("cacheReadPerMillionTokens").GetDouble(), "Cache read price is stored");
            Check.Equal(250000d, saved.GetProperty("pricing").GetProperty("cacheWritePerMillionTokens").GetDouble(), "Cache write price is stored");
            await RoleAsync(f, "Extraction", openAi, ct).ConfigureAwait(false);
            RoleOutcome first = await f.Services.Ai.Roles.PromptAsync("Extraction", "Extract the community center name from: Example Community Center, Reno NV. " + new string('x', 400), null, 50, ct).ConfigureAwait(false);
            Check.True(first.Success, "The OpenAI wire call answered (" + first.ErrorCode + ")");
            JsonElement row = (await RowsAsync(f, new { endpointId = openAi }, ct).ConfigureAwait(false)).First();
            int prompt = row.GetProperty("promptTokens").GetInt32();
            int completion = row.GetProperty("completionTokens").GetInt32();
            Check.Equal(40L, Long(row, "cachedReadTokens") ?? -1, "Cached tokens from prompt_tokens_details");
            Check.True(IsNull(row, "cacheCreationTokens"), "The OpenAI wire reports no cache creation");
            Check.True(prompt > 40, "The prompt is larger than its cached part (" + prompt + ")");
            double expected = ((prompt - 40) * 1.0 + 40 * 0.1 + completion * 0.5);
            Check.True(Math.Abs(row.GetProperty("costUsd").GetDouble() - expected) < 1e-6, "Cached tokens are taken out of the input price and billed at the cache read price (" + row.GetProperty("costUsd").GetDouble() + " vs " + expected + ")");
            Check.Equal("stop", Api.Str(row, "finishReason"), "Finish reason on the OpenAI wire");
            Check.True(Api.Str(row, "responseId").Length > 0, "The provider's response id");

            // Anthropic wire: cache read and creation tokens are additional to the prompt tokens.
            string anthropic = await AiTestKit.EndpointAsync(f, new
            {
                name = "Cached Anthropic wire", kind = "Completion", apiType = "Anthropic", model = "fake-chat", endpointUrl = model.BaseUrl, authType = "ApiKey", authValue = "test-key-not-real",
                parameters = new { streaming = true, timeoutMs = 10000 }, limits = new { maxRetries = 0 }, healthCheck = new { enabled = false },
                pricing = new { inputPerMillionTokens = 1000000, outputPerMillionTokens = 500000, cacheReadPerMillionTokens = 100000, cacheWritePerMillionTokens = 250000 }
            }, ct).ConfigureAwait(false);
            await RoleAsync(f, "Extraction", anthropic, ct).ConfigureAwait(false);
            RoleOutcome second = await f.Services.Ai.Roles.PromptAsync("Extraction", "Extract the community center name from: Example Community Center, Reno NV.", null, 50, ct).ConfigureAwait(false);
            Check.True(second.Success, "The Anthropic wire call answered (" + second.ErrorCode + ")");
            JsonElement added = (await RowsAsync(f, new { endpointId = anthropic }, ct).ConfigureAwait(false)).First();
            int aPrompt = added.GetProperty("promptTokens").GetInt32();
            int aCompletion = added.GetProperty("completionTokens").GetInt32();
            Check.Equal(40L, Long(added, "cachedReadTokens") ?? -1, "Cache read tokens on the Anthropic wire");
            Check.Equal(10L, Long(added, "cacheCreationTokens") ?? -1, "Cache creation tokens on the Anthropic wire");
            double additive = aPrompt * 1.0 + 40 * 0.1 + 10 * 0.25 + aCompletion * 0.5;
            Check.True(Math.Abs(added.GetProperty("costUsd").GetDouble() - additive) < 1e-6, "Cache tokens are added at the cache prices (" + added.GetProperty("costUsd").GetDouble() + " vs " + additive + ")");

            // No cache prices: cached tokens bill at the input price. No prices at all: cost 0.
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/ai/endpoints/" + openAi, HttpFixture.AdminToken, new { pricing = new { cacheReadPerMillionTokens = (double?)null, cacheWritePerMillionTokens = (double?)null } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await RoleAsync(f, "Extraction", openAi, ct).ConfigureAwait(false);
            await f.Services.Ai.Roles.PromptAsync("Extraction", "Extract again. " + new string('y', 400), null, 50, ct).ConfigureAwait(false);
            JsonElement plain = (await RowsAsync(f, new { endpointId = openAi }, ct).ConfigureAwait(false)).First();
            double atInput = plain.GetProperty("promptTokens").GetInt32() * 1.0 + plain.GetProperty("completionTokens").GetInt32() * 0.5;
            Check.True(Math.Abs(plain.GetProperty("costUsd").GetDouble() - atInput) < 1e-6, "Without a cache price, cached tokens bill at the input price");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/ai/endpoints/" + openAi, HttpFixture.AdminToken, new { pricing = new { inputPerMillionTokens = (double?)null, outputPerMillionTokens = (double?)null } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await f.Services.Ai.Roles.PromptAsync("Extraction", "Extract once more. " + new string('z', 400), null, 50, ct).ConfigureAwait(false);
            JsonElement free = (await RowsAsync(f, new { endpointId = openAi }, ct).ConfigureAwait(false)).First();
            Check.Equal(0d, free.GetProperty("costUsd").GetDouble(), "An endpoint without prices records no cost");
            Check.Equal(40L, Long(free, "cachedReadTokens") ?? -1, "Telemetry is stored whether or not there is a price");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/ai/endpoints/" + openAi, HttpFixture.AdminToken, new { pricing = new { cacheReadPerMillionTokens = -1 } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
        }

        private static async Task ToolTimeAsync(HttpFixture f, FakeModelServer model, CancellationToken ct)
        {
            string chat = await AiTestKit.EndpointAsync(f, AiTestKit.OllamaEndpoint(model, "Agent " + Guid.NewGuid().ToString("N").Substring(0, 6)), ct).ConfigureAwait(false);
            await RoleAsync(f, "Research", chat, ct).ConfigureAwait(false);
            model.ToolCalls = new List<FakeToolCall> { new FakeToolCall { Name = "submit_candidate", ArgumentsJson = "{\"name\":\"Example Community Center\"}" } };
            model.ToolRounds = 1;
            model.ReplyText = "Submitted one community center.";
            ConcurrentQueue<string> stored = new ConcurrentQueue<string>();
            AgentOutcome outcome = await f.Services.Ai.Roles.RunAgentAsync("Research", new AgentRequest
            {
                Instructions = "Find community centers around Reno, Nevada, and submit each one.",
                Tools = new List<AgentTool>
                {
                    new AgentTool
                    {
                        Name = "submit_candidate",
                        Description = "Submit one community center.",
                        ParametersSchema = "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":200}},\"required\":[\"name\"],\"additionalProperties\":false}",
                        Handler = async (call, token) =>
                        {
                            await Task.Delay(120, token).ConfigureAwait(false);
                            stored.Enqueue(call.Arguments.GetProperty("name").GetString() ?? String.Empty);
                            return AgentToolResult.Accept("Recorded.");
                        }
                    }
                }
            }, ct).ConfigureAwait(false);
            Check.True(outcome.Success, "The agent answered (" + outcome.ErrorCode + ")");
            Check.Equal(1, stored.Count, "The tool ran once");
            Check.True(outcome.ToolCalls.Single().DurationMs >= 100, "The tool record carries its duration (" + outcome.ToolCalls.Single().DurationMs + " ms)");
            List<JsonElement> rows = await RowsAsync(f, new { role = new[] { "Research" } }, ct).ConfigureAwait(false);
            Check.Equal(2, rows.Count, "A tool round and an answer round");
            JsonElement answer = rows[0];
            JsonElement asked = rows[1];
            Check.Equal(1L, Long(asked, "toolCalls") ?? -1, "The call that asked for the tool counts it");
            Check.True((Long(asked, "toolExecMs") ?? -1) >= 100, "The tool's time is attributed to the call that asked for it (" + Long(asked, "toolExecMs") + " ms)");
            Check.Equal(0L, Long(answer, "toolCalls") ?? -1, "The answering call asked for none");
            Check.True(IsNull(answer, "toolExecMs"), "No tool time on the answering call");
        }

        private static async Task OtherPathsAsync(HttpFixture f, FakeModelServer model, CancellationToken ct)
        {
            // Retries: one row per attempt.
            string retried = await AiTestKit.EndpointAsync(f, AiTestKit.OllamaEndpoint(model, "Retried", "Completion", new { limits = new { maxRetries = 1 } }), ct).ConfigureAwait(false);
            await RoleAsync(f, "Extraction", retried, ct).ConfigureAwait(false);
            model.FailNext = 1;
            model.FailStatus = 503;
            RoleOutcome outcome = await f.Services.Ai.Roles.PromptAsync("Extraction", "Extract the name.", null, 50, ct).ConfigureAwait(false);
            Check.True(outcome.Success, "The retry answered (" + outcome.ErrorCode + ")");
            List<JsonElement> attempts = await RowsAsync(f, new { endpointId = retried }, ct).ConfigureAwait(false);
            Check.Equal(2, attempts.Count, "One row per attempt");
            Check.Equal("Error", Api.Str(attempts[1], "outcome"), "The first attempt failed");
            Check.Equal(503L, Long(attempts[1], "statusCode") ?? 0, "Its HTTP status");
            Check.Equal(0L, Long(attempts[1], "retries") ?? -1, "It was the first attempt");
            Check.Equal("Ok", Api.Str(attempts[0], "outcome"), "The second attempt answered");
            Check.Equal(1L, Long(attempts[0], "retries") ?? -1, "It was the first retry");
            Check.Equal(200L, Long(attempts[0], "statusCode") ?? 0, "Its HTTP status");

            // Embeddings.
string embed = await AiTestKit.EndpointAsync(f, AiTestKit.OllamaEndpoint(model, "Embed", "Embedding", new { pricing = new { inputPerMillionTokens = 1000000 } }), ct).ConfigureAwait(false);
            await RoleAsync(f, "Embedding", embed, ct).ConfigureAwait(false);
            await f.Services.Ai.Roles.EmbedAsync(new List<string> { "armbar from closed guard", "single leg takedown" }, ct).ConfigureAwait(false);
            JsonElement embedding = (await RowsAsync(f, new { endpointId = embed }, ct).ConfigureAwait(false)).Single();
            Check.Equal("Embedding", Api.Str(embedding, "purpose"), "Embedding purpose");
            Check.Equal(200L, Long(embedding, "statusCode") ?? 0, "Embedding status");
            Check.True((Long(embedding, "requestBytes") ?? 0) > 20 && (Long(embedding, "responseBytes") ?? 0) > 20, "Embedding request and response sizes");
            int embedded = embedding.GetProperty("promptTokens").GetInt32();
            Check.True(embedded > 0 && (Long(embedding, "totalTokens") ?? 0) == embedded, "Embedding tokens are read from the provider's response (" + embedded + ")");
            Check.True(Math.Abs(embedding.GetProperty("costUsd").GetDouble() - embedded) < 1e-6, "Embedding cost from the input price");
            Check.True(!embedding.GetProperty("streamed").GetBoolean() && IsNull(embedding, "timeToFirstTokenMs"), "Embeddings do not stream");
            Check.True((Long(embedding, "queueMs") ?? -1) >= 0, "Embedding queue time");

            // An administrator's endpoint test.
            long rollupsBefore = await ContentTestKit.ScalarAsync(f, "SELECT COALESCE(SUM(requests), 0) AS cnt FROM aiusagerollups;", ct).ConfigureAwait(false);
            string tested = await AiTestKit.EndpointAsync(f, AiTestKit.OllamaEndpoint(model, "Tested"), ct).ConfigureAwait(false);
            JsonElement test = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints/" + tested + "/test", HttpFixture.AdminToken, new { prompt = "Say hello in five words." }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(test.GetProperty("success").GetBoolean(), "The test answered");
            JsonElement testRow = (await RowsAsync(f, new { endpointId = tested }, ct).ConfigureAwait(false)).Single();
            Check.Equal("EndpointTest", Api.Str(testRow, "purpose"), "Endpoint test purpose");
            Check.True(testRow.GetProperty("isTest").GetBoolean(), "Test traffic");
            Check.True(testRow.GetProperty("streamed").GetBoolean() && (Long(testRow, "timeToFirstTokenMs") ?? -1) >= 0 && (Long(testRow, "chunks") ?? 0) > 0, "The streamed test stores its stream timings");
            Check.True(testRow.GetProperty("completionTokens").GetInt32() > 0 && (Long(testRow, "requestBytes") ?? 0) > 0, "Test tokens and request size");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints/validate", HttpFixture.AdminToken, AiTestKit.OllamaEndpoint(model, "Unsaved"), HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<JsonElement> validations = (await RowsAsync(f, new { purpose = new[] { "EndpointTest" } }, ct).ConfigureAwait(false)).Where(r => IsNull(r, "endpointId")).ToList();
            Check.Equal(1, validations.Count, "Validating an unsaved endpoint is a model call with a row and no endpoint id");

            // A health check that calls the model; a connectivity check calls none.
            string checkedId = await AiTestKit.EndpointAsync(f, AiTestKit.OllamaEndpoint(model, "Checked", "Completion", new { healthCheck = new { enabled = true, mode = "Completion", healthyThreshold = 1, unhealthyThreshold = 1 } }), ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints/" + checkedId + "/health/check", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement health = (await RowsAsync(f, new { endpointId = checkedId, purpose = new[] { "HealthCheck" } }, ct).ConfigureAwait(false)).First();
            Check.Equal("Ok", Api.Str(health, "outcome"), "Health check outcome");
            Check.True(health.GetProperty("isTest").GetBoolean() && (Long(health, "statusCode") ?? 0) == 200, "Health check row is test traffic with its status");
            string pinged = await AiTestKit.EndpointAsync(f, AiTestKit.OllamaEndpoint(model, "Pinged", "Completion", new { healthCheck = new { enabled = true, mode = "Connectivity", healthyThreshold = 1, unhealthyThreshold = 1 } }), ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints/" + pinged + "/health/check", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0, (await RowsAsync(f, new { endpointId = pinged }, ct).ConfigureAwait(false)).Count, "A connectivity check calls no model and writes no row");
            long rollupsAfter = await ContentTestKit.ScalarAsync(f, "SELECT COALESCE(SUM(requests), 0) AS cnt FROM aiusagerollups;", ct).ConfigureAwait(false);
            Check.Equal(rollupsBefore, rollupsAfter, "Tests and health checks stay out of the usage rollups");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/ai/assistants", HttpFixture.AdminToken, new { name = "Bad slot", systemPrompt = "x", models = new { chat = tested, healthCheck = tested } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
        }

        private static Task UnitAsync(CancellationToken ct)
        {
            ModelEndpoint openAi = new ModelEndpoint { ApiType = ModelApiTypeEnum.OpenAICompatible, InputPricePerMillion = 2, OutputPricePerMillion = 8, CacheReadPricePerMillion = 0.5, CacheWritePricePerMillion = 3 };
            Check.Equal(2000L + 4000L, ModelInvoker.Cost(openAi, 1000, 500), "Without cache tokens the cost is unchanged");
            Check.Equal(600L * 2 + 400L / 2 + 4000L, ModelInvoker.Cost(openAi, 1000, 500, 400, null), "Subset providers: cached tokens leave the input price");
            ModelEndpoint anthropic = new ModelEndpoint { ApiType = ModelApiTypeEnum.Anthropic, InputPricePerMillion = 2, OutputPricePerMillion = 8, CacheReadPricePerMillion = 0.5, CacheWritePricePerMillion = 3 };
            Check.Equal(2000L + 200L + 300L + 4000L, ModelInvoker.Cost(anthropic, 1000, 500, 400, 100), "Additive providers: cache tokens are added");
            ModelEndpoint noCachePrice = new ModelEndpoint { ApiType = ModelApiTypeEnum.Anthropic, InputPricePerMillion = 2, OutputPricePerMillion = 8 };
            Check.Equal(2000L + 800L + 200L + 4000L, ModelInvoker.Cost(noCachePrice, 1000, 500, 400, 100), "No cache price: the input price applies");
            Check.Equal(0L, ModelInvoker.Cost(new ModelEndpoint { ApiType = ModelApiTypeEnum.Ollama }, 1000, 500, 400, 100), "No prices: no cost");
            Check.Equal(0L, ModelInvoker.Cost(null, 1000, 500, 400, 100), "No endpoint: no cost");
            Check.Equal(200L, ModelInvoker.Cost(openAi, 100, 0, 400, null), "More cached tokens than prompt tokens never bills a negative input part");

            Check.True(Math.Abs((AiCallDetails.TokensPerSecond(100, 2000, 10000) ?? 0) - 50) < 1e-9, "The provider's generation time is preferred");
            Check.True(Math.Abs((AiCallDetails.TokensPerSecond(100, null, 4000) ?? 0) - 25) < 1e-9, "Else the whole call");
            Check.True(AiCallDetails.TokensPerSecond(0, 2000, 4000) == null && AiCallDetails.TokensPerSecond(10, null, null) == null, "Unknown stays null");

            Check.True(ModelCallProbe.Current == null, "No probe by default");
            using (ModelCallProbe outer = ModelCallProbe.Begin())
            {
                outer.AddRequest(100);
                using (ModelCallProbe inner = ModelCallProbe.Begin())
                {
                    Check.True(ReferenceEquals(ModelCallProbe.Current, inner), "The inner probe is current");
                    inner.AddRequest(null);
                    inner.AddResponse(null);
                    Check.True(inner.RequestBytes == null && inner.ResponseBytes == null && inner.Requests == 1, "Undeclared lengths stay unknown");
                }
                Check.True(ReferenceEquals(ModelCallProbe.Current, outer), "The outer probe is restored");
                outer.AddResponse(250);
                Check.True(outer.RequestBytes == 100 && outer.ResponseBytes == 250, "Declared lengths are summed");
            }
            Check.True(ModelCallProbe.Current == null, "Closed");

            AiCallDetail detail = new AiCallDetail { CachedReadTokens = 5, FinishReason = "stop", Streamed = true, TokensPerSecond = 12.5 };
            AiCallDetail copy = new AiCallDetail();
            detail.CopyTo(copy);
            Check.True(copy.CachedReadTokens == 5 && copy.FinishReason == "stop" && copy.Streamed == true && copy.TokensPerSecond == 12.5 && copy.ExitCode == null, "Detail copies every value");
            return Task.CompletedTask;
        }

        #endregion
    }
}
