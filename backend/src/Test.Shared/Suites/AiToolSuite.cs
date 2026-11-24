namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared.Fakes;
    using Touchstone.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services.Ai;

    /// <summary>
    /// Assistant tools (docs/AI_CONTRACT.md section 8): the tool policy and the registry console, the bounded loop
    /// (iterations, calls per turn, token budget, per-call timeout, schema validation), SSE tool events and stored tool
    /// calls with citations, tools running as the caller (members, signed-in non-members, and visitors see only what
    /// the app shows them), property isolation, the per-tool kill switch with audit, provider modes (native Ollama and
    /// OpenAI wire formats, the Gemini and Anthropic wire formats, the prompt protocol, the automatic prompt fallback,
    /// and Off), the tool routing model, the administrator test console and conversation view, the getThread,
    /// listForums, and getMyProfile tools, and tool calls in moderation snapshots and the account export. Every model is
    /// the in-process fake.
    /// </summary>
    public static class AiToolSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "AiTools";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "AI assistant tools (" + TestEnvironment.Provider + ")", new[]
            {
                AiTestKit.Case(Id, "Tools.PolicyAndRegistry", "Unknown tools, an empty allowed list, and out-of-range limits are rejected; aliases normalize; the registry lists every engine tool with labels and aliases; toolRouting and endpoint toolCalling are accepted", async (f, model, ct) =>
                {
                    string chat = await AiTestKit.EndpointAsync(f, AiTestKit.OllamaEndpoint(model, "Chat"), ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/ai/assistants", HttpFixture.AdminToken, new { name = "Bad", models = new { chat }, tools = new { enabled = true, allowed = new[] { "noSuchTool" } } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/ai/assistants", HttpFixture.AdminToken, new { name = "Bad", models = new { chat }, tools = new { enabled = true, allowed = Array.Empty<string>() } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/ai/assistants", HttpFixture.AdminToken, new { name = "Bad", models = new { chat }, tools = new { maxToolIterations = 11 } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/ai/assistants", HttpFixture.AdminToken, new { name = "Bad", models = new { chat }, tools = new { maxTokensPerTurn = 500 } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    string id = await AiTestKit.AssistantAsync(f, chat, ct, new { models = new { chat, toolRouting = chat }, tools = new { enabled = true, allowed = new[] { "findGyms", "searchFeed", "FINDVENUES" }, maxToolCallsPerTurn = 3 } }).ConfigureAwait(false);
                    JsonElement admin = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/ai/assistants/" + id, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement tools = admin.GetProperty("tools");
                    Check.True(tools.GetProperty("enabled").GetBoolean(), "Tools enabled");
                    Check.True(tools.GetProperty("allowed").EnumerateArray().Select(a => a.GetString()).SequenceEqual(new[] { "findVenues", "searchFeed" }), "Aliases normalized and duplicates removed (" + tools + ")");
                    Check.Equal(3, tools.GetProperty("maxToolCallsPerTurn").GetInt32(), "Calls per turn");
                    Check.Equal(4, tools.GetProperty("maxToolIterations").GetInt32(), "Default iterations");
                    Check.Equal(32000, tools.GetProperty("maxTokensPerTurn").GetInt32(), "Default token budget");
                    Check.Equal(chat, admin.GetProperty("models").GetProperty("toolRouting").GetString(), "Tool routing model accepted");
                    JsonElement updated = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/ai/assistants/" + id, HttpFixture.AdminToken, new { tools = new { allowed = new[] { "searchLibrary" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(updated.GetProperty("tools").GetProperty("allowed").EnumerateArray().Select(a => a.GetString()).SequenceEqual(new[] { "searchLibrary" }), "allowed replaces the list");
                    Check.Equal(3, updated.GetProperty("tools").GetProperty("maxToolCallsPerTurn").GetInt32(), "Other tool fields merge");

                    JsonElement registry = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/tools/enumerate", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    List<string> names = registry.GetProperty("objects").EnumerateArray().Select(t => Api.Str(t, "name")).ToList();
                    foreach (string expected in new[] { "searchLibrary", "findEvents", "findVenues", "searchFeed", "searchForums", "getPropertyInfo" })
                        Check.True(names.Contains(expected), "Registry lists " + expected + " (" + String.Join(",", names) + ")");
                    JsonElement venues = registry.GetProperty("objects").EnumerateArray().First(t => Api.Str(t, "name") == "findVenues");
                    Check.True(venues.GetProperty("aliases").EnumerateArray().Any(a => a.GetString() == "findGyms"), "findGyms alias listed");
                    JsonElement library = registry.GetProperty("objects").EnumerateArray().First(t => Api.Str(t, "name") == "searchLibrary");
                    Check.Equal("Searching the library", Api.Str(library, "label"), "Activity label");
                    Check.Equal("Searched the library", Api.Str(library, "completedLabel"), "Completed label");
                    Check.Equal(JsonValueKind.Object, library.GetProperty("parametersSchema").ValueKind, "Schema object");
                    JsonElement firstPage = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/tools/enumerate", HttpFixture.AdminToken, new { maxResults = 1 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    string next = Api.Str(firstPage, "continuationToken");
                    JsonElement nextPage = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/tools/enumerate", HttpFixture.AdminToken, new { maxResults = 1, continuationToken = next, skip = 99 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, nextPage.GetProperty("objects").GetArrayLength(), "Cursor takes precedence over skip");
                    Check.True(Api.Str(firstPage.GetProperty("objects")[0], "name") != Api.Str(nextPage.GetProperty("objects")[0], "name"), "Stable next tool");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/ai/tools/enumerate", HttpFixture.AdminToken, new { continuationToken = next, search = "changed" }, HttpStatusCode.BadRequest, "InvalidContinuationToken", ct).ConfigureAwait(false);
                    JsonElement future = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/tools/enumerate", HttpFixture.AdminToken, new { createdAfterUtc = DateTime.UtcNow.AddDays(1) }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(0, future.GetProperty("objects").GetArrayLength(), "Tool creation filter honored");
                    JsonElement searched = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/tools/enumerate", HttpFixture.AdminToken, new { search = "Search forum threads", maxResults = 1 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("searchForums", Api.Str(searched.GetProperty("objects")[0], "name"), "Search");
                    JsonElement clamped = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/tools/enumerate", HttpFixture.AdminToken, new { maxResults = 5000 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(f.Settings.Enumeration.MaxResultsAdmin, clamped.GetProperty("maxResults").GetInt32(), "maxResults clamps to enumeration.maxResultsAdmin");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/ai/tools/enumerate", (await Api.MemberAsync(f, ct).ConfigureAwait(false)).AccessToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);

                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    JsonElement summary = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/assistants/" + id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(summary.GetProperty("toolsEnabled").GetBoolean(), "Members see toolsEnabled");
                    Check.True(!summary.ToString().Contains("searchLibrary", StringComparison.Ordinal), "Members do not see the tool policy");

                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/ai/endpoints/" + chat, HttpFixture.AdminToken, new { parameters = new { toolCalling = "Prompt" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement endpoint = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/ai/endpoints/" + chat, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Prompt", endpoint.GetProperty("parameters").GetProperty("toolCalling").GetString(), "Endpoint toolCalling");
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/ai/endpoints/" + chat, HttpFixture.AdminToken, new { parameters = new { toolCalling = "Sometimes" } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                }),

                AiTestKit.Case(Id, "Tools.StreamEventsAndPersistence", "A tool round streams tool.started and tool.completed between the deltas, the deltas still equal the content, the tool output reaches the model, the call is stored with result links and cited, each round is its own stage and usage record, and admins see arguments members do not", async (f, model, ct) =>
                {
                    RegisterEcho(f);
                    model.ToolCalls = new List<FakeToolCall> { new FakeToolCall { Name = "testEcho", ArgumentsJson = "{\"query\":\"photo\"}" } };
                    model.ToolPreamble = "Let me check.";
                    string assistant = await AiTestKit.ReadyAsync(f, model, ct, new { tools = new { enabled = true, allowed = new[] { "testEcho" } } }).ConfigureAwait(false);
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    JsonElement conversation = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/assistants/" + assistant + "/conversations", member.AccessToken, new { }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    string conversationId = Api.Str(conversation, "id");
                    model.ClearRequests();
                    List<SseEvent> events = await AiTestKit.SseAsync(f, "/api/v1/assistant-conversations/" + conversationId + "/messages", member.AccessToken, new { content = "How do I change my photo?" }, ct).ConfigureAwait(false);
                    List<string> names = events.Select(e => e.Name).ToList();
                    string order = String.Join(",", names);
                    int started = names.IndexOf("tool.started");
                    int completed = names.IndexOf("tool.completed");
                    Check.True(started > names.IndexOf("reply.started") && completed > started && names.LastIndexOf("reply.delta") > completed, "Tool events between deltas (" + order + ")");
                    Check.Equal("reply.completed", names[names.Count - 1], "Terminal event");
                    Check.Equal(1, names.Count(n => n == "reply.completed" || n == "error" || n == "reply.blocked"), "One terminal event");
                    JsonElement startedData = events[started].Data;
                    Check.Equal("testEcho", Api.Str(startedData, "toolName"), "tool.started name");
                    Check.Equal("Echoing", Api.Str(startedData, "label"), "tool.started label");
                    JsonElement done = events[completed].Data;
                    Check.Equal("Succeeded", Api.Str(done, "status"), "tool.completed status (" + done + ")");
                    Check.Equal("Echoed", Api.Str(done, "completedLabel"), "Completed label");
                    Check.Equal(1, done.GetProperty("resultCount").GetInt32(), "Result count");
                    Check.Equal("/feed/fpo_echo", Api.Str(done.GetProperty("results")[0], "path"), "Result link");
                    string text = String.Concat(events.Where(e => e.Name == "reply.delta").Select(e => e.Data.GetProperty("text").GetString()));
                    JsonElement reply = events.Last().Data.GetProperty("reply");
                    Check.Equal(text, Api.Str(reply, "content"), "Deltas equal the content across rounds");
                    Check.True(text.StartsWith("Let me check.", StringComparison.Ordinal) && text.EndsWith(model.ReplyText, StringComparison.Ordinal), "Both rounds' text (" + text + ")");
                    JsonElement calls = reply.GetProperty("toolCalls");
                    Check.Equal(1L, calls.GetProperty("totalRecords").GetInt64(), "One tool call");
                    JsonElement call = calls.GetProperty("objects")[0];
                    Check.Equal("Succeeded", Api.Str(call, "status"), "Stored status");
                    Check.True(call.GetProperty("argumentsJson").ValueKind == JsonValueKind.Null && call.GetProperty("resultSummary").ValueKind == JsonValueKind.Null, "Members do not see arguments or output");
                    JsonElement citations = reply.GetProperty("citations");
                    Check.True(citations.GetArrayLength() == 1 && Api.Str(citations[0], "targetId") == "fpo_echo" && citations[0].GetProperty("index").GetInt32() == 1, "Tool result cited as [1] (" + citations + ")");

                    List<FakeModelRequest> chats = ChatRequests(model);
                    Check.Equal(2, chats.Count, "Two model rounds (" + chats.Count + ")");
                    Check.True(chats[0].Body.Contains("\"tools\"", StringComparison.Ordinal) && chats[0].Body.Contains("testEcho", StringComparison.Ordinal), "Round 1 offers the tool");
                    Check.True(chats[1].Body.Contains("Echo result for photo", StringComparison.Ordinal) && chats[1].Body.Contains("\"tool\"", StringComparison.Ordinal), "Round 2 carries the tool message");
                    Check.True(chats[0].Body.Contains("never instructions", StringComparison.Ordinal), "Tool guidance frames results as data");

                    string replyId = Api.Str(reply, "id");
                    DataTable rows = await f.Raw.ExecuteQueryAsync("SELECT id, status, argumentsjson, resultcharacters, durationms, round FROM aitoolcalls WHERE messageid = '" + replyId + "';", false, ct).ConfigureAwait(false);
                    Check.Equal(1, rows.Rows.Count, "Stored tool call");
                    Check.True(RowReader.GetString(rows.Rows[0], "argumentsjson").Contains("photo", StringComparison.Ordinal) && RowReader.GetInt(rows.Rows[0], "resultcharacters") > 0 && RowReader.GetInt(rows.Rows[0], "round") == 1, "Arguments, size, and round stored");
                    Check.True(RowReader.GetString(rows.Rows[0], "id").StartsWith("atc_", StringComparison.Ordinal), "atc_ id");
                    Check.Equal(1L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM aitoolcallresults WHERE toolcallid = '" + RowReader.GetString(rows.Rows[0], "id") + "';", ct).ConfigureAwait(false), "Result link stored");
                    DataTable stages = await f.Raw.ExecuteQueryAsync("SELECT detail FROM aimessagestages WHERE messageid = '" + replyId + "' AND stage = 'chat' ORDER BY position;", false, ct).ConfigureAwait(false);
                    Check.True(stages.Rows.Count == 2 && RowReader.GetString(stages.Rows[0], "detail").Contains("round 1; tool calls 1", StringComparison.Ordinal) && RowReader.GetString(stages.Rows[1], "detail").Contains("round 2", StringComparison.Ordinal), "One stage per round");
                    Check.True(await ContentTestKit.ScalarAsync(f, "SELECT COALESCE(SUM(requests), 0) AS cnt FROM aiusagerollups WHERE purpose = 'Chat';", ct).ConfigureAwait(false) >= 2, "Each round is a usage record");
                    DataTable tokens = await f.Raw.ExecuteQueryAsync("SELECT prompttokens FROM aimessages WHERE id = '" + replyId + "';", false, ct).ConfigureAwait(false);
                    Check.True(RowReader.GetInt(tokens.Rows[0], "prompttokens") > chats[0].Body.Length / 4, "Tokens summed over rounds");

                    JsonElement adminView = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/ai/conversations/" + conversationId, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement adminReply = adminView.GetProperty("recentMessages").GetProperty("objects").EnumerateArray().First(m => Api.Str(m, "id") == replyId);
                    JsonElement adminCall = adminReply.GetProperty("toolCalls").GetProperty("objects")[0];
                    Check.True(Api.Str(adminCall, "argumentsJson").Contains("photo", StringComparison.Ordinal) && Api.Str(adminCall, "resultSummary").Contains("Echo result", StringComparison.Ordinal), "Admins see arguments and the output summary");
                    JsonElement memberRead = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/assistant-conversations/" + conversationId + "/messages/" + replyId, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("/feed/fpo_echo", Api.Str(memberRead.GetProperty("toolCalls").GetProperty("objects")[0].GetProperty("results")[0], "path"), "Persisted results for members");

                    model.ToolPreamble = null;
                    JsonElement json = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/assistant-conversations/" + conversationId + "/messages", member.AccessToken, new { content = "And again?" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Check.Equal(1L, json.GetProperty("reply").GetProperty("toolCalls").GetProperty("totalRecords").GetInt64(), "JSON replies run tools too");
                    Check.Equal(model.ReplyText, Api.Str(json.GetProperty("reply"), "content"), "JSON reply content");
                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/assistant-conversations/" + conversationId, member.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                    Check.Equal(0L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM aitoolcalls WHERE messageid = '" + replyId + "';", ct).ConfigureAwait(false), "Tool calls deleted with the conversation");
                }),

                AiTestKit.Case(Id, "Tools.LoopLimits", "maxToolIterations and maxToolCallsPerTurn stop the loop with a final call without tools, extra calls are rejected, the token budget stops tool rounds, slow tools time out, and invalid arguments are rejected by the schema", async (f, model, ct) =>
                {
                    RegisterEcho(f);
                    f.Services.Ai.Tools.Register(new AssistantTool
                    {
                        Name = "testSlow",
                        Description = "A slow tool.",
                        Handler = async (caller, args, token) =>
                        {
                            await Task.Delay(5000, token).ConfigureAwait(false);
                            return new AssistantToolResult { Text = "late" };
                        }
                    });
                    model.ToolCalls = new List<FakeToolCall> { new FakeToolCall { Name = "testEcho", ArgumentsJson = "{\"query\":\"a\"}" }, new FakeToolCall { Name = "testEcho", ArgumentsJson = "{\"query\":\"b\"}" } };
                    model.ToolRounds = 100;
                    string assistant = await AiTestKit.ReadyAsync(f, model, ct, new { tools = new { enabled = true, allowed = new[] { "testEcho", "testSlow" }, maxToolIterations = 2, maxToolCallsPerTurn = 3 } }).ConfigureAwait(false);
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    model.ClearRequests();
                    JsonElement exchange = await AiTestKit.StartAsync(f, assistant, member.AccessToken, "Loop please", ct).ConfigureAwait(false);
                    JsonElement reply = exchange.GetProperty("reply");
                    Check.Equal("Completed", Api.Str(reply, "status"), "Reply completes (" + reply + ")");
                    Check.Equal(model.ReplyText, Api.Str(reply, "content"), "Final answer without tools");
                    List<string> statuses = reply.GetProperty("toolCalls").GetProperty("objects").EnumerateArray().Select(c => Api.Str(c, "status")).ToList();
                    Check.True(statuses.SequenceEqual(new[] { "Succeeded", "Succeeded", "Succeeded", "Rejected" }), "Three calls run, the fourth rejected (" + String.Join(",", statuses) + ")");
                    DataTable limit = await f.Raw.ExecuteQueryAsync("SELECT error FROM aitoolcalls WHERE messageid = '" + Api.Str(reply, "id") + "' AND status = 'Rejected';", false, ct).ConfigureAwait(false);
                    Check.Equal("ToolLimit", RowReader.GetString(limit.Rows[0], "error"), "Rejected for the call limit");
                    List<FakeModelRequest> chats = ChatRequests(model);
                    Check.Equal(3, chats.Count, "Two tool rounds and a final round (" + chats.Count + ")");
                    Check.True(!chats[2].Body.Contains("\"tools\"", StringComparison.Ordinal), "The final round offers no tools");
                    Check.True(chats[2].Body.Contains("Tool result for", StringComparison.Ordinal), "The final round carries the results as plain messages");

                    string budget = await AiTestKit.ReadyAsync(f, model, ct, new { systemPrompt = new string('x', 6000), tools = new { enabled = true, allowed = new[] { "testEcho" }, maxTokensPerTurn = 1000 } }).ConfigureAwait(false);
                    JsonElement budgeted = (await AiTestKit.StartAsync(f, budget, member.AccessToken, "Budget please", ct).ConfigureAwait(false)).GetProperty("reply");
                    Check.Equal(2L, budgeted.GetProperty("toolCalls").GetProperty("totalRecords").GetInt64(), "One tool round before the token budget stops tools");
                    DataTable detail = await f.Raw.ExecuteQueryAsync("SELECT detail FROM aimessagestages WHERE messageid = '" + Api.Str(budgeted, "id") + "' AND stage = 'chat' ORDER BY position DESC LIMIT 1;", false, ct).ConfigureAwait(false);
                    Check.True(RowReader.GetString(detail.Rows[0], "detail").Contains("limit tokens", StringComparison.Ordinal), "Stage names the token limit (" + RowReader.GetString(detail.Rows[0], "detail") + ")");

                    model.ToolRounds = 1;
                    model.ToolCalls = new List<FakeToolCall> { new FakeToolCall { Name = "testSlow", ArgumentsJson = "{}" } };
                    string slow = await AiTestKit.ReadyAsync(f, model, ct, new { tools = new { enabled = true, allowed = new[] { "testSlow" }, toolCallTimeoutMs = 200 } }).ConfigureAwait(false);
                    JsonElement timed = (await AiTestKit.StartAsync(f, slow, member.AccessToken, "Slow please", ct).ConfigureAwait(false)).GetProperty("reply");
                    Check.Equal("TimedOut", Api.Str(timed.GetProperty("toolCalls").GetProperty("objects")[0], "status"), "Per-tool timeout");
                    Check.True(timed.GetProperty("toolCalls").GetProperty("objects")[0].GetProperty("durationMs").GetInt64() < 4000, "Stopped at the timeout");
                    Check.Equal("Completed", Api.Str(timed, "status"), "The reply still completes");

                    model.ToolCalls = new List<FakeToolCall> { new FakeToolCall { Name = "testEcho", ArgumentsJson = "{\"query\":5}" } };
                    JsonElement invalid = (await AiTestKit.StartAsync(f, assistant, member.AccessToken, "Bad arguments", ct).ConfigureAwait(false)).GetProperty("reply");
                    Check.Equal("Rejected", Api.Str(invalid.GetProperty("toolCalls").GetProperty("objects")[0], "status"), "Schema validation rejects");
                    DataTable schema = await f.Raw.ExecuteQueryAsync("SELECT error FROM aitoolcalls WHERE messageid = '" + Api.Str(invalid, "id") + "';", false, ct).ConfigureAwait(false);
                    Check.True(RowReader.GetString(schema.Rows[0], "error").StartsWith("InvalidArguments", StringComparison.Ordinal), "InvalidArguments (" + RowReader.GetString(schema.Rows[0], "error") + ")");
                }),

                AiTestKit.Case(Id, "Tools.RunAsTheCaller", "searchFeed runs as the caller: members get Members posts, signed-in non-members only Public and SignedIn, visitors only Public; members-only tools are never offered to visitors and are rejected when called", async (f, model, ct) =>
                {
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/access", HttpFixture.AdminToken, new { model = "PartiallyGated", anonymousBrowsing = true, gatedFeatures = Array.Empty<string>() }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    f.Services.Ai.Tools.Register(new AssistantTool { Name = "testMembersOnly", Description = "Members only.", Handler = (c, a, t) => Task.FromResult(new AssistantToolResult { Text = "secret" }) });
                    TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
                    string marker = "toolmark" + new string(Guid.NewGuid().ToString("N").Where(Char.IsLetter).Take(6).ToArray());
                    string pub = Api.Str(await ContentTestKit.PostAsync(f, admin.AccessToken, new { title = "Public " + marker, body = "Public " + marker + " post", audience = "Public" }, ct).ConfigureAwait(false), "id");
                    string signedIn = Api.Str(await ContentTestKit.PostAsync(f, admin.AccessToken, new { title = "SignedIn " + marker, body = "SignedIn " + marker + " post", audience = "SignedIn" }, ct).ConfigureAwait(false), "id");
                    string members = Api.Str(await ContentTestKit.PostAsync(f, admin.AccessToken, new { title = "Members " + marker, body = "Members " + marker + " post", audience = "Members" }, ct).ConfigureAwait(false), "id");
                    model.ToolCalls = new List<FakeToolCall> { new FakeToolCall { Name = "searchFeed", ArgumentsJson = "{\"query\":\"" + marker + "\",\"maxResults\":10}" } };
                    string assistant = await AiTestKit.ReadyAsync(f, model, ct, new { audience = "Public", tools = new { enabled = true, allowed = new[] { "searchFeed", "testMembersOnly" } } }).ConfigureAwait(false);

                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    model.ClearRequests();
                    List<string> seen = Results(await AiTestKit.StartAsync(f, assistant, member.AccessToken, "Find " + marker, ct).ConfigureAwait(false));
                    Check.True(seen.Contains(pub) && seen.Contains(signedIn) && seen.Contains(members), "Member sees all three (" + String.Join(",", seen) + ")");
                    Check.True(ChatRequests(model)[0].Body.Contains("testMembersOnly", StringComparison.Ordinal), "Members are offered members-only tools");

                    TestMember free = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    model.ClearRequests();
                    List<string> freeSeen = Results(await AiTestKit.StartAsync(f, assistant, free.AccessToken, "Find " + marker, ct).ConfigureAwait(false));
                    Check.True(freeSeen.Contains(pub) && freeSeen.Contains(signedIn) && !freeSeen.Contains(members), "Signed-in non-member never gets the Members post (" + String.Join(",", freeSeen) + ")");
                    Check.True(!ChatRequests(model).Any(r => r.Body.Contains(members, StringComparison.Ordinal)), "The Members post never reaches the model for a non-member");

                    JsonElement session = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/assistant-visitors", null, null, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Dictionary<string, string> headers = new Dictionary<string, string> { ["X-TK-Visitor-Token"] = Api.Str(session, "visitorToken") };
                    model.ClearRequests();
                    ApiResult visitor = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/assistants/" + assistant + "/conversations", null, new { message = "Find " + marker }, ct, "widget", headers).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Created, visitor.Status, "Visitor chat (" + visitor.Text + ")");
                    List<string> visitorSeen = Results(visitor.Body);
                    Check.True(visitorSeen.SequenceEqual(new[] { pub }), "Visitor sees only the Public post (" + String.Join(",", visitorSeen) + ")");
                    List<FakeModelRequest> visitorChats = ChatRequests(model);
                    Check.True(!visitorChats[0].Body.Contains("testMembersOnly", StringComparison.Ordinal), "Members-only tools are not offered to visitors");
                    Check.True(!visitorChats.Any(r => r.Body.Contains(signedIn, StringComparison.Ordinal) || r.Body.Contains(members, StringComparison.Ordinal)), "Gated posts never reach the model for a visitor");

                    model.ToolCalls = new List<FakeToolCall> { new FakeToolCall { Name = "testMembersOnly", ArgumentsJson = "{}" } };
                    ApiResult forced = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/assistants/" + assistant + "/conversations", null, new { message = "Members only please" }, ct, "widget", new Dictionary<string, string> { ["X-TK-Visitor-Token"] = Api.Str((await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/assistant-visitors", null, null, HttpStatusCode.Created, ct).ConfigureAwait(false)), "visitorToken") }).ConfigureAwait(false);
                    JsonElement forcedCall = forced.Body.GetProperty("reply").GetProperty("toolCalls").GetProperty("objects")[0];
                    Check.Equal("Rejected", Api.Str(forcedCall, "status"), "A visitor calling a members-only tool is rejected");
                    Check.Equal(0L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM aitoolcalls WHERE toolname = 'testMembersOnly' AND status = 'Succeeded';", ct).ConfigureAwait(false), "The members-only tool never ran");
                }),

                AiTestKit.Case(Id, "Tools.KillSwitchAndAudit", "Disabling a tool property-wide removes it from every assistant's offered tools and rejects calls to it; switching is audited; unknown tools are 404 and a missing flag is 400", async (f, model, ct) =>
                {
                    RegisterEcho(f);
                    model.ToolCalls = new List<FakeToolCall> { new FakeToolCall { Name = "testEcho", ArgumentsJson = "{\"query\":\"x\"}" } };
                    string assistant = await AiTestKit.ReadyAsync(f, model, ct, new { tools = new { enabled = true, allowed = new[] { "testEcho", "getPropertyInfo" } } }).ConfigureAwait(false);
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    JsonElement off = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/ai/tools/testEcho", HttpFixture.AdminToken, new { disabled = true, reason = "Investigating results" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(off.GetProperty("disabled").GetBoolean() && Api.Str(off, "disabledReason") == "Investigating results", "Switched off");
                    JsonElement disabled = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/tools/enumerate", HttpFixture.AdminToken, new { filters = new { disabled = true } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(disabled.GetProperty("objects").GetArrayLength() == 1 && Api.Str(disabled.GetProperty("objects")[0], "name") == "testEcho", "Filter disabled");
                    Check.Equal(1L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM auditrecords WHERE action = 'ai.tool.disable' AND targetid = 'testEcho';", ct).ConfigureAwait(false), "Disable audited");
                    model.ClearRequests();
                    JsonElement reply = (await AiTestKit.StartAsync(f, assistant, member.AccessToken, "Echo please", ct).ConfigureAwait(false)).GetProperty("reply");
                    List<FakeModelRequest> chats = ChatRequests(model);
                    Check.True(!chats[0].Body.Contains("\"name\":\"testEcho\"", StringComparison.Ordinal) && chats[0].Body.Contains("getPropertyInfo", StringComparison.Ordinal), "The disabled tool is not offered");
                    Check.Equal("Rejected", Api.Str(reply.GetProperty("toolCalls").GetProperty("objects")[0], "status"), "A call to the disabled tool is rejected");
                    DataTable error = await f.Raw.ExecuteQueryAsync("SELECT error FROM aitoolcalls WHERE messageid = '" + Api.Str(reply, "id") + "';", false, ct).ConfigureAwait(false);
                    Check.Equal("NotAllowed", RowReader.GetString(error.Rows[0], "error"), "Not offered, so not allowed this turn");

                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/ai/tools/getPropertyInfo", HttpFixture.AdminToken, new { disabled = true }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    model.ClearRequests();
                    JsonElement none = (await AiTestKit.StartAsync(f, assistant, member.AccessToken, "Anything?", ct).ConfigureAwait(false)).GetProperty("reply");
                    Check.True(!ChatRequests(model)[0].Body.Contains("\"tools\"", StringComparison.Ordinal), "No tools offered when every allowed tool is off");
                    Check.Equal(0L, none.GetProperty("toolCalls").GetProperty("totalRecords").GetInt64(), "No tool calls");

                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/ai/tools/testEcho", HttpFixture.AdminToken, new { disabled = false }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM auditrecords WHERE action = 'ai.tool.enable' AND targetid = 'testEcho';", ct).ConfigureAwait(false), "Enable audited");
                    JsonElement back = (await AiTestKit.StartAsync(f, assistant, member.AccessToken, "Echo again", ct).ConfigureAwait(false)).GetProperty("reply");
                    Check.Equal("Succeeded", Api.Str(back.GetProperty("toolCalls").GetProperty("objects")[0], "status"), "Runs again once enabled");
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/ai/tools/noSuchTool", HttpFixture.AdminToken, new { disabled = true }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/ai/tools/testEcho", HttpFixture.AdminToken, new { reason = "x" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    JsonElement alias = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/ai/tools/findGyms", HttpFixture.AdminToken, new { disabled = true }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("findVenues", Api.Str(alias, "name"), "The alias switches the canonical tool");
                }),

                AiTestKit.Case(Id, "Tools.ProviderModes", "OpenAI and Azure (Responses API) and OpenAI-compatible (Chat Completions) wire formats stream native tool calls; Prompt endpoints use the JSON protocol without leaking it to the member; a Native endpoint that rejects tools falls back to the protocol; Off endpoints get no tools", async (f, model, ct) =>
                {
                    RegisterEcho(f);
                    model.ToolCalls = new List<FakeToolCall> { new FakeToolCall { Name = "testEcho", ArgumentsJson = "{\"query\":\"wire\"}" } };
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    object toolPolicy = new { enabled = true, allowed = new[] { "testEcho" } };

                    string openAi = await AiTestKit.EndpointAsync(f, new { name = "OpenAI tools", kind = "Completion", apiType = "OpenAI", model = "fake-chat", endpointUrl = model.BaseUrl, authType = "Bearer", authValue = "k", parameters = new { streaming = true, timeoutMs = 10000 }, limits = new { maxRetries = 0 }, healthCheck = new { enabled = false } }, ct).ConfigureAwait(false);
                    string azure = await AiTestKit.EndpointAsync(f, new { name = "Azure tools", kind = "Completion", apiType = "AzureOpenAI", model = "fake-chat", endpointUrl = model.BaseUrl, authType = "ApiKey", authValue = "k", providerSettings = new { deployment = "chat-dep", apiVersion = "2024-10-21" }, parameters = new { streaming = true, timeoutMs = 10000 }, limits = new { maxRetries = 0 }, healthCheck = new { enabled = false } }, ct).ConfigureAwait(false);
                    string compatible = await AiTestKit.EndpointAsync(f, new { name = "Compatible tools", kind = "Completion", apiType = "OpenAICompatible", model = "fake-chat", endpointUrl = model.BaseUrl, authType = "Bearer", authValue = "k", parameters = new { streaming = true, timeoutMs = 10000 }, limits = new { maxRetries = 0 }, healthCheck = new { enabled = false } }, ct).ConfigureAwait(false);
                    Dictionary<string, string> wire = new Dictionary<string, string> { [openAi] = "/v1/responses", [azure] = "/openai/v1/responses", [compatible] = "/v1/chat/completions" };
                    foreach (string endpoint in new[] { openAi, azure, compatible })
                    {
                        model.ClearRequests();
                        string assistant = await AiTestKit.AssistantAsync(f, endpoint, ct, new { tools = toolPolicy }).ConfigureAwait(false);
                        JsonElement conversation = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/assistants/" + assistant + "/conversations", member.AccessToken, new { }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                        List<SseEvent> events = await AiTestKit.SseAsync(f, "/api/v1/assistant-conversations/" + Api.Str(conversation, "id") + "/messages", member.AccessToken, new { content = "Wire?" }, ct).ConfigureAwait(false);
                        JsonElement reply = events.Last().Data.GetProperty("reply");
                        Check.Equal("Succeeded", Api.Str(reply.GetProperty("toolCalls").GetProperty("objects")[0], "status"), "Native tool call over the OpenAI wire (" + endpoint + ")");
                        Check.Equal(model.ReplyText, Api.Str(reply, "content"), "Answer after the tool");
                        Check.True(model.Requests.Where(r => r.Method == "POST" && !r.Body.Contains("Write a short title", StringComparison.Ordinal)).All(r => r.Path == wire[endpoint]), "Wire route " + wire[endpoint] + " (" + String.Join(",", model.Requests.Select(r => r.Path)) + ")");
                    }

                    string prompt = await AiTestKit.EndpointAsync(f, AiTestKit.OllamaEndpoint(model, "Prompt tools", "Completion", new { parameters = new { streaming = true, timeoutMs = 10000, toolCalling = "Prompt" } }), ct).ConfigureAwait(false);
                    string promptAssistant = await AiTestKit.AssistantAsync(f, prompt, ct, new { tools = toolPolicy }).ConfigureAwait(false);
                    JsonElement promptConversation = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/assistants/" + promptAssistant + "/conversations", member.AccessToken, new { }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    model.ClearRequests();
                    List<SseEvent> promptEvents = await AiTestKit.SseAsync(f, "/api/v1/assistant-conversations/" + Api.Str(promptConversation, "id") + "/messages", member.AccessToken, new { content = "Prompt?" }, ct).ConfigureAwait(false);
                    string streamed = String.Concat(promptEvents.Where(e => e.Name == "reply.delta").Select(e => e.Data.GetProperty("text").GetString()));
                    JsonElement promptReply = promptEvents.Last().Data.GetProperty("reply");
                    Check.Equal(model.ReplyText, streamed, "The JSON tool request never reaches the member");
                    Check.Equal("Succeeded", Api.Str(promptReply.GetProperty("toolCalls").GetProperty("objects")[0], "status"), "Prompt protocol tool call");
                    List<FakeModelRequest> promptChats = ChatRequests(model);
                    Check.True(!promptChats[0].Body.Contains("\"tools\"", StringComparison.Ordinal) && promptChats[0].Body.Contains(ToolPromptProtocol.Marker, StringComparison.Ordinal), "Protocol in the system prompt, no native tools");
                    Check.True(promptChats[1].Body.Contains(ToolPromptProtocol.ResultPrefix, StringComparison.Ordinal), "Results as framed user messages");

                    model.RejectNativeTools = true;
                    string strict = await AiTestKit.EndpointAsync(f, AiTestKit.OllamaEndpoint(model, "No tool support"), ct).ConfigureAwait(false);
                    string strictAssistant = await AiTestKit.AssistantAsync(f, strict, ct, new { tools = toolPolicy }).ConfigureAwait(false);
                    model.ClearRequests();
                    JsonElement fallback = (await AiTestKit.StartAsync(f, strictAssistant, member.AccessToken, "Fallback?", ct).ConfigureAwait(false)).GetProperty("reply");
                    Check.Equal("Completed", Api.Str(fallback, "status"), "A model without tool support still answers (" + fallback + ")");
                    Check.Equal("Succeeded", Api.Str(fallback.GetProperty("toolCalls").GetProperty("objects")[0], "status"), "Through the prompt protocol");
                    List<FakeModelRequest> fallbackChats = ChatRequests(model);
                    Check.True(fallbackChats[0].Body.Contains("\"tools\"", StringComparison.Ordinal) && fallbackChats[1].Body.Contains(ToolPromptProtocol.Marker, StringComparison.Ordinal), "Native first, then the protocol");
                    model.RejectNativeTools = false;

                    string offEndpoint = await AiTestKit.EndpointAsync(f, AiTestKit.OllamaEndpoint(model, "No tools", "Completion", new { parameters = new { streaming = true, timeoutMs = 10000, toolCalling = "Off" } }), ct).ConfigureAwait(false);
                    string offAssistant = await AiTestKit.AssistantAsync(f, offEndpoint, ct, new { tools = toolPolicy }).ConfigureAwait(false);
                    model.ClearRequests();
                    JsonElement off = (await AiTestKit.StartAsync(f, offAssistant, member.AccessToken, "Off?", ct).ConfigureAwait(false)).GetProperty("reply");
                    Check.Equal(0L, off.GetProperty("toolCalls").GetProperty("totalRecords").GetInt64(), "Off endpoints never call tools");
                    Check.True(ChatRequests(model).All(r => !r.Body.Contains("\"tools\"", StringComparison.Ordinal) && !r.Body.Contains(ToolPromptProtocol.Marker, StringComparison.Ordinal)), "No tools and no protocol");
                }),

                AiTestKit.Case(Id, "Tools.RoutingAndTestConsole", "With a toolRouting model the routing model decides the calls without streaming and the chat model writes the answer without tools; the admin test console streams tool debug events", async (f, model, ct) =>
                {
                    RegisterEcho(f);
                    model.ToolCalls = new List<FakeToolCall> { new FakeToolCall { Name = "testEcho", ArgumentsJson = "{\"query\":\"route\"}" } };
                    string chat = await AiTestKit.EndpointAsync(f, AiTestKit.OllamaEndpoint(model, "Chat"), ct).ConfigureAwait(false);
                    string router = await AiTestKit.EndpointAsync(f, AiTestKit.OllamaEndpoint(model, "Router", "Completion", new { model = "fake-route" }), ct).ConfigureAwait(false);
                    string assistant = await AiTestKit.AssistantAsync(f, chat, ct, new { models = new { chat, toolRouting = router }, tools = new { enabled = true, allowed = new[] { "testEcho" } } }).ConfigureAwait(false);
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    JsonElement conversation = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/assistants/" + assistant + "/conversations", member.AccessToken, new { }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    model.ClearRequests();
                    List<SseEvent> events = await AiTestKit.SseAsync(f, "/api/v1/assistant-conversations/" + Api.Str(conversation, "id") + "/messages", member.AccessToken, new { content = "Route?" }, ct).ConfigureAwait(false);
                    JsonElement reply = events.Last().Data.GetProperty("reply");
                    Check.Equal(model.ReplyText, String.Concat(events.Where(e => e.Name == "reply.delta").Select(e => e.Data.GetProperty("text").GetString())), "Only the chat model's answer is streamed");
                    Check.Equal("Succeeded", Api.Str(reply.GetProperty("toolCalls").GetProperty("objects")[0], "status"), "Routed tool call");
                    List<FakeModelRequest> chats = ChatRequests(model);
                    Check.True(chats[0].Body.Contains("fake-route", StringComparison.Ordinal) && chats[0].Body.Contains("\"tools\"", StringComparison.Ordinal) && chats[0].Body.Contains("\"stream\":false", StringComparison.Ordinal), "The routing model decides, without streaming");
                    FakeModelRequest last = chats[chats.Count - 1];
                    Check.True(last.Body.Contains("fake-chat", StringComparison.Ordinal) && !last.Body.Contains("\"tools\"", StringComparison.Ordinal), "The chat model answers without tools");
                    DataTable stages = await f.Raw.ExecuteQueryAsync("SELECT stage FROM aimessagestages WHERE messageid = '" + Api.Str(reply, "id") + "' ORDER BY position;", false, ct).ConfigureAwait(false);
                    Check.True(stages.Rows.Cast<DataRow>().Any(r => RowReader.GetString(r, "stage") == "toolRouting"), "toolRouting stage");
                    Check.True(await ContentTestKit.ScalarAsync(f, "SELECT COALESCE(SUM(requests), 0) AS cnt FROM aiusagerollups WHERE purpose = 'ToolRouting';", ct).ConfigureAwait(false) >= 1, "Routing usage recorded under its purpose");

                    List<SseEvent> test = await AiTestKit.SseAsync(f, "/api/v1/admin/ai/assistants/" + assistant + "/test", HttpFixture.AdminToken, new { messages = new[] { new { role = "user", content = "Test the tool" } } }, ct).ConfigureAwait(false);
                    JsonElement debug = test.First(e => e.Name == "debug" && e.Data.TryGetProperty("toolCall", out JsonElement _)).Data.GetProperty("toolCall");
                    Check.True(Api.Str(debug, "argumentsJson").Contains("route", StringComparison.Ordinal) && Api.Str(debug, "output").Contains("Echo result", StringComparison.Ordinal), "Debug event with arguments and output");
                    Check.True(test.Any(e => e.Name == "tool.completed"), "Test console shows tool activity");
                }),

                AiTestKit.Case(Id, "Tools.GeminiAndAnthropicWire", "Gemini (generateContent and streamGenerateContent) and Anthropic (Messages, streamed and whole) endpoints call native tools: functionDeclarations and functionResponse parts, tool_use and tool_result blocks, and the answer after the tool", async (f, model, ct) =>
                {
                    RegisterEcho(f);
                    model.ToolCalls = new List<FakeToolCall> { new FakeToolCall { Name = "testEcho", ArgumentsJson = "{\"query\":\"wire\"}" } };
                    model.ToolPreamble = "Checking.";
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    object toolPolicy = new { enabled = true, allowed = new[] { "testEcho" } };
                    foreach (string apiType in new[] { "Gemini", "Anthropic" })
                    {
                        foreach (bool streaming in new[] { true, false })
                        {
                            string label = apiType + (streaming ? " streamed" : " whole");
                            string endpoint = await AiTestKit.EndpointAsync(f, new { name = label, kind = "Completion", apiType, model = "fake-chat", endpointUrl = model.BaseUrl, authType = "ApiKey", authValue = "k", parameters = new { streaming, timeoutMs = 10000 }, limits = new { maxRetries = 0 }, healthCheck = new { enabled = false } }, ct).ConfigureAwait(false);
                            string assistant = await AiTestKit.AssistantAsync(f, endpoint, ct, new { tools = toolPolicy }).ConfigureAwait(false);
                            JsonElement conversation = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/assistants/" + assistant + "/conversations", member.AccessToken, new { }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                            model.ClearRequests();
                            List<SseEvent> events = await AiTestKit.SseAsync(f, "/api/v1/assistant-conversations/" + Api.Str(conversation, "id") + "/messages", member.AccessToken, new { content = "Wire?" }, ct).ConfigureAwait(false);
                            JsonElement reply = events.Last().Data.GetProperty("reply");
                            Check.Equal("reply.completed", events.Last().Name, label + " completes (" + String.Join(",", events.Select(e => e.Name)) + ")");
                            Check.Equal("Succeeded", Api.Str(reply.GetProperty("toolCalls").GetProperty("objects")[0], "status"), label + " native tool call");
                            string text = String.Concat(events.Where(e => e.Name == "reply.delta").Select(e => e.Data.GetProperty("text").GetString()));
                            Check.Equal(text, Api.Str(reply, "content"), label + " deltas equal the content");
                            Check.True(text.EndsWith(model.ReplyText, StringComparison.Ordinal), label + " answers after the tool (" + text + ")");
                            List<FakeModelRequest> chats = model.Requests.Where(r => r.Method == "POST" && (r.Path == "/v1/messages" || r.Path.StartsWith("/v1beta/models/", StringComparison.Ordinal)) &&
                                !r.Body.Contains("Write a short title", StringComparison.Ordinal) && !r.Body.Contains("content safety classifier", StringComparison.Ordinal)).ToList();
                            Check.Equal(2, chats.Count, label + " two model rounds (" + String.Join(",", model.Requests.Select(r => r.Path)) + ")");
                            if (apiType == "Gemini")
                            {
                                string suffix = streaming ? ":streamGenerateContent" : ":generateContent";
                                Check.True(chats.All(r => r.Path == "/v1beta/models/fake-chat" + suffix), label + " route " + suffix);
                                Check.True(chats[0].Body.Contains("functionDeclarations", StringComparison.Ordinal) && chats[0].Body.Contains("testEcho", StringComparison.Ordinal), label + " offers the tool as a function declaration");
                                Check.True(chats[1].Body.Contains("functionResponse", StringComparison.Ordinal) && chats[1].Body.Contains("functionCall", StringComparison.Ordinal) && chats[1].Body.Contains("Echo result for wire", StringComparison.Ordinal), label + " replays the call and its response");
                            }
                            else
                            {
                                Check.True(chats.All(r => r.Path == "/v1/messages"), label + " route /v1/messages");
                                Check.True(chats[0].Body.Contains("input_schema", StringComparison.Ordinal) && chats[0].Body.Contains("testEcho", StringComparison.Ordinal), label + " offers the tool with input_schema");
                                Check.True(chats[1].Body.Contains("tool_use", StringComparison.Ordinal) && chats[1].Body.Contains("tool_result", StringComparison.Ordinal) && chats[1].Body.Contains("Echo result for wire", StringComparison.Ordinal), label + " replays tool_use and tool_result blocks");
                            }
                        }
                    }
                }),

                AiTestKit.Case(Id, "Tools.ThreadForumsAndProfile", "getThread, listForums, and getMyProfile run as the caller: a member reads the thread with its top comments and sees the forums they may read and only their own profile; a visitor gets no members-only thread and is never offered getMyProfile", async (f, model, ct) =>
                {
                    ForumTestKit.Relax(f);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/access", HttpFixture.AdminToken, new { model = "PartiallyGated", anonymousBrowsing = true, gatedFeatures = Array.Empty<string>() }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
                    string marker = "thrtool" + new string(Guid.NewGuid().ToString("N").Where(Char.IsLetter).Take(6).ToArray());
                    TestMember author = await Api.RegisterAsync(f, ct, null, "Writer " + marker).ConfigureAwait(false);
                    await Api.GrantCompAsync(f, author.Id, ct).ConfigureAwait(false);
                    string forumId = Api.Str(await ForumTestKit.CreateForumAsync(f, admin.AccessToken, "Lounge " + marker, ct, new { audience = "Members" }).ConfigureAwait(false), "id");
                    string threadId = Api.Str(await ForumTestKit.ThreadAsync(f, author.AccessToken, forumId, new { type = "Discussion", title = "Thread " + marker, body = "Body of " + marker, audience = "Members" }, ct).ConfigureAwait(false), "id");
                    await ForumTestKit.CommentIdAsync(f, author.AccessToken, threadId, "Comment about " + marker, ct).ConfigureAwait(false);
                    string assistant = await AiTestKit.ReadyAsync(f, model, ct, new { audience = "Public", tools = new { enabled = true, allowed = new[] { "getThread", "listForums", "getMyProfile" } } }).ConfigureAwait(false);
                    JsonElement registry = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/tools/enumerate", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    List<string> names = registry.GetProperty("objects").EnumerateArray().Select(t => Api.Str(t, "name")).ToList();
                    foreach (string expected in new[] { "getThread", "listForums", "getMyProfile" }) Check.True(names.Contains(expected), "Registry lists " + expected);

                    TestMember member = await Api.RegisterAsync(f, ct, null, "Asker " + marker).ConfigureAwait(false);
                    await Api.GrantCompAsync(f, member.Id, ct).ConfigureAwait(false);
                    model.ToolCalls = new List<FakeToolCall> { new FakeToolCall { Name = "getThread", ArgumentsJson = "{\"threadId\":\"/threads/" + threadId + "\"}" } };
                    model.ClearRequests();
                    JsonElement read = (await AiTestKit.StartAsync(f, assistant, member.AccessToken, "What does the thread say?", ct).ConfigureAwait(false)).GetProperty("reply");
                    JsonElement readCall = read.GetProperty("toolCalls").GetProperty("objects")[0];
                    Check.Equal("Succeeded", Api.Str(readCall, "status"), "getThread succeeds for a member (" + readCall + ")");
                    Check.Equal(threadId, Api.Str(readCall.GetProperty("results")[0], "targetId"), "Thread link");
                    List<FakeModelRequest> chats = ChatRequests(model);
                    Check.True(chats[1].Body.Contains("Body of " + marker, StringComparison.Ordinal) && chats[1].Body.Contains("Comment about " + marker, StringComparison.Ordinal), "Body and top comment reach the model");

                    model.ToolCalls = new List<FakeToolCall> { new FakeToolCall { Name = "listForums", ArgumentsJson = "{\"search\":\"" + marker + "\"}" } };
                    JsonElement listed = (await AiTestKit.StartAsync(f, assistant, member.AccessToken, "Which forums are there?", ct).ConfigureAwait(false)).GetProperty("reply");
                    JsonElement listCall = listed.GetProperty("toolCalls").GetProperty("objects")[0];
                    Check.Equal("Succeeded", Api.Str(listCall, "status"), "listForums succeeds");
                    Check.True(listCall.GetProperty("results").EnumerateArray().Any(r => Api.Str(r, "targetId") == forumId), "The members forum is listed for a member (" + listCall + ")");

                    model.ToolCalls = new List<FakeToolCall> { new FakeToolCall { Name = "getMyProfile", ArgumentsJson = "{}" } };
                    model.ClearRequests();
                    JsonElement profile = (await AiTestKit.StartAsync(f, assistant, member.AccessToken, "What is on my profile?", ct).ConfigureAwait(false)).GetProperty("reply");
                    Check.Equal("Succeeded", Api.Str(profile.GetProperty("toolCalls").GetProperty("objects")[0], "status"), "getMyProfile succeeds for a member");
                    string profileRound = ChatRequests(model)[1].Body;
                    Check.True(profileRound.Contains("Display name: Asker " + marker, StringComparison.Ordinal) && profileRound.Contains("Membership: active", StringComparison.Ordinal), "The caller's own profile and membership");
                    Check.True(!profileRound.Contains("Writer " + marker, StringComparison.Ordinal) && !profileRound.Contains(member.Email, StringComparison.Ordinal), "No other member and no email address");

                    JsonElement session = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/assistant-visitors", null, null, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Dictionary<string, string> headers = new Dictionary<string, string> { ["X-TK-Visitor-Token"] = Api.Str(session, "visitorToken") };
                    model.ToolCalls = new List<FakeToolCall> { new FakeToolCall { Name = "getThread", ArgumentsJson = "{\"threadId\":\"" + threadId + "\"}" } };
                    model.ClearRequests();
                    ApiResult visitor = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/assistants/" + assistant + "/conversations", null, new { message = "Read it" }, ct, "widget", headers).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Created, visitor.Status, "Visitor chat (" + visitor.Text + ")");
                    JsonElement visitorCall = visitor.Body.GetProperty("reply").GetProperty("toolCalls").GetProperty("objects")[0];
                    Check.True(visitorCall.GetProperty("resultCount").GetInt32() == 0 && visitorCall.GetProperty("results").GetArrayLength() == 0, "A visitor gets no members-only thread (" + visitorCall + ")");
                    List<FakeModelRequest> visitorChats = ChatRequests(model);
                    Check.True(!visitorChats.Any(r => r.Body.Contains("Body of " + marker, StringComparison.Ordinal) || r.Body.Contains("Comment about " + marker, StringComparison.Ordinal)), "The members thread never reaches the model for a visitor");
                    Check.True(!visitorChats[0].Body.Contains("getMyProfile", StringComparison.Ordinal), "getMyProfile is not offered to visitors");
                }, null, p => p.Features.Forums = true),

                AiTestKit.Case(Id, "Tools.SnapshotAndExport", "A reported reply's moderation snapshot keeps its tool calls (arguments, summary, links) after the member deletes the conversation and prunes them with the snapshot; the account export lists each reply's tool calls without the tool output", async (f, model, ct) =>
                {
                    RegisterEcho(f);
                    model.ToolCalls = new List<FakeToolCall> { new FakeToolCall { Name = "testEcho", ArgumentsJson = "{\"query\":\"keep\"}" } };
                    string assistant = await AiTestKit.ReadyAsync(f, model, ct, new { tools = new { enabled = true, allowed = new[] { "testEcho" } } }).ConfigureAwait(false);
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    JsonElement exchange = await AiTestKit.StartAsync(f, assistant, member.AccessToken, "Echo something", ct).ConfigureAwait(false);
                    string replyId = Api.Str(exchange.GetProperty("reply"), "id");
                    string conversationId = Api.Str(exchange.GetProperty("userMessage"), "conversationId");

                    JsonElement file = await AccountExportSuite.ExportAsync(f, member, ct).ConfigureAwait(false);
                    JsonElement exported = file.GetProperty("assistantConversations").EnumerateArray().First(c => Api.Str(c, "id") == conversationId);
                    JsonElement exportedReply = exported.GetProperty("messages").EnumerateArray().First(m => Api.Str(m, "id") == replyId);
                    JsonElement exportedCall = exportedReply.GetProperty("toolCalls")[0];
                    Check.Equal("testEcho", Api.Str(exportedCall, "toolName"), "Export: tool name");
                    Check.True(Api.Str(exportedCall, "argumentsJson").Contains("keep", StringComparison.Ordinal) && Api.Str(exportedCall, "status") == "Succeeded", "Export: arguments and status");
                    Check.Equal("fpo_echo", Api.Str(exportedCall.GetProperty("results")[0], "targetId"), "Export: result links");
                    Check.True(!exportedCall.TryGetProperty("resultSummary", out JsonElement _), "Export: no tool output");
                    JsonElement exportedQuestion = exported.GetProperty("messages")[0];
                    Check.Equal(0, exportedQuestion.GetProperty("toolCalls").GetArrayLength(), "Export: member messages have no tool calls");

                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/reports", member.AccessToken, new { targetType = "AssistantMessage", targetId = replyId, reason = "Harassment" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/assistant-conversations/" + conversationId, member.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                    Check.Equal(0L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM aitoolcalls WHERE messageid = '" + replyId + "';", ct).ConfigureAwait(false), "Live tool calls deleted with the conversation");
                    JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/moderation/queue/AssistantMessage/" + replyId, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement snapshotCalls = detail.GetProperty("target").GetProperty("toolCalls");
                    Check.Equal(1L, snapshotCalls.GetProperty("totalRecords").GetInt64(), "Snapshot keeps the tool call");
                    JsonElement snapshotCall = snapshotCalls.GetProperty("objects")[0];
                    Check.Equal("testEcho", Api.Str(snapshotCall, "toolName"), "Snapshot tool name");
                    Check.True(Api.Str(snapshotCall, "argumentsJson").Contains("keep", StringComparison.Ordinal) && Api.Str(snapshotCall, "resultSummary").Contains("Echo result for keep", StringComparison.Ordinal), "Snapshot arguments and summary for moderators");
                    Check.Equal("/feed/fpo_echo", Api.Str(snapshotCall.GetProperty("results")[0], "path"), "Snapshot result link");
                    Check.Equal("Echoed", Api.Str(snapshotCall, "completedLabel"), "Snapshot label from the registry");
                    await f.Raw.ExecuteQueryAsync("UPDATE aimoderationsnapshots SET expiresutc = now() - interval '1 day' WHERE messageid = '" + replyId + "';", false, ct).ConfigureAwait(false);
                    Check.True(await f.Services.Ai.Reports.PruneAsync(DateTime.UtcNow, ct).ConfigureAwait(false) >= 1, "Expired snapshot pruned");
                    Check.Equal(0L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM aimoderationsnapshottoolcalls WHERE propertyid = '" + f.Services.PropertyId + "';", ct).ConfigureAwait(false), "Snapshot tool calls pruned");
                }),

                PropertyIsolation()
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor PropertyIsolation()
        {
            return new TestCaseDescriptor(Id, "Tools.PropertyIsolation", "A tool runs against the chatting property only: property A's searchFeed never returns property B's posts, and tool calls are stored under A", async ct =>
            {
                HttpFixture f = await HttpFixture.StartAsync(ct, p => p.Features.Ai = true, s =>
                {
                    MultiPropertyKit.Multi(s);
                    s.Secrets.EncryptionKey = TrellisKit.Core.Security.SecretBox.GenerateKey();
                }).ConfigureAwait(false);
                FakeModelServer model = FakeModelServer.Start();
                await using (f.ConfigureAwait(false))
                await using (model.ConfigureAwait(false))
                {
                    string platform = await MultiPropertyKit.PlatformLoginAsync(f, ct).ConfigureAwait(false);
                    JsonObject document = MultiPropertyKit.SecondDocument();
                    ((JsonObject)document["features"]!)["ai"] = true;
                    JsonElement created = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/properties", platform, new { document }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    string b = Api.Str(created, "id");
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/properties/" + b + "/enable", platform, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    string marker = "isotool" + new string(Guid.NewGuid().ToString("N").Where(Char.IsLetter).Take(6).ToArray());
                    f.PropertyId = b;
                    TestMember bAdmin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
                    string bPost = Api.Str(await ContentTestKit.PostAsync(f, bAdmin.AccessToken, new { title = "B " + marker, body = "B " + marker + " post", audience = "Public" }, ct).ConfigureAwait(false), "id");
                    f.PropertyId = TestProperty.SampleId;
                    TestMember aAdmin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
                    string aPost = Api.Str(await ContentTestKit.PostAsync(f, aAdmin.AccessToken, new { title = "A " + marker, body = "A " + marker + " post", audience = "Public" }, ct).ConfigureAwait(false), "id");
                    model.ToolCalls = new List<FakeToolCall> { new FakeToolCall { Name = "searchFeed", ArgumentsJson = "{\"query\":\"" + marker + "\"}" } };
                    string assistant = await AiTestKit.ReadyAsync(f, model, ct, new { tools = new { enabled = true, allowed = new[] { "searchFeed" } } }).ConfigureAwait(false);
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    model.ClearRequests();
                    JsonElement exchange = await AiTestKit.StartAsync(f, assistant, member.AccessToken, "Find " + marker, ct).ConfigureAwait(false);
                    List<string> seen = Results(exchange);
                    Check.True(seen.Contains(aPost) && !seen.Contains(bPost), "Only property A's post (" + String.Join(",", seen) + ")");
                    Check.True(!ChatRequests(model).Any(r => r.Body.Contains(bPost, StringComparison.Ordinal)), "B's post never reaches the model");
                    string replyId = Api.Str(exchange.GetProperty("reply"), "id");
                    Check.Equal(0L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM aitoolcalls WHERE messageid = '" + replyId + "' AND propertyid <> '" + TestProperty.SampleId + "';", ct).ConfigureAwait(false), "Stored under A");
                    AssistantToolRegistry aTools = f.Services.Ai.Tools;
                    f.PropertyId = b;
                    Check.True(!ReferenceEquals(aTools, f.Services.Ai.Tools), "Each property has its own registry");
                    f.PropertyId = TestProperty.SampleId;
                }
            }, new[] { "http", "integration", "ai", "isolation", TestEnvironment.Provider.ToString().ToLowerInvariant() });
        }

        private static void RegisterEcho(HttpFixture f)
        {
            f.Services.Ai.Tools.Register(new AssistantTool
            {
                Name = "testEcho",
                Description = "Echo a query.",
                Label = "Echoing",
                CompletedLabel = "Echoed",
                ParametersSchema = "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"}},\"required\":[\"query\"]}",
                Handler = (caller, args, token) =>
                {
                    string query = args.TryGetProperty("query", out JsonElement q) ? q.GetString() ?? String.Empty : String.Empty;
                    return Task.FromResult(new AssistantToolResult
                    {
                        Text = "Echo result for " + query,
                        ResultCount = 1,
                        Results = new List<AssistantToolCallResult> { new AssistantToolCallResult { TargetType = "FeedPost", TargetId = "fpo_echo", Title = "Echo post", Path = "/feed/fpo_echo" } }
                    });
                }
            });
        }

        private static List<FakeModelRequest> ChatRequests(FakeModelServer model)
        {
            return model.Requests.Where(r => (r.Path == "/api/chat" || r.Path.EndsWith("/chat/completions", StringComparison.Ordinal)) &&
                !r.Body.Contains("Write a short title", StringComparison.Ordinal) && !r.Body.Contains("content safety classifier", StringComparison.Ordinal)).ToList();
        }

        private static List<string> Results(JsonElement exchange)
        {
            JsonElement calls = exchange.GetProperty("reply").GetProperty("toolCalls").GetProperty("objects");
            return calls.EnumerateArray().SelectMany(c => c.GetProperty("results").EnumerateArray().Select(r => Api.Str(r, "targetId"))).ToList();
        }

        #endregion
    }
}
