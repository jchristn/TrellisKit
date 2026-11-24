namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared.Fakes;
    using Touchstone.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Workers;

    /// <summary>
    /// Assistants and chat (docs/AI_CONTRACT.md sections 5 to 14): assistant validation, member summaries, SSE framing
    /// and event order, non-streaming JSON, idempotency, limits, word filter and moderation (block and fail open),
    /// fallback with usage and cost, knowledge grounding (keyword and vector) with citations, the administrator
    /// conversation browser with audit, feedback, visitors on public assistants, and maintenance.
    /// </summary>
    public static class AiChatSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "AiChat";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "AI assistants and chat (" + TestEnvironment.Provider + ")", new[]
            {
                AiTestKit.Case(Id, "Assistants.ValidationAndSummaries", "Kind mismatches, unknown tokens, and a missing chat model are rejected; members see summaries without the system prompt or models", async (f, model, ct) =>
                {
                    string chat = await AiTestKit.EndpointAsync(f, AiTestKit.OllamaEndpoint(model, "Chat"), ct).ConfigureAwait(false);
                    string embed = await AiTestKit.EndpointAsync(f, AiTestKit.OllamaEndpoint(model, "Embed", "Embedding"), ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/ai/assistants", HttpFixture.AdminToken, new { name = "Bad", models = new { chat = embed } }, HttpStatusCode.BadRequest, "EndpointKindMismatch", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/ai/assistants", HttpFixture.AdminToken, new { name = "Bad", models = new { chat, embedding = chat } }, HttpStatusCode.BadRequest, "EndpointKindMismatch", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/ai/assistants", HttpFixture.AdminToken, new { name = "Bad", systemPrompt = "Hi {unknownToken}", models = new { chat } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/ai/assistants", HttpFixture.AdminToken, new { name = "Bad" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    string id = await AiTestKit.AssistantAsync(f, chat, ct, new { models = new { chat, embedding = embed }, limits = new { messagesPerMemberPerDay = 3 } }).ConfigureAwait(false);
                    JsonElement admin = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/ai/assistants/" + id, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(800, admin.GetProperty("limits").GetProperty("maxTokensPerReply").GetInt32(), "Default 800 tokens per reply");
                    Check.Equal(embed, admin.GetProperty("models").GetProperty("embedding").GetString(), "Embedding purpose");
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    ApiResult summary = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/assistants/" + id, member.AccessToken, null, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, summary.Status, "Summary (" + summary.Text + ")");
                    Check.True(!summary.Text.Contains("systemPrompt", StringComparison.Ordinal) && !summary.Text.Contains(chat, StringComparison.Ordinal), "No prompt or endpoint ids");
                    Check.Equal(3, summary.Body.GetProperty("limits").GetProperty("remainingToday").GetInt32(), "Remaining today");
                    Check.True(summary.Body.GetProperty("privacy").GetProperty("administratorsCanReadConversations").GetBoolean(), "Admin access disclosed");
                    JsonElement list = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/assistants/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, list.GetProperty("objects").GetArrayLength(), "Member list");
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/assistants/" + id, null, null, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);
                    TestMember signedIn = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/assistants/" + id, signedIn.AccessToken, null, HttpStatusCode.PaymentRequired, "MembershipRequired", ct).ConfigureAwait(false);
                }),

                AiTestKit.Case(Id, "Chat.SseStreamAndPersistence", "Streaming emits message.created, reply.started, ordered deltas that equal the content, and one reply.completed; the exchange is stored with telemetry; JSON mode returns 201; a repeated clientMessageId returns the stored exchange", async (f, model, ct) =>
                {
                    string assistant = await AiTestKit.ReadyAsync(f, model, ct).ConfigureAwait(false);
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    JsonElement conversation = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/assistants/" + assistant + "/conversations", member.AccessToken, new { }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    string conversationId = Api.Str(conversation, "id");
                    string clientId = Guid.NewGuid().ToString();
                    List<SseEvent> events = await AiTestKit.SseAsync(f, "/api/v1/assistant-conversations/" + conversationId + "/messages", member.AccessToken, new { content = "How do I change my profile photo?", clientMessageId = clientId }, ct).ConfigureAwait(false);
                    List<string> names = events.Select(e => e.Name).ToList();
                    Check.Equal("message.created", names[0], "First event (" + String.Join(",", names) + ")");
                    Check.Equal("reply.started", names[1], "Second event");
                    Check.Equal("reply.completed", names[names.Count - 1], "Last event");
                    Check.Equal(1, names.Count(n => n == "reply.completed" || n == "reply.blocked" || n == "error"), "Exactly one terminal event");
                    string text = String.Concat(events.Where(e => e.Name == "reply.delta").Select(e => e.Data.GetProperty("text").GetString()));
                    JsonElement reply = events.Last().Data.GetProperty("reply");
                    Check.Equal(model.ReplyText, text, "Deltas concatenate to the reply");
                    Check.Equal(text, reply.GetProperty("content").GetString(), "Final content");
                    Check.True(reply.GetProperty("isAiGenerated").GetBoolean(), "AI badge");
                    Check.True(reply.GetProperty("model").ValueKind == JsonValueKind.Null, "Model hidden from members");
                    string replyId = Api.Str(reply, "id");
                    DataTable row = await f.Raw.ExecuteQueryAsync("SELECT status, prompttokens, completiontokens, latencyms, ttftms FROM aimessages WHERE id = '" + replyId + "';", false, ct).ConfigureAwait(false);
                    Check.Equal("Completed", RowReader.GetString(row.Rows[0], "status"), "Stored completed");
                    Check.True(RowReader.GetInt(row.Rows[0], "completiontokens") > 0 && RowReader.GetNullableLong(row.Rows[0], "ttftms") != null, "Telemetry stored");
                    DataTable stages = await f.Raw.ExecuteQueryAsync("SELECT stage FROM aimessagestages WHERE messageid = '" + replyId + "';", false, ct).ConfigureAwait(false);
                    Check.True(stages.Rows.Cast<DataRow>().Any(r => RowReader.GetString(r, "stage") == "chat"), "Chat stage stored");

                    List<SseEvent> repeat = await AiTestKit.SseAsync(f, "/api/v1/assistant-conversations/" + conversationId + "/messages", member.AccessToken, new { content = "How do I change my profile photo?", clientMessageId = clientId }, ct).ConfigureAwait(false);
                    Check.Equal(replyId, repeat.Last().Data.GetProperty("reply").GetProperty("id").GetString(), "Idempotent repeat returns the stored reply");

                    JsonElement exchange = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/assistant-conversations/" + conversationId + "/messages", member.AccessToken, new { content = "Thanks, and notifications?" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Check.Equal("Completed", exchange.GetProperty("reply").GetProperty("status").GetString(), "JSON reply");
                    JsonElement messages = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/assistant-conversations/" + conversationId + "/messages/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(4, messages.GetProperty("objects").GetArrayLength(), "Four messages");
                    JsonElement read = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/assistant-conversations/" + conversationId, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(4, read.GetProperty("messageCount").GetInt32(), "Message count");
                    Check.True(!String.IsNullOrEmpty(Api.Str(read, "title")), "Title set");
                    JsonElement mine = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/assistant-conversations/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(conversationId, mine.GetProperty("objects")[0].GetProperty("id").GetString(), "Listed");
                    await f.Raw.ExecuteQueryAsync("UPDATE aiconversations SET lastmessagepreview = '**Open** _Account_, then [Profile](https://example.test)' WHERE id = '" + conversationId + "';", false, ct).ConfigureAwait(false);
                    JsonElement plain = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/assistant-conversations/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Open Account, then Profile", plain.GetProperty("objects")[0].GetProperty("lastMessagePreview").GetString(), "The list preview is plain text, not Markdown");
                    JsonElement regenerated = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/assistant-conversations/" + conversationId + "/messages/" + Api.Str(exchange.GetProperty("reply"), "id") + "/regenerate", member.AccessToken, null, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Check.True(Api.Str(regenerated.GetProperty("reply"), "id") != Api.Str(exchange.GetProperty("reply"), "id"), "Regenerated reply");
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/assistant-conversations/" + conversationId + "/messages/" + replyId + "/feedback", member.AccessToken, new { rating = "Up", note = "Helpful" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement queue = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/feedback/enumerate", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("How do I change my profile photo?", queue.GetProperty("objects")[0].GetProperty("promptContent").GetString(), "Rated pair copied");
                    TestMember other = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/assistant-conversations/" + conversationId, other.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/assistant-conversations/" + conversationId, member.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/assistant-conversations/" + conversationId, member.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                }),

                AiTestKit.Case(Id, "Chat.LimitsAndFilters", "Daily allowance, conversation cap, message length, and the word filter are enforced in order with the contract errors", async (f, model, ct) =>
                {
                    string assistant = await AiTestKit.ReadyAsync(f, model, ct, new { limits = new { messagesPerMemberPerDay = 2, maxMessagesPerConversation = 2, maxMessageCharacters = 40 } }).ConfigureAwait(false);
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    JsonElement first = await AiTestKit.StartAsync(f, assistant, member.AccessToken, "Hello there", ct).ConfigureAwait(false);
                    string conversationId = Api.Str(first.GetProperty("userMessage"), "conversationId");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/assistant-conversations/" + conversationId + "/messages", member.AccessToken, new { content = "Again" }, HttpStatusCode.Conflict, "ConversationFull", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/assistants/" + assistant + "/conversations", member.AccessToken, new { message = new string('x', 41) }, HttpStatusCode.BadRequest, "MessageTooLong", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/word-filter", HttpFixture.AdminToken, new { term = "forbiddenword", matchType = "Word", action = "Block" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/assistants/" + assistant + "/conversations", member.AccessToken, new { message = "a forbiddenword here" }, HttpStatusCode.BadRequest, "ContentFiltered", ct).ConfigureAwait(false);
                    await AiTestKit.StartAsync(f, assistant, member.AccessToken, "Second message", ct).ConfigureAwait(false);
                    ApiResult limited = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/assistants/" + assistant + "/conversations", member.AccessToken, new { message = "Third" }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.TooManyRequests, limited.Status, "Daily allowance (" + limited.Text + ")");
                    Check.Equal("AssistantLimitReached", limited.Error, "AssistantLimitReached");
                    Check.True(!String.IsNullOrEmpty(limited.RetryAfter), "Retry-After until midnight");
                    JsonElement summary = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/assistants/" + assistant, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(0, summary.GetProperty("limits").GetProperty("remainingToday").GetInt32(), "None left");
                    JsonElement adminMessage = await AiTestKit.StartAsync(f, assistant, HttpFixture.AdminToken, "Admins are exempt", ct).ConfigureAwait(false);
                    Check.Equal("Completed", adminMessage.GetProperty("reply").GetProperty("status").GetString(), "SystemAdmin exempt from member limits");
                }),

                AiTestKit.Case(Id, "Chat.ModerationBlocksAndFailsOpen", "A Block verdict rejects input with 400 MessageBlocked and withholds output; a failing classifier fails open", async (f, model, ct) =>
                {
                    string chat = await AiTestKit.EndpointAsync(f, AiTestKit.OllamaEndpoint(model, "Chat"), ct).ConfigureAwait(false);
                    string moderation = await AiTestKit.EndpointAsync(f, AiTestKit.OllamaEndpoint(model, "Moderation", "Completion", new { parameters = new { streaming = false, timeoutMs = 5000 } }), ct).ConfigureAwait(false);
                    string assistant = await AiTestKit.AssistantAsync(f, chat, ct, new { models = new { chat, moderation }, moderationMode = "Block" }).ConfigureAwait(false);
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    model.ModerationReply = "Block Harassment";
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/assistants/" + assistant + "/conversations", member.AccessToken, new { message = "something rude" }, HttpStatusCode.BadRequest, "MessageBlocked", ct).ConfigureAwait(false);
                    long stored = await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM aimessages WHERE propertyid = '" + f.Services.PropertyId + "'", ct).ConfigureAwait(false);
                    Check.Equal(0L, stored, "Blocked input is not stored");
                    model.ModerationReply = "Allow None";
                    JsonElement ok = await AiTestKit.StartAsync(f, assistant, member.AccessToken, "A fine question", ct).ConfigureAwait(false);
                    Check.Equal("Completed", ok.GetProperty("reply").GetProperty("status").GetString(), "Allowed");
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/ai/endpoints/" + moderation, HttpFixture.AdminToken, new { endpointUrl = "http://127.0.0.1:9" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement open = await AiTestKit.StartAsync(f, assistant, member.AccessToken, "Classifier is down", ct).ConfigureAwait(false);
                    Check.Equal("Completed", open.GetProperty("reply").GetProperty("status").GetString(), "Fail open");
                }),

                AiTestKit.Case(Id, "Chat.FallbackUsageAndCost", "A failing primary falls back; usage rollups, cost from endpoint prices, analytics, and cost by member are recorded", async (f, model, ct) =>
                {
                    string primary = await AiTestKit.EndpointAsync(f, new { name = "Primary", kind = "Completion", apiType = "Ollama", model = "m", endpointUrl = "http://127.0.0.1:9", limits = new { maxRetries = 0 }, healthCheck = new { enabled = false } }, ct).ConfigureAwait(false);
                    string backup = await AiTestKit.EndpointAsync(f, AiTestKit.OllamaEndpoint(model, "Backup", "Completion", new { pricing = new { inputPerMillionTokens = 1000.0, outputPerMillionTokens = 2000.0 } }), ct).ConfigureAwait(false);
                    string assistant = await AiTestKit.AssistantAsync(f, primary, ct, new { models = new { chat = primary, chatFallback = backup } }).ConfigureAwait(false);
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    JsonElement exchange = await AiTestKit.StartAsync(f, assistant, member.AccessToken, "Use the backup please", ct).ConfigureAwait(false);
                    Check.Equal("Completed", exchange.GetProperty("reply").GetProperty("status").GetString(), "Fallback answered");
                    string replyId = Api.Str(exchange.GetProperty("reply"), "id");
                    DataTable row = await f.Raw.ExecuteQueryAsync("SELECT usedfallback, endpointid, costmicros FROM aimessages WHERE id = '" + replyId + "';", false, ct).ConfigureAwait(false);
                    Check.True(RowReader.GetBool(row.Rows[0], "usedfallback"), "usedFallback");
                    Check.Equal(backup, RowReader.GetString(row.Rows[0], "endpointid"), "Endpoint used");
                    Check.True(RowReader.GetLong(row.Rows[0], "costmicros") > 0, "Cost recorded");
                    JsonElement overview = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/ai/analytics/overview?days=7", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(overview.GetProperty("requests").GetInt64() >= 2, "Requests counted (" + overview + ")");
                    Check.True(overview.GetProperty("errors").GetInt64() >= 1, "Primary error counted");
                    Check.True(overview.GetProperty("costUsd").GetDouble() > 0, "Cost in overview");
                    Check.True(overview.GetProperty("fallbackRate").GetDouble() > 0, "Fallback rate");
                    JsonElement endpoints = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/analytics/endpoints/enumerate", HttpFixture.AdminToken, new { filters = new { windowDays = "7" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement backupRow = endpoints.GetProperty("objects").EnumerateArray().First(o => o.GetProperty("endpoint").GetProperty("id").GetString() == backup);
                    Check.True(backupRow.GetProperty("costUsd").GetDouble() > 0, "Cost by endpoint");
                    JsonElement members = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/analytics/members/enumerate", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(member.Id, members.GetProperty("objects")[0].GetProperty("subjectId").GetString(), "Cost by member");
                    JsonElement assistants = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/analytics/assistants/enumerate", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(assistants.GetProperty("objects")[0].GetProperty("costUsd").GetDouble() > 0, "Cost by assistant");
                    JsonElement series = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/ai/analytics/timeseries?days=7&metric=cost", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(series.GetProperty("objects").GetArrayLength() >= 1, "Cost by day");
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/ai/assistants/" + assistant, HttpFixture.AdminToken, new { budget = new { monthlyUsd = 0.000001, hardStop = true } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/assistants/" + assistant + "/conversations", member.AccessToken, new { message = "Over budget" }, HttpStatusCode.ServiceUnavailable, "BudgetExceeded", ct).ConfigureAwait(false);
                    int changed = await new AiMaintenanceWorker(f.Services.Ai, new TrellisKit.Server.Services.AppLogger(TestLogging.CreateModule())).PassAsync(DateTime.UtcNow, ct).ConfigureAwait(false);
                    Check.True(changed >= 0, "Maintenance pass runs");
                }),

                AiTestKit.Case(Id, "Knowledge.KeywordAndVectorWithCitations", "Labelled feed posts ground replies with citations through keyword retrieval as the member; ingestion indexes them into the vector store and vector retrieval finds them", async (f, model, ct) =>
                {
                    TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
                    JsonElement post = await ContentTestKit.PostAsync(f, poster.AccessToken, new { title = "Changing your profile photo", body = "Open Account, then Profile, then tap your photo to change it.", labels = new[] { "guide" } }, ct).ConfigureAwait(false);
                    string postId = Api.Str(post, "id");
                    await ContentTestKit.PostAsync(f, poster.AccessToken, new { body = "Unrelated post about the weather today." }, ct).ConfigureAwait(false);
                    string chat = await AiTestKit.EndpointAsync(f, AiTestKit.OllamaEndpoint(model, "Chat"), ct).ConfigureAwait(false);
                    string embed = await AiTestKit.EndpointAsync(f, AiTestKit.OllamaEndpoint(model, "Embed", "Embedding"), ct).ConfigureAwait(false);
                    string assistant = await AiTestKit.AssistantAsync(f, chat, ct, new
                    {
                        models = new { chat, embedding = embed },
                        knowledge = new { enabled = true, mode = "Keyword", citations = true, sources = new[] { new { targetType = "FeedPost", labels = new[] { "guide" } } } }
                    }).ConfigureAwait(false);
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    JsonElement exchange = await AiTestKit.StartAsync(f, assistant, member.AccessToken, "How do I change my profile photo?", ct).ConfigureAwait(false);
                    JsonElement citations = exchange.GetProperty("reply").GetProperty("citations");
                    Check.Equal(1, citations.GetArrayLength(), "One citation (" + exchange + ")");
                    Check.Equal(postId, citations[0].GetProperty("targetId").GetString(), "Cites the guide post");
                    Check.Equal("/feed/" + postId, citations[0].GetProperty("path").GetString(), "App path");
                    JsonElement preview = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/assistants/" + assistant + "/knowledge/preview/enumerate", HttpFixture.AdminToken, new { filters = new { query = "profile photo" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(preview.GetProperty("objects").GetArrayLength() > 0, "Preview has items (" + preview + ")");
                    Check.Equal(postId, preview.GetProperty("objects")[0].GetProperty("targetId").GetString(), "Preview");
                    Check.True(preview.GetProperty("objects")[0].GetProperty("labels").EnumerateArray().Any(x => x.GetString() == "guide"), "Preview carries source metadata");
                    JsonElement noMatches = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/assistants/" + assistant + "/knowledge/preview/enumerate", HttpFixture.AdminToken, new { filters = new { query = "profile photo" }, labels = new[] { "missing-preview-label" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(0, noMatches.GetProperty("objects").GetArrayLength(), "Preview metadata filter applies to finite shortlist");
                    Check.True(noMatches.TryGetProperty("mode", out _) && noMatches.TryGetProperty("totalCandidates", out _), "Retrieval context retained on empty page");


                    InMemoryVectorStore store = new InMemoryVectorStore();
                    f.Services.Ai.VectorStore = store;
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/ai/assistants/" + assistant, HttpFixture.AdminToken, new { knowledge = new { mode = "Vector" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Assistant saved = (await f.Database.Ai.Assistants.ReadAsync(assistant, ct).ConfigureAwait(false))!;
                    int embedded = await f.Services.Ai.Indexer.IndexAsync(saved, ct).ConfigureAwait(false);
                    Check.True(embedded >= 1, "Chunks embedded (" + embedded + ")");
                    JsonElement status = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/ai/assistants/" + assistant + "/knowledge/status", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Idle", status.GetProperty("status").GetString(), "Index idle (" + status + ")");
                    Check.Equal(1, status.GetProperty("itemCount").GetInt32(), "One item indexed");
                    JsonElement items = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/assistants/" + assistant + "/knowledge/items/enumerate", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Indexed", items.GetProperty("objects")[0].GetProperty("status").GetString(), "Item status");
                    Check.Equal(0, await f.Services.Ai.Indexer.IndexAsync(saved, ct).ConfigureAwait(false), "Unchanged content is not re-embedded");
                    JsonElement vector = await AiTestKit.StartAsync(f, assistant, member.AccessToken, "profile photo change", ct).ConfigureAwait(false);
                    Check.True(vector.GetProperty("reply").GetProperty("citations").GetArrayLength() > 0, "Vector citations (" + vector + ")");
                    Check.Equal(postId, vector.GetProperty("reply").GetProperty("citations")[0].GetProperty("targetId").GetString(), "Vector retrieval cites the post");
                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/feed/" + postId, poster.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                    await f.Services.Ai.Indexer.IndexAsync(saved, ct).ConfigureAwait(false);
                    JsonElement after = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/ai/assistants/" + assistant + "/knowledge/status", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(0, after.GetProperty("itemCount").GetInt32(), "Deleted content removed from the index");
                }),

                AiTestKit.Case(Id, "Admin.ConversationAccessIsAudited", "Administrators list and read members' conversations with full telemetry; each read is audited", async (f, model, ct) =>
                {
                    string assistant = await AiTestKit.ReadyAsync(f, model, ct).ConfigureAwait(false);
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    JsonElement exchange = await AiTestKit.StartAsync(f, assistant, member.AccessToken, "A private question", ct).ConfigureAwait(false);
                    string conversationId = Api.Str(exchange.GetProperty("userMessage"), "conversationId");
                    JsonElement list = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/conversations/enumerate", HttpFixture.AdminToken, new { filters = new { userId = member.Id } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(conversationId, list.GetProperty("objects")[0].GetProperty("id").GetString(), "Admin list filtered by member");
                    Check.Equal("Member", list.GetProperty("objects")[0].GetProperty("owner").GetProperty("type").GetString(), "Owner shown");
                    JsonElement read = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/ai/conversations/" + conversationId, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement reply = read.GetProperty("recentMessages").GetProperty("objects")[1];
                    Check.True(reply.GetProperty("telemetry").GetProperty("stages").GetArrayLength() >= 1, "Telemetry for admins");
                    Check.True(reply.GetProperty("model").GetProperty("endpointId").GetString() != null, "Model details for admins");
                    DataTable audit = await f.Raw.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM auditrecords WHERE action = 'ai.conversation.read' AND targetid = '" + conversationId + "';", false, ct).ConfigureAwait(false);
                    Check.Equal(1L, RowReader.GetLong(audit.Rows[0], "cnt"), "Read audited");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/ai/conversations/enumerate", member.AccessToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                }),

                AiTestKit.Case(Id, "Visitors.PublicAssistants", "Signed out callers (anonymous browsing on) get a visitor token for Public assistants, chat with visitor limits, and see only their own conversations; Members assistants stay closed", async (f, model, ct) =>
                {
                    await SetAccessAsync(f, new { model = "PartiallyGated", anonymousBrowsing = true, gatedFeatures = Array.Empty<string>() }, ct).ConfigureAwait(false);
                    string assistant = await AiTestKit.ReadyAsync(f, model, ct, new { audience = "Public", limits = new { messagesPerVisitorPerDay = 1 } }).ConfigureAwait(false);
                    string members = await AiTestKit.ReadyAsync(f, model, ct).ConfigureAwait(false);
                    JsonElement session = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/assistant-visitors", null, null, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Dictionary<string, string> headers = new Dictionary<string, string> { ["X-TK-Visitor-Token"] = Api.Str(session, "visitorToken") };
                    ApiResult list = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/assistants/enumerate", null, new { }, ct, "widget", headers).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, list.Status, "Visitor list (" + list.Text + ")");
                    Check.Equal(1, list.Body.GetProperty("objects").GetArrayLength(), "Only the Public assistant");
                    ApiResult started = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/assistants/" + assistant + "/conversations", null, new { message = "Hi from a visitor" }, ct, "widget", headers).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Created, started.Status, "Visitor chat (" + started.Text + ")");
                    string conversationId = Api.Str(started.Body.GetProperty("userMessage"), "conversationId");
                    ApiResult limited = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/assistants/" + assistant + "/conversations", null, new { message = "Second" }, ct, "widget", headers).ConfigureAwait(false);
                    Check.Equal("AssistantLimitReached", limited.Error, "Visitor daily limit");
                    ApiResult closed = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/assistants/" + members + "/conversations", null, new { message = "Let me in" }, ct, "widget", headers).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Unauthorized, closed.Status, "Members assistant closed to visitors");
                    ApiResult fresh = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/assistants/" + assistant + "/conversations", null, new { }, ct, "widget").ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Created, fresh.Status, "A new visitor is created on first use");
                    Check.True(fresh.Headers.ContainsKey("X-TK-Visitor-Token"), "Token in the response header");
                    Dictionary<string, string> other = new Dictionary<string, string> { ["X-TK-Visitor-Token"] = fresh.Headers["X-TK-Visitor-Token"] };
                    ApiResult foreign = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/assistant-conversations/" + conversationId, null, null, ct, "widget", other).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.NotFound, foreign.Status, "Another visitor cannot read it");
                    DataTable row = await f.Raw.ExecuteQueryAsync("SELECT surface FROM aiconversations WHERE id = '" + conversationId + "';", false, ct).ConfigureAwait(false);
                    Check.Equal("Widget", RowReader.GetString(row.Rows[0], "surface"), "Widget surface");
                }),

                AiTestKit.Case(Id, "Access.Models", "Assistants follow the access model: FullyGated needs a membership and lets visitors use only embeddable Public assistants (the widget); PartiallyGated gates by audience or by the assistants feature; Open treats Members as SignedIn; new assistants take the property default audience", async (f, model, ct) =>
                {
                    string chat = await AiTestKit.EndpointAsync(f, AiTestKit.OllamaEndpoint(model, "Access chat"), ct).ConfigureAwait(false);
                    string publicPlain = await AiTestKit.AssistantAsync(f, chat, ct, new { audience = "Public" }, "Public plain").ConfigureAwait(false);
                    string publicEmbed = await AiTestKit.AssistantAsync(f, chat, ct, new { audience = "Public", embed = new { enabled = true } }, "Public widget").ConfigureAwait(false);
                    string signedIn = await AiTestKit.AssistantAsync(f, chat, ct, new { audience = "SignedIn" }, "Signed in").ConfigureAwait(false);
                    string members = await AiTestKit.AssistantAsync(f, chat, ct, new { audience = "Members" }, "Members").ConfigureAwait(false);
                    TestMember free = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);

                    // FullyGated (the fixture default).
                    ApiResult gated = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/assistants/" + publicPlain, free.AccessToken, null, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.PaymentRequired, gated.Status, "FullyGated: free accounts need a membership (" + gated.Text + ")");
                    Check.Equal("assistants", Api.Str(gated.Body, "feature"), "feature assistants");
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/assistants/" + members, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/assistants/enumerate", null, new { }, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Unauthorized, (await Api.SendAsync(f, HttpMethod.Post, "/api/v1/assistants/" + publicPlain + "/conversations", null, new { }, ct, "widget").ConfigureAwait(false)).Status, "FullyGated: a Public assistant that is not embeddable is closed to visitors");
                    Check.Equal(HttpStatusCode.Created, (await Api.SendAsync(f, HttpMethod.Post, "/api/v1/assistants/" + publicEmbed + "/conversations", null, new { }, ct, "widget").ConfigureAwait(false)).Status, "FullyGated: the widget serves an embeddable Public assistant to visitors");

                    // PartiallyGated, assistants not gated.
                    await SetAccessAsync(f, new { model = "PartiallyGated", anonymousBrowsing = true, gatedFeatures = Array.Empty<string>(), defaultAudience = "SignedIn" }, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/assistants/" + signedIn, free.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/assistants/" + members, free.AccessToken, null, HttpStatusCode.PaymentRequired, "MembershipRequired", ct).ConfigureAwait(false);
                    JsonElement freeList = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/assistants/enumerate", free.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    List<string> freeIds = freeList.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "id")).ToList();
                    Check.True(freeIds.Contains(signedIn) && freeIds.Contains(publicPlain) && !freeIds.Contains(members), "Free accounts list Public and SignedIn assistants");
                    Check.Equal(HttpStatusCode.Created, (await Api.SendAsync(f, HttpMethod.Post, "/api/v1/assistants/" + publicPlain + "/conversations", null, new { }, ct, "web").ConfigureAwait(false)).Status, "Anonymous browsing opens Public assistants to visitors");
                    JsonElement created = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/assistants", HttpFixture.AdminToken,
                        new { name = "Default audience " + Guid.NewGuid().ToString("N").Substring(0, 6), systemPrompt = "Be brief.", models = new { chat } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Check.Equal("SignedIn", Api.Str(created, "audience"), "New assistants take access.defaultAudience");

                    // PartiallyGated, assistants gated.
                    await SetAccessAsync(f, new { gatedFeatures = new[] { "assistants" } }, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/assistants/" + signedIn, free.AccessToken, null, HttpStatusCode.PaymentRequired, "MembershipRequired", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/assistants/enumerate", free.AccessToken, new { }, HttpStatusCode.PaymentRequired, "MembershipRequired", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/assistants/" + signedIn, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/assistants/enumerate", null, new { }, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Unauthorized, (await Api.SendAsync(f, HttpMethod.Post, "/api/v1/assistants/" + publicPlain + "/conversations", null, new { }, ct, "web").ConfigureAwait(false)).Status, "A gated feature closes Public assistants to visitors");
                    Check.Equal(HttpStatusCode.Created, (await Api.SendAsync(f, HttpMethod.Post, "/api/v1/assistants/" + publicEmbed + "/conversations", null, new { }, ct, "widget").ConfigureAwait(false)).Status, "The widget still serves an embeddable Public assistant");

                    // Open.
                    await SetAccessAsync(f, new { model = "Open", anonymousBrowsing = false }, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/assistants/" + members, free.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Unauthorized, (await Api.SendAsync(f, HttpMethod.Post, "/api/v1/assistants/" + publicPlain + "/conversations", null, new { }, ct, "web").ConfigureAwait(false)).Status, "Without anonymous browsing visitors need the widget");
                }),

                AiTestKit.Case(Id, "Test.ConsoleStreamsDebugWithoutStoring", "The admin test console streams debug events and the reply for a draft without storing messages", async (f, model, ct) =>
                {
                    string assistant = await AiTestKit.ReadyAsync(f, model, ct).ConfigureAwait(false);
                    List<SseEvent> events = await AiTestKit.SseAsync(f, "/api/v1/admin/ai/assistants/" + assistant + "/test", HttpFixture.AdminToken,
                        new { messages = new[] { new { role = "user", content = "Draft test" } }, useDraft = new { systemPrompt = "Draft prompt for {name}" } }, ct).ConfigureAwait(false);
                    SseEvent debug = events.First(e => e.Name == "debug");
                    Check.True(debug.Data.GetProperty("systemPrompt").GetString()!.StartsWith("Draft prompt for ", StringComparison.Ordinal), "Draft system prompt with tokens substituted");
                    Check.Equal("reply.completed", events.Last().Name, "Completed");
                    Check.True(events.Last().Data.GetProperty("reply").GetProperty("telemetry").ValueKind == JsonValueKind.Object, "Telemetry for the console");
                    long stored = await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM aimessages WHERE propertyid = '" + f.Services.PropertyId + "'", ct).ConfigureAwait(false);
                    Check.Equal(0L, stored, "Nothing stored");
                    JsonElement overview = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/ai/analytics/overview", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(0L, overview.GetProperty("requests").GetInt64(), "Test traffic excluded from analytics");
                })
            });
        }

        #endregion

        #region Private-Methods

        private static async Task SetAccessAsync(HttpFixture f, object body, CancellationToken ct)
        {
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/access", HttpFixture.AdminToken, body, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        #endregion
    }
}
