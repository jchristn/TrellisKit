namespace Test.Shared.Suites
{
    using PolyPrompt.Models;
    using System;
    using System.Collections;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Data;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared.Fakes;
    using Touchstone.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Security;
    using TrellisKit.Server.Services.Ai;
    using TrellisKit.Server.Services.Jobs;

    /// <summary>
    /// CLI agent harness executors and the job-scoped MCP tool server (docs/HARNESSES.md; docs/INTEGRATION.md section
    /// 30.2): a harness runs only inside a job item on an instance with jobs.runHarnesses (elsewhere the role falls
    /// through to its API executor with the reason in the ledger); a fake Claude Code CLI reaches the job tools over MCP,
    /// with the scope guard leading its instructions, a scrubbed environment, and a throwaway directory that is removed;
    /// ledger rows carry the harness runtime, model, tokens, cost, and telemetry; tool arguments are validated; one
    /// item's token cannot reach another item's tools; and the administrator's harness profile (model, permission mode,
    /// limits, base URL, and an encrypted credential) reaches the CLI process as the right environment variables.
    /// </summary>
    public static class HarnessSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Harness";

        #endregion

        #region Private-Members

        private const string Roles = "/api/v1/admin/ai/roles";
        private const string ToolName = "submit_candidate";
        private const string ToolSchema = "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":200},\"city\":{\"type\":\"string\",\"maxLength\":100}},\"required\":[\"name\",\"city\"],\"additionalProperties\":false}";
        private const string ValidArguments = "{\"name\":\"Example Community Center\",\"city\":\"Reno\"}";
        private const string InvalidArguments = "{\"city\":\"Reno\"}";
        private const string SecretVariable = "TK_HARNESS_SUITE_SECRET";
        private const string Profiles = "/api/v1/admin/ai/harnesses";
        private const string StoredToken = "oauth-test-token-not-real";
        private const string Gateway = "https://gateway.example.test/anthropic";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "CLI agent harnesses and the job tool server (" + TestEnvironment.Provider + ")", new[]
            {
                AiTestKit.Case(Id, "Gate.FallsThroughOutsideJobs", "Outside a job item, and inside one with jobs.runHarnesses off, the harness never starts: the role falls through to its API executor, which runs the same job tool through the engine's tool loop, and the ledger records why the harness was passed over", GateAsync, s => s.Jobs.RunHarnesses = true),
                AiTestKit.Case(Id, "Harness.RunsInJob", "Inside a job item on a worker, the Claude Code harness reaches the job tool over MCP: the valid call is stored, the invalid one is rejected, the scope guard leads its instructions, the environment holds no server settings, the throwaway directory is removed, and the ledger row carries the harness runtime, model, tokens, and cost that reach the item", RunsInJobAsync, s => s.Jobs.RunHarnesses = true),
                SuiteCases.Unit(Id, "Mcp.ValidationAndIsolation", "The job tool server answers only its own bearer token, lists only its item's tools, validates arguments (invalid calls are tool errors and never reach the handler), refuses unknown tools, and stops with its call", McpAsync),
                SuiteCases.Unit(Id, "Launch.Isolation", "Launch plans: the environment keeps only the allowlist and the runtime's own credentials, HOME and TMPDIR point into the sandbox, each runtime gets only the job tool server, OpenCode and Pi cannot take job tools, and the tool set refuses bad calls and calls past its limit", LaunchAsync),
                AiTestKit.Case(Id, "Profile.AdminAndSecrets", "Harness profiles list every runtime, validate per runtime (Restricted or ReadOnly only, credential types, base URL, limits), store the credential encrypted, never return it, reveal it only to a signed-in administrator with a reason, and are covered by key rotation", ProfileAdminAsync, s => s.Jobs.RunHarnesses = true),
                AiTestKit.Case(Id, "Profile.AppliedToLaunch", "A saved profile reaches the CLI process: the stored OAuth token replaces the worker's inherited key, the base URL is set, the profile's model, permission mode, tool call limit, and web research switch apply, the credential never appears in the ledger, and the Environment type goes back to the worker's own key", ProfileLaunchAsync, s => s.Jobs.RunHarnesses = true),
                AiTestKit.Case(Id, "Harness.Refusals", "The harness service refuses calls outside a job item, with harnesses off, without the scope guard, and in permissive modes, and RunAgentAsync refuses invalid requests", RefusalsAsync, s => s.Jobs.RunHarnesses = true)
            });
        }

        #endregion

        #region Private-Methods

        private static AgentTool Tool(ConcurrentQueue<string> stored)
        {
            return new AgentTool
            {
                Name = ToolName,
                Description = "Submit one community center or venue found for this map cell.",
                ParametersSchema = ToolSchema,
                Handler = (call, ct) =>
                {
                    string name = call.Arguments.GetProperty("name").GetString() ?? String.Empty;
                    if (name.Contains("forbidden", StringComparison.OrdinalIgnoreCase)) return Task.FromResult(AgentToolResult.Reject("That name is not allowed."));
                    stored.Enqueue(name + "|" + (call.JobItemId ?? "none") + "|" + call.Runtime);
                    return Task.FromResult(AgentToolResult.Accept("Recorded " + name + ".", "{\"name\":" + JsonSerializer.Serialize(name) + "}"));
                }
            };
        }

        private static AgentRequest Request(ConcurrentQueue<string> stored)
        {
            return new AgentRequest
            {
                Instructions = "Find community centers in the map cell around Reno, Nevada, and submit each one.",
                Tools = new List<AgentTool> { Tool(stored) },
                TimeoutSeconds = 60
            };
        }

        private static async Task<string> HierarchyAsync(HttpFixture f, FakeModelServer model, CancellationToken ct, string? harnessModel = "sonnet")
        {
            string chat = await AiTestKit.EndpointAsync(f, AiTestKit.OllamaEndpoint(model, "Agent " + Guid.NewGuid().ToString("N").Substring(0, 6)), ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, Roles + "/Research", HttpFixture.AdminToken, new
            {
                executors = new object[] { new { runtime = "ClaudeCode", model = harnessModel }, new { runtime = "ApiEndpoint", endpointId = chat } }
            }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            f.Services.Ai.Roles.Invalidate();
            return chat;
        }

        private static async Task<AgentOutcome> InJobAsync(HttpFixture f, bool requiresHarness, Func<CancellationToken, Task<AgentOutcome>> body, CancellationToken ct)
        {
            AgentOutcome? seen = null;
            TestJobHandler handler = new TestJobHandler(null, new JobHandlerDefaults { Name = "Harness test", Description = "Harness suite handler.", MaxConcurrency = 1, MaxAttempts = 1, RequiresHarness = requiresHarness })
            {
                Targets = new List<string> { "cell-1" }
            };
            handler.Behavior = c =>
            {
                seen = body(CancellationToken.None).GetAwaiter().GetResult();
                return seen.Success ? JobItemOutcome.Ok("{\"accepted\":" + seen.Accepted.Count + "}") : JobItemOutcome.Fail(seen.ErrorCode ?? "failed", false);
            };
            f.Services.Jobs.Registry.Register(handler);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/jobs/" + handler.Key + "/run", HttpFixture.AdminToken, new { }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            await f.Services.Jobs.ProcessAsync(ct).ConfigureAwait(false);
            Check.True(seen != null, "The job item ran");
            return seen!;
        }

        private static async Task<List<JsonElement>> LedgerAsync(HttpFixture f, CancellationToken ct)
        {
            JsonElement calls = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/calls/enumerate", HttpFixture.AdminToken, new { filters = new { role = new[] { "Research" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            return calls.GetProperty("objects").EnumerateArray().ToList();
        }

        private static async Task GateAsync(HttpFixture f, FakeModelServer model, CancellationToken ct)
        {
            using (FakeHarnessCli cli = new FakeHarnessCli(ToolName, ValidArguments, InvalidArguments))
            {
                f.Services.Ai.Harnesses.Executables[AiExecutorRuntimeEnum.ClaudeCode] = cli.ScriptPath;
                string chat = await HierarchyAsync(f, model, ct).ConfigureAwait(false);
                model.ToolCalls = new List<FakeToolCall> { new FakeToolCall { Name = ToolName, ArgumentsJson = ValidArguments } };
                model.ToolRounds = 1;
                model.ReplyText = "Submitted one community center.";

                // A request path: no job scope, so the harness is passed over even with jobs.runHarnesses on.
                ConcurrentQueue<string> stored = new ConcurrentQueue<string>();
                AgentOutcome direct = await f.Services.Ai.Roles.RunAgentAsync("Research", Request(stored), ct).ConfigureAwait(false);
                Check.True(direct.Success, "The API executor answered (" + direct.ErrorCode + ")");
                Check.Equal("ApiEndpoint", direct.Runtime ?? String.Empty, "API runtime");
                Check.Equal(1, direct.ExecutorPosition ?? -1, "Second executor");
                Check.Equal(chat, direct.EndpointId ?? String.Empty, "Endpoint");
                Check.True(direct.Escalations.Single().Contains("request paths", StringComparison.Ordinal), "The harness was passed over outside a job: " + direct.Escalations.Single());
                Check.True(!cli.Launched, "The harness never started in a request path");
                Check.Equal(1, direct.Accepted.Count, "The API tool loop ran the job tool");
                Check.True(stored.Single().StartsWith("Example Community Center|none|ApiEndpoint", StringComparison.Ordinal), "The same handler stored the result: " + stored.Single());
                Check.True(model.Requests.Any(r => r.Body.Contains(ToolName, StringComparison.Ordinal)), "The job tool was offered to the endpoint");
                List<JsonElement> rows = await LedgerAsync(f, ct).ConfigureAwait(false);
                Check.True(rows.Count >= 2, "A tool round and an answer round are in the ledger (" + rows.Count + ")");
                Check.True(rows.All(r => Api.Str(r, "runtime") == "ApiEndpoint"), "Only API calls ran");
                Check.True(rows.All(r => Api.Str(r, "escalationReason").Contains("#0 ClaudeCode", StringComparison.Ordinal) && Api.Str(r, "escalationReason").Contains("request paths", StringComparison.Ordinal)), "The ledger records why the harness was passed over");

                // Admin role tests are request paths too.
                JsonElement test = await Api.ExpectAsync(f, HttpMethod.Post, Roles + "/Research/test", HttpFixture.AdminToken, new { prompt = "Name one community center in Reno." }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.Equal(1, test.GetProperty("executorPosition").GetInt32(), "The role test fell through to the API executor");
                Check.True(!cli.Launched, "The role test never started the harness");

                // Inside a job item, but on an instance with jobs.runHarnesses off.
                f.Services.Settings.Jobs.RunHarnesses = false;
                ConcurrentQueue<string> inJob = new ConcurrentQueue<string>();
                AgentOutcome off = await InJobAsync(f, false, t => f.Services.Ai.Roles.RunAgentAsync("Research", Request(inJob), t), ct).ConfigureAwait(false);
                f.Services.Settings.Jobs.RunHarnesses = true;
                Check.True(off.Success, "The API executor answered in the job");
                Check.Equal("ApiEndpoint", off.Runtime ?? String.Empty, "API runtime in the job");
                Check.True(off.Escalations.Single().Contains("worker instances", StringComparison.Ordinal), "Passed over because harnesses are off: " + off.Escalations.Single());
                Check.True(!cli.Launched, "The harness never started with jobs.runHarnesses off");
                Check.True(inJob.Single().Contains("|jbi_", StringComparison.Ordinal), "The tool call knew its job item");
            }
        }

        private static async Task RunsInJobAsync(HttpFixture f, FakeModelServer model, CancellationToken ct)
        {
            if (OperatingSystem.IsWindows()) return;
            Environment.SetEnvironmentVariable(SecretVariable, "do-not-leak");
            try
            {
                using (FakeHarnessCli cli = new FakeHarnessCli(ToolName, ValidArguments, InvalidArguments))
                {
                    f.Services.Ai.Harnesses.Executables[AiExecutorRuntimeEnum.ClaudeCode] = cli.ScriptPath;
                    await HierarchyAsync(f, model, ct).ConfigureAwait(false);
                    model.ClearRequests();
                    ConcurrentQueue<string> stored = new ConcurrentQueue<string>();
                    AgentOutcome outcome = await InJobAsync(f, true, t => f.Services.Ai.Roles.RunAgentAsync("Research", Request(stored), t), ct).ConfigureAwait(false);

                    Check.True(cli.Launched, "The harness started inside the job item");
                    Check.True(outcome.Success, "The harness finished (" + outcome.ErrorCode + "; " + String.Join(" | ", outcome.Escalations) + ")");
                    Check.Equal("ClaudeCode", outcome.Runtime ?? String.Empty, "Harness runtime");
                    Check.Equal(0, outcome.ExecutorPosition ?? -1, "First executor");
                    Check.Equal(FakeHarnessCli.Model, outcome.Model ?? String.Empty, "Reported model");
                    Check.Equal("Reported one candidate.", outcome.Text, "Final text");
                    Check.Equal(0, model.Requests.Count, "No API call was needed");

                    // Tools over MCP: the valid call was stored; the invalid one was a tool error that never reached the handler.
                    Check.True(cli.Read("list").Contains(ToolName, StringComparison.Ordinal), "tools/list shows the job tool");
                    Check.True(cli.Read("call1").Contains("Recorded Example Community Center", StringComparison.Ordinal), "The valid call was accepted: " + cli.Read("call1"));
                    Check.True(cli.Read("call2").Contains("\"isError\":true", StringComparison.Ordinal), "The invalid call is a tool error: " + cli.Read("call2"));
                    Check.Equal(1, stored.Count, "Only the valid call reached the handler");
                    Check.True(stored.Single().StartsWith("Example Community Center|jbi_", StringComparison.Ordinal) && stored.Single().EndsWith("|ClaudeCode", StringComparison.Ordinal), "The call knew its item and runtime: " + stored.Single());
                    Check.Equal(1, outcome.Accepted.Count, "One accepted call in the outcome");
                    Check.Equal("{\"name\":\"Example Community Center\"}", outcome.Accepted[0].DataJson ?? String.Empty, "Stored data in the outcome");
                    Check.True(cli.Read("url").Trim().StartsWith("http://127.0.0.1:", StringComparison.Ordinal), "The MCP server is on loopback");

                    // The scope guard leads the instructions; Restricted mode; only the job tool server.
                    List<string> args = cli.ArgumentLines();
                    int system = args.IndexOf("--append-system-prompt");
                    Check.True(system >= 0 && args[system + 1].StartsWith(ScopeGuard.Floor.Substring(0, 60), StringComparison.Ordinal), "The scope guard leads the system instructions");
                    Check.True(cli.Read("args").Contains(ScopeGuard.Floor, StringComparison.Ordinal), "The whole floor is present");
                    Check.True(args.Contains("dontAsk") && args.Contains("--strict-mcp-config") && args.Contains("--mcp-config"), "Restricted permission mode with a strict MCP configuration");
                    Check.True(args.Contains("mcp__trelliskit"), "Only the job tool server is pre-approved");
                    Check.True(args.Contains("--model") && args[args.IndexOf("--model") + 1] == "sonnet", "The executor's model");
                    Check.True(cli.Read("stdin").Contains("Find community centers in the map cell", StringComparison.Ordinal), "The task travels on standard input");

                    // No server settings or secrets in the environment; a throwaway home; the directory is gone.
                    string env = cli.Read("env");
                    Check.True(!env.Contains(SecretVariable, StringComparison.Ordinal), "Server variables are scrubbed");
                    Check.True(!env.Contains("TK_TEST_PG", StringComparison.Ordinal), "Test database settings are scrubbed");
                    Check.True(env.Contains("PATH=", StringComparison.Ordinal), "PATH is kept");
                    string cwd = cli.Read("cwd").Trim();
                    Check.True(cwd.Length > 0 && env.Contains("HOME=", StringComparison.Ordinal), "Working directory and HOME recorded");
                    Check.True(!Directory.Exists(cwd), "The throwaway working directory was removed: " + cwd);
                    string home = env.Split('\n').First(l => l.StartsWith("HOME=", StringComparison.Ordinal)).Substring(5);
                    Check.True(!Directory.Exists(home), "The throwaway home was removed");

                    // Ledger: the harness row carries runtime, model, tokens, and cost, and the cost reached the item and run.
                    List<JsonElement> rows = await LedgerAsync(f, ct).ConfigureAwait(false);
                    JsonElement row = rows.Single(r => Api.Str(r, "runtime") == "ClaudeCode");
                    Check.Equal("Ok", Api.Str(row, "outcome"), "Ledger outcome");
                    Check.Equal(FakeHarnessCli.Model, Api.Str(row, "model"), "Ledger model");
                    Check.Equal(FakeHarnessCli.PromptTokens, row.GetProperty("promptTokens").GetInt32(), "Ledger prompt tokens");
                    Check.Equal(FakeHarnessCli.CompletionTokens, row.GetProperty("completionTokens").GetInt32(), "Ledger completion tokens");
                    Check.True(Math.Abs(row.GetProperty("costUsd").GetDouble() - (double)FakeHarnessCli.CostUsd) < 1e-9, "Ledger cost from the Completed event");
                    Check.Equal("Task", Api.Str(row, "purpose"), "Task purpose");
                    Check.Equal(0, row.GetProperty("executorPosition").GetInt32(), "Ledger position");
                    Check.Equal(FakeHarnessCli.CacheReadTokens, row.GetProperty("cachedReadTokens").GetInt32(), "Ledger cache read tokens from the CLI's usage");
                    Check.Equal(FakeHarnessCli.CacheCreationTokens, row.GetProperty("cacheCreationTokens").GetInt32(), "Ledger cache creation tokens from the CLI's usage");
                    Check.Equal(1, row.GetProperty("toolCalls").GetInt32(), "Ledger tool calls (the one the CLI reported and the engine ran; the invalid call stops at the MCP server's schema check)");
                    Check.True(row.GetProperty("toolExecMs").ValueKind == JsonValueKind.Number, "Ledger tool execution time");
                    Check.Equal(0, row.GetProperty("exitCode").GetInt32(), "Ledger exit code");
                    Check.Equal(FakeHarnessCli.SessionId, Api.Str(row, "responseId"), "Ledger session id");
                    Check.Equal(FakeHarnessCli.Model, Api.Str(row, "providerModel"), "Ledger provider model");
                    Check.True(row.GetProperty("streamed").GetBoolean() && row.GetProperty("chunks").GetInt32() == 2, "The CLI's two text events");
                    long ttft = row.GetProperty("timeToFirstTokenMs").GetInt64();
                    long ttlt = row.GetProperty("timeToLastTokenMs").GetInt64();
                    Check.True(ttft >= 0 && ttlt >= ttft && row.GetProperty("streamingMs").GetInt64() == ttlt - ttft, "Time to first and last text event (" + ttft + ", " + ttlt + ")");
                    Check.True(row.GetProperty("latencyMs").GetInt64() >= ttlt, "Overall latency covers the run");
                    Check.True(row.GetProperty("statusCode").ValueKind == JsonValueKind.Null && row.GetProperty("queueMs").ValueKind == JsonValueKind.Null, "A harness call has no HTTP status and no endpoint queue");
                    Check.True(Api.Str(row, "jobItemId").StartsWith("jbi_", StringComparison.Ordinal), "Ledger job item");
                    string runId = Api.Str(row, "jobRunId");
                    JobRun run = (await f.Services.Database.Jobs.ReadRunAsync(runId, ct).ConfigureAwait(false))!;
                    Check.Equal(42500L, run.CostMicros, "The harness cost reached the run");
                    long itemCost = await ContentTestKit.ScalarAsync(f, "SELECT costmicros AS cnt FROM jobitems WHERE runid = '" + runId + "';", ct).ConfigureAwait(false);
                    Check.Equal(42500L, itemCost, "The harness cost reached the item");
                    long rollup = await ContentTestKit.ScalarAsync(f, "SELECT COALESCE(SUM(costmicros), 0) AS cnt FROM aiusagerollups WHERE endpointid = 'harness:ClaudeCode';", ct).ConfigureAwait(false);
                    Check.Equal(42500L, rollup, "The harness cost counts toward the AI budget rollups");
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable(SecretVariable, null);
            }
        }

        private static async Task McpAsync(CancellationToken ct)
        {
            ConcurrentQueue<string> storedA = new ConcurrentQueue<string>();
            ConcurrentQueue<string> storedB = new ConcurrentQueue<string>();
            AgentToolSet toolsA;
            AgentToolSet toolsB;
            using (AiCallScope.BeginJob("test.harness", "jbr_a", "jbi_a")) toolsA = new AgentToolSet(new[] { Tool(storedA) }, 10);
            AgentTool other = Tool(storedB);
            other.Name = "report_source";
            using (AiCallScope.BeginJob("test.harness", "jbr_b", "jbi_b")) toolsB = new AgentToolSet(new[] { other }, 10);
            JobToolServer serverA = await JobToolServer.StartAsync(toolsA, null, null, ct).ConfigureAwait(false);
            JobToolServer serverB = await JobToolServer.StartAsync(toolsB, null, null, ct).ConfigureAwait(false);
            int portA = serverA.Port;
            try
            {
                Check.True(serverA.Url.StartsWith("http://127.0.0.1:", StringComparison.Ordinal), "Loopback URL");
                Check.True(serverA.Token != serverB.Token && serverA.Token.Length >= 40, "Distinct random tokens");
                using (HttpClient http = new HttpClient())
                {
                    using (HttpResponseMessage none = await PostAsync(http, serverA.Url, null, null, Initialize(), ct).ConfigureAwait(false))
                        Check.Equal(HttpStatusCode.Unauthorized, none.StatusCode, "No token is refused");
                    using (HttpResponseMessage cross = await PostAsync(http, serverB.Url, serverA.Token, null, Initialize(), ct).ConfigureAwait(false))
                        Check.Equal(HttpStatusCode.Unauthorized, cross.StatusCode, "Item A's token is refused by item B's server");

                    string session;
                    using (HttpResponseMessage init = await PostAsync(http, serverA.Url, serverA.Token, null, Initialize(), ct).ConfigureAwait(false))
                    {
                        Check.Equal(HttpStatusCode.OK, init.StatusCode, "Initialize with the item's token");
                        session = init.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? values) ? values.First() : String.Empty;
                        string body = await init.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        Check.True(body.Contains(JobToolServer.ServerName, StringComparison.Ordinal), "Server name in serverInfo");
                    }
                    Check.True(session.Length > 0, "Session issued");
                    using (HttpResponseMessage note = await PostAsync(http, serverA.Url, serverA.Token, session, "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}", ct).ConfigureAwait(false))
                        Check.True((int)note.StatusCode < 300, "Initialized notification accepted");

                    string list = await BodyAsync(http, serverA, session, "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}", ct).ConfigureAwait(false);
                    Check.True(list.Contains(ToolName, StringComparison.Ordinal), "Item A's tool is listed");
                    Check.True(!list.Contains("report_source", StringComparison.Ordinal), "Item B's tool is not listed");
                    Check.True(!list.Contains("\"echo\"", StringComparison.Ordinal), "No diagnostic tools");

                    string ok = await BodyAsync(http, serverA, session, Call(3, ToolName, ValidArguments), ct).ConfigureAwait(false);
                    Check.True(ok.Contains("Recorded Example Community Center", StringComparison.Ordinal) && !ok.Contains("\"isError\":true", StringComparison.Ordinal), "A valid call is accepted: " + ok);
                    string missing = await BodyAsync(http, serverA, session, Call(4, ToolName, InvalidArguments), ct).ConfigureAwait(false);
                    Check.True(missing.Contains("\"isError\":true", StringComparison.Ordinal), "A call missing a required argument is a tool error: " + missing);
                    string extra = await BodyAsync(http, serverA, session, Call(5, ToolName, "{\"name\":\"A\",\"city\":\"B\",\"sql\":\"DROP TABLE venues\"}"), ct).ConfigureAwait(false);
                    Check.True(extra.Contains("\"isError\":true", StringComparison.Ordinal), "An unknown argument is a tool error: " + extra);
                    string tooLong = await BodyAsync(http, serverA, session, Call(6, ToolName, "{\"name\":\"" + new string('x', 201) + "\",\"city\":\"B\"}"), ct).ConfigureAwait(false);
                    Check.True(tooLong.Contains("\"isError\":true", StringComparison.Ordinal), "An argument over its maxLength is a tool error");
                    string refused = await BodyAsync(http, serverA, session, Call(7, ToolName, "{\"name\":\"Forbidden Community Center\",\"city\":\"B\"}"), ct).ConfigureAwait(false);
                    Check.True(refused.Contains("\"isError\":true", StringComparison.Ordinal) && refused.Contains("not allowed", StringComparison.Ordinal), "A call the handler refuses is a tool error with its message");
                    string foreign = await BodyAsync(http, serverA, session, Call(8, "report_source", ValidArguments), ct).ConfigureAwait(false);
                    Check.True(foreign.Contains("\"error\"", StringComparison.Ordinal) && foreign.Contains("-32602", StringComparison.Ordinal), "Another item's tool is unknown here: " + foreign);

                    Check.Equal(1, storedA.Count, "Only the valid call was stored");
                    Check.True(storedA.Single().StartsWith("Example Community Center|jbi_a|", StringComparison.Ordinal), "Stored for item A");
                    Check.Equal(0, storedB.Count, "Item B's handler never ran");
                    List<AgentToolRecord> records = toolsA.Records();
                    Check.True(records.Count(r => r.Accepted) == 1, "One accepted record");
                    Check.True(records.Any(r => !r.Accepted && r.Message.Contains("not allowed", StringComparison.Ordinal)), "The handler's refusal is recorded");
                }
            }
            finally
            {
                serverA.Dispose();
                serverB.Dispose();
            }
            bool refusedAfterStop;
            using (TcpClient client = new TcpClient())
            {
                try
                {
                    await client.ConnectAsync(IPAddress.Loopback, portA, ct).ConfigureAwait(false);
                    refusedAfterStop = false;
                }
                catch (SocketException)
                {
                    refusedAfterStop = true;
                }
            }
            Check.True(refusedAfterStop, "The server stops with its call");
        }

        private static async Task LaunchAsync(CancellationToken ct)
        {
            string parent = Path.Combine(Path.GetTempPath(), "tk-harness-suite-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Hashtable inherited = new Hashtable
            {
                ["PATH"] = "/usr/bin:/bin",
                ["HOME"] = "/home/server",
                ["TK_DB_PASSWORD"] = "secret",
                ["TK_SECRETS_ENCRYPTION_KEY"] = "secret",
                ["ANTHROPIC_API_KEY"] = "sk-ant",
                ["OPENAI_API_KEY"] = "sk-openai",
                ["AWS_CONTAINER_CREDENTIALS_RELATIVE_URI"] = "/v2/credentials",
                ["EXTRA_ALLOWED"] = "yes"
            };
            using (HarnessSandbox sandbox = new HarnessSandbox(parent, "ClaudeCode"))
            {
                Dictionary<string, string?> claude = HarnessLaunchPlanner.Environment(AiExecutorRuntimeEnum.ClaudeCode, sandbox, "EXTRA_ALLOWED, TK_DB_PASSWORD", inherited);
                Check.True(claude.ContainsKey("TK_DB_PASSWORD") && claude["TK_DB_PASSWORD"] == null, "TK_ settings are removed, even when listed");
                Check.True(claude.ContainsKey("TK_SECRETS_ENCRYPTION_KEY") && claude["TK_SECRETS_ENCRYPTION_KEY"] == null, "The secrets key is removed");
                Check.True(claude.ContainsKey("OPENAI_API_KEY") && claude["OPENAI_API_KEY"] == null, "Other vendors' keys are removed");
                Check.True(claude.ContainsKey("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI") && claude["AWS_CONTAINER_CREDENTIALS_RELATIVE_URI"] == null, "Task role credentials are removed");
                Check.True(!claude.ContainsKey("ANTHROPIC_API_KEY") && !claude.ContainsKey("PATH") && !claude.ContainsKey("EXTRA_ALLOWED"), "The runtime's key, PATH, and listed extras are kept");
                Check.Equal(sandbox.Home, claude["HOME"] ?? String.Empty, "HOME is the sandbox home");
                Check.Equal(sandbox.Temp, claude["TMPDIR"] ?? String.Empty, "TMPDIR is the sandbox");
                Dictionary<string, string?> codex = HarnessLaunchPlanner.Environment(AiExecutorRuntimeEnum.Codex, sandbox, null, inherited);
                Check.True(codex.ContainsKey("ANTHROPIC_API_KEY") && !codex.ContainsKey("OPENAI_API_KEY"), "Codex keeps only its own key");
                Check.Equal(Path.Combine(sandbox.Home, ".codex"), codex["CODEX_HOME"] ?? String.Empty, "Codex home in the sandbox");
                Dictionary<string, string?> mux = HarnessLaunchPlanner.Environment(AiExecutorRuntimeEnum.Mux, sandbox, null, inherited);
                Check.Equal(Path.Combine("/home/server", ".mux"), mux["MUX_CONFIG_DIR"] ?? String.Empty, "Mux keeps the worker's endpoints");

                // Harness profiles: a stored credential replaces the worker's own, in the runtime's own variable.
                Check.True(HarnessLaunchPlanner.CredentialVariables(AiExecutorRuntimeEnum.ClaudeCode).Contains("CLAUDE_CODE_OAUTH_TOKEN"), "The Environment type keeps a worker's Claude Code OAuth token");
                Dictionary<string, string?> unchanged = HarnessLaunchPlanner.Environment(AiExecutorRuntimeEnum.ClaudeCode, sandbox, null, inherited);
                HarnessLaunchPlanner.ApplyProfile(unchanged, AiExecutorRuntimeEnum.ClaudeCode, HarnessCredentialTypeEnum.Environment, null, null);
                Check.True(!unchanged.ContainsKey("ANTHROPIC_API_KEY") && !unchanged.ContainsKey("ANTHROPIC_BASE_URL"), "The Environment type leaves the worker's key in place");
                Dictionary<string, string?> apiKey = HarnessLaunchPlanner.Environment(AiExecutorRuntimeEnum.ClaudeCode, sandbox, null, inherited);
                HarnessLaunchPlanner.ApplyProfile(apiKey, AiExecutorRuntimeEnum.ClaudeCode, HarnessCredentialTypeEnum.ApiKey, "stored-key", null);
                Check.Equal("stored-key", apiKey["ANTHROPIC_API_KEY"] ?? String.Empty, "Claude Code API key in ANTHROPIC_API_KEY");
                Check.True(apiKey["ANTHROPIC_AUTH_TOKEN"] == null && apiKey["CLAUDE_CODE_OAUTH_TOKEN"] == null && apiKey["CLAUDE_CODE_USE_BEDROCK"] == null, "Every other inherited Claude Code credential is removed");
                Dictionary<string, string?> oauth = HarnessLaunchPlanner.Environment(AiExecutorRuntimeEnum.ClaudeCode, sandbox, null, inherited);
                HarnessLaunchPlanner.ApplyProfile(oauth, AiExecutorRuntimeEnum.ClaudeCode, HarnessCredentialTypeEnum.OAuthToken, "stored-oauth", null);
                Check.Equal("stored-oauth", oauth["CLAUDE_CODE_OAUTH_TOKEN"] ?? String.Empty, "Claude Code OAuth token in CLAUDE_CODE_OAUTH_TOKEN");
                Check.True(oauth.ContainsKey("ANTHROPIC_API_KEY") && oauth["ANTHROPIC_API_KEY"] == null, "The inherited API key cannot shadow the stored OAuth token");
                Dictionary<string, string?> gateway = HarnessLaunchPlanner.Environment(AiExecutorRuntimeEnum.ClaudeCode, sandbox, null, inherited);
                HarnessLaunchPlanner.ApplyProfile(gateway, AiExecutorRuntimeEnum.ClaudeCode, HarnessCredentialTypeEnum.AuthToken, "stored-auth", "https://gateway.example.test/anthropic ");
                Check.Equal("stored-auth", gateway["ANTHROPIC_AUTH_TOKEN"] ?? String.Empty, "Claude Code auth token in ANTHROPIC_AUTH_TOKEN");
                Check.Equal("https://gateway.example.test/anthropic", gateway["ANTHROPIC_BASE_URL"] ?? String.Empty, "With ANTHROPIC_BASE_URL");
                Dictionary<string, string?> codexKey = HarnessLaunchPlanner.Environment(AiExecutorRuntimeEnum.Codex, sandbox, null, inherited);
                HarnessLaunchPlanner.ApplyProfile(codexKey, AiExecutorRuntimeEnum.Codex, HarnessCredentialTypeEnum.ApiKey, "stored-codex", "https://gateway.example.test/v1");
                Check.True(codexKey["CODEX_API_KEY"] == "stored-codex" && codexKey["OPENAI_API_KEY"] == "stored-codex" && codexKey["OPENAI_BASE_URL"] == "https://gateway.example.test/v1", "Codex key in CODEX_API_KEY and OPENAI_API_KEY, base URL in OPENAI_BASE_URL");
                Dictionary<string, string?> geminiKey = HarnessLaunchPlanner.Environment(AiExecutorRuntimeEnum.GeminiCli, sandbox, null, inherited);
                HarnessLaunchPlanner.ApplyProfile(geminiKey, AiExecutorRuntimeEnum.GeminiCli, HarnessCredentialTypeEnum.ApiKey, "stored-gemini", null);
                Check.True(geminiKey["GEMINI_API_KEY"] == "stored-gemini" && geminiKey["GOOGLE_API_KEY"] == null, "Gemini CLI key in GEMINI_API_KEY");
                Dictionary<string, string?> cursorKey = HarnessLaunchPlanner.Environment(AiExecutorRuntimeEnum.CursorCli, sandbox, null, inherited);
                HarnessLaunchPlanner.ApplyProfile(cursorKey, AiExecutorRuntimeEnum.CursorCli, HarnessCredentialTypeEnum.ApiKey, "stored-cursor", "https://ignored.example.test");
                Check.True(cursorKey["CURSOR_API_KEY"] == "stored-cursor" && !cursorKey.Values.Contains("https://ignored.example.test"), "Cursor CLI key in CURSOR_API_KEY, and no base URL variable");
                Dictionary<string, string?> muxKey = HarnessLaunchPlanner.Environment(AiExecutorRuntimeEnum.Mux, sandbox, null, inherited);
                HarnessLaunchPlanner.ApplyProfile(muxKey, AiExecutorRuntimeEnum.Mux, HarnessCredentialTypeEnum.ApiKey, "stored-mux", null);
                Check.True(!muxKey.Values.Contains("stored-mux"), "Mux takes no stored credential");
                Check.True(HarnessLaunchPlanner.CredentialTypes(AiExecutorRuntimeEnum.OpenCode).Single() == HarnessCredentialTypeEnum.Environment && HarnessLaunchPlanner.CredentialTypes(AiExecutorRuntimeEnum.ClaudeCode).Count == 4, "Credential types per runtime");
                Check.True(HarnessProfileService.PermissionOf(new AiHarnessProfile { PermissionMode = "ReadOnly" }) == CliPermissionMode.ReadOnly && HarnessProfileService.PermissionOf(new AiHarnessProfile()) == null, "A profile forces Restricted or ReadOnly, or leaves the mode to the job");

                ConcurrentQueue<string> stored = new ConcurrentQueue<string>();
                AgentToolSet tools = new AgentToolSet(new[] { Tool(stored) }, 2);
                JobToolServer server = await JobToolServer.StartAsync(tools, null, null, ct).ConfigureAwait(false);
                try
                {
                    HarnessLaunchPlan claudePlan = HarnessLaunchPlanner.Plan(AiExecutorRuntimeEnum.ClaudeCode, sandbox, server, true);
                    Check.True(claudePlan.ExtraArguments.ContainsKey("--strict-mcp-config") && claudePlan.ExtraArguments["--setting-sources"] == "project,local", "Claude Code loads no user settings or other servers");
                    Check.Equal("mcp__trelliskit,WebSearch,WebFetch", claudePlan.ExtraArguments["--allowedTools"] ?? String.Empty, "Claude Code pre-approves the job tools and web research");
                    string mcp = claudePlan.Files[claudePlan.ExtraArguments["--mcp-config"]!];
                    Check.True(mcp.Contains(server.Url, StringComparison.Ordinal) && mcp.Contains("Bearer " + server.Token, StringComparison.Ordinal), "The MCP file names the server and token");
                    HarnessLaunchPlan codexPlan = HarnessLaunchPlanner.Plan(AiExecutorRuntimeEnum.Codex, sandbox, server, false);
                    string toml = codexPlan.Files[Path.Combine(sandbox.Home, ".codex", "config.toml")];
                    Check.True(toml.Contains("[mcp_servers.trelliskit]", StringComparison.Ordinal) && toml.Contains(server.Url, StringComparison.Ordinal), "Codex config names only the job tool server");
                    HarnessLaunchPlan geminiPlan = HarnessLaunchPlanner.Plan(AiExecutorRuntimeEnum.GeminiCli, sandbox, server, false);
                    Check.True(geminiPlan.Files[Path.Combine(sandbox.Home, ".gemini", "settings.json")].Contains("\"trust\":true", StringComparison.Ordinal), "Gemini CLI trusts only the job tool server");
                    Check.True(geminiPlan.Files[Path.Combine(sandbox.Home, ".gemini", "trustedFolders.json")].Contains("TRUST_FOLDER", StringComparison.Ordinal), "Gemini CLI trusts the throwaway working directory");
                    Check.Equal("true", HarnessLaunchPlanner.Environment(AiExecutorRuntimeEnum.GeminiCli, sandbox, null, inherited)["GEMINI_CLI_TRUST_WORKSPACE"] ?? String.Empty, "Gemini CLI loads MCP servers in the workspace");
                    HarnessLaunchPlan muxPlan = HarnessLaunchPlanner.Plan(AiExecutorRuntimeEnum.Mux, sandbox, server, false);
                    Check.True(muxPlan.ExtraArguments.ContainsKey("--strict-mcp-config") && muxPlan.Files.Values.Single().Contains("\"bearerToken\"", StringComparison.Ordinal), "Mux gets a strict per-call server list");
                    Check.True(!HarnessLaunchPlanner.Plan(AiExecutorRuntimeEnum.OpenCode, sandbox, server, false).Supported, "OpenCode cannot take job tools");
                    Check.True(!HarnessLaunchPlanner.Plan(AiExecutorRuntimeEnum.Pi, sandbox, server, false).Supported, "Pi cannot take job tools");
                    Check.True(HarnessLaunchPlanner.Plan(AiExecutorRuntimeEnum.OpenCode, sandbox, null, false).Supported, "OpenCode can answer plain prompts");
                }
                finally
                {
                    server.Dispose();
                }

                AgentToolResult bad = await tools.InvokeAsync(ToolName, "{not json", ct).ConfigureAwait(false);
                Check.True(!bad.Accepted && tools.Records().Last().Error == "InvalidJson", "Invalid JSON is refused");
                AgentToolResult unknown = await tools.InvokeAsync("drop_tables", "{}", ct).ConfigureAwait(false);
                Check.True(!unknown.Accepted && tools.Records().Last().Error == "UnknownTool", "Unknown tools are refused");
                AgentToolResult limit = await tools.InvokeAsync(ToolName, ValidArguments, ct).ConfigureAwait(false);
                Check.True(!limit.Accepted && tools.Records().Last().Error == "ToolLimit", "Calls past the attempt limit are refused");
                tools.BeginAttempt(1, AiExecutorRuntimeEnum.ApiEndpoint);
                AgentToolResult again = await tools.InvokeAsync(ToolName, ValidArguments, ct).ConfigureAwait(false);
                Check.True(again.Accepted && tools.Records().Last().ExecutorPosition == 1, "The next executor attempt gets its own limit");
                AgentToolResult invalid = await tools.InvokeAsync(ToolName, InvalidArguments, ct).ConfigureAwait(false);
                Check.True(!invalid.Accepted && tools.Records().Last().Error == "InvalidArguments", "The engine validates arguments too");
                Check.Equal(1, stored.Count, "Only the valid call was stored");
                Check.Throws<ArgumentException>(() => new AgentTool { Name = "Bad-Name" }, "Tool names are lower snake case");
                Check.Throws<ArgumentException>(() => new AgentToolSet(new[] { Tool(stored), Tool(stored) }, 5), "Duplicate tool names are refused");
                Check.Throws<ArgumentException>(() => new AgentToolSet(new[] { new AgentTool { Name = "no_handler" } }, 5), "A tool needs a handler");
                Check.True(Directory.Exists(sandbox.Work), "Sandbox exists while in use");
            }
            Check.True(Directory.GetDirectories(parent).Length == 0, "The sandbox is removed on dispose");
            Directory.Delete(parent, true);
        }

        private static async Task ProfileAdminAsync(HttpFixture f, FakeModelServer model, CancellationToken ct)
        {
            JsonElement list = await Api.ExpectAsync(f, HttpMethod.Get, Profiles, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<JsonElement> all = list.GetProperty("objects").EnumerateArray().ToList();
            Check.Equal(7, all.Count, "Every harness runtime is listed");
            Check.True(all.All(p => !p.GetProperty("saved").GetBoolean() && Api.Str(p, "credentialType") == "Environment" && !p.GetProperty("hasCredential").GetBoolean()), "Nothing is saved at first: every runtime uses the worker's environment");
            JsonElement claude = all.Single(p => Api.Str(p, "runtime") == "ClaudeCode");
            Check.Equal("Environment,ApiKey,OAuthToken,AuthToken", String.Join(",", claude.GetProperty("credentialTypes").EnumerateArray().Select(t => t.GetString())), "Claude Code credential types");
            Check.Equal("ANTHROPIC_BASE_URL", Api.Str(claude, "baseUrlVariable"), "Claude Code base URL variable");
            Check.Equal("CLAUDE_CODE_OAUTH_TOKEN", claude.GetProperty("credentialVariablesByType").GetProperty("OAuthToken").EnumerateArray().Single().GetString() ?? String.Empty, "The view names the variable of each stored credential type");
            Check.True(claude.GetProperty("runsOnThisInstance").GetBoolean() && claude.GetProperty("defaultTimeoutSeconds").GetInt32() == 600, "The view names the instance's settings");
            Check.Equal("Environment", String.Join(",", all.Single(p => Api.Str(p, "runtime") == "Mux").GetProperty("credentialTypes").EnumerateArray().Select(t => t.GetString())), "Mux uses its own configuration");

            ApiResult saved = await Api.SendAsync(f, HttpMethod.Put, Profiles + "/ClaudeCode", HttpFixture.AdminToken, new
            {
                model = "opus", permissionMode = "ReadOnly", timeoutSeconds = 120, maxToolCalls = 5, allowWebResearch = false, baseUrl = Gateway, credentialType = "OAuthToken", credential = StoredToken
            }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, saved.Status, "Saved (" + saved.Text + ")");
            Check.True(!saved.Text.Contains(StoredToken, StringComparison.Ordinal), "The save response never returns the credential");
            Check.True(saved.Body.GetProperty("hasCredential").GetBoolean() && Api.Str(saved.Body, "credentialType") == "OAuthToken", "A credential is stored");
            Check.Equal("CLAUDE_CODE_OAUTH_TOKEN", saved.Body.GetProperty("credentialVariables").EnumerateArray().Single().GetString() ?? String.Empty, "The variable the credential is passed in");
            Check.True(Api.Str(saved.Body, "model") == "opus" && Api.Str(saved.Body, "permissionMode") == "ReadOnly" && saved.Body.GetProperty("timeoutSeconds").GetInt32() == 120 && saved.Body.GetProperty("maxToolCalls").GetInt32() == 5 && !saved.Body.GetProperty("allowWebResearch").GetBoolean() && Api.Str(saved.Body, "baseUrl") == Gateway, "Every field is stored");
            DataTable stored = await f.Raw.ExecuteQueryAsync("SELECT credentialsecret FROM aiharnessprofiles WHERE runtime = 'ClaudeCode';", false, ct).ConfigureAwait(false);
            string envelope = RowReader.GetString(stored.Rows[0], "credentialsecret");
            Check.True(envelope.Length > 20 && !envelope.Contains(StoredToken, StringComparison.Ordinal), "The credential is encrypted at rest");
            ApiResult read = await Api.SendAsync(f, HttpMethod.Get, Profiles + "/ClaudeCode", HttpFixture.AdminToken, null, ct).ConfigureAwait(false);
            Check.True(read.Status == HttpStatusCode.OK && !read.Text.Contains(StoredToken, StringComparison.Ordinal) && !read.Text.Contains(envelope, StringComparison.Ordinal), "A read returns neither the credential nor its ciphertext");
            ApiResult listed = await Api.SendAsync(f, HttpMethod.Get, Profiles, HttpFixture.AdminToken, null, ct).ConfigureAwait(false);
            Check.True(!listed.Text.Contains(StoredToken, StringComparison.Ordinal) && !listed.Text.Contains(envelope, StringComparison.Ordinal), "Nor does the list");

            // Omitting the credential keeps it; changing the type needs a new one.
            JsonElement kept = await Api.ExpectAsync(f, HttpMethod.Put, Profiles + "/ClaudeCode", HttpFixture.AdminToken, new { model = "sonnet", credentialType = "OAuthToken" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(kept.GetProperty("hasCredential").GetBoolean() && Api.Str(kept, "model") == "sonnet" && kept.GetProperty("timeoutSeconds").ValueKind == JsonValueKind.Null && kept.GetProperty("allowWebResearch").GetBoolean(), "The credential is kept and omitted fields return to their defaults");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, Profiles + "/ClaudeCode", HttpFixture.AdminToken, new { credentialType = "ApiKey" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, Profiles + "/ClaudeCode", HttpFixture.AdminToken, new { credentialType = "OAuthToken", credential = "" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);

            // Validation.
            await Api.ExpectErrorAsync(f, HttpMethod.Put, Profiles + "/ClaudeCode", HttpFixture.AdminToken, new { permissionMode = "Unrestricted" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, Profiles + "/ClaudeCode", HttpFixture.AdminToken, new { permissionMode = "WorkspaceWrite" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, Profiles + "/ClaudeCode", HttpFixture.AdminToken, new { timeoutSeconds = 5 }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, Profiles + "/ClaudeCode", HttpFixture.AdminToken, new { maxToolCalls = 501 }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, Profiles + "/ClaudeCode", HttpFixture.AdminToken, new { baseUrl = "ftp://gateway.example.test" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, Profiles + "/ClaudeCode", HttpFixture.AdminToken, new { baseUrl = "https://user:pass@gateway.example.test" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, Profiles + "/ClaudeCode", HttpFixture.AdminToken, new { credentialType = "AuthToken", credential = "gateway-token-not-real" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, Profiles + "/ClaudeCode", HttpFixture.AdminToken, new { credentialType = "Password", credential = "x" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, Profiles + "/Mux", HttpFixture.AdminToken, new { credentialType = "ApiKey", credential = "x" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, Profiles + "/Codex", HttpFixture.AdminToken, new { credentialType = "OAuthToken", credential = "x" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, Profiles + "/CursorCli", HttpFixture.AdminToken, new { baseUrl = "https://gateway.example.test" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, Profiles + "/ApiEndpoint", HttpFixture.AdminToken, new { model = "x" }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, Profiles + "/NoSuchRuntime", HttpFixture.AdminToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            JsonElement codex = await Api.ExpectAsync(f, HttpMethod.Put, Profiles + "/Codex", HttpFixture.AdminToken, new { credentialType = "ApiKey", credential = "codex-key-not-real", baseUrl = "http://127.0.0.1:9/v1" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("CODEX_API_KEY,OPENAI_API_KEY", String.Join(",", codex.GetProperty("credentialVariables").EnumerateArray().Select(v => v.GetString())), "Codex credential variables");
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, Profiles, member.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, Profiles + "/ClaudeCode", member.AccessToken, new { model = "x" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);

            // Reveal: the endpoint reveal rules.
            await Api.ExpectErrorAsync(f, HttpMethod.Post, Profiles + "/ClaudeCode/reveal", HttpFixture.AdminToken, new { reason = "Rotating the harness token" }, HttpStatusCode.Forbidden, "RevealNotAllowed", ct).ConfigureAwait(false);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, Profiles + "/ClaudeCode/reveal", admin.AccessToken, new { reason = "short" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            ApiResult revealed = await Api.SendAsync(f, HttpMethod.Post, Profiles + "/ClaudeCode/reveal", admin.AccessToken, new { reason = "Rotating the harness token" }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, revealed.Status, "Revealed (" + revealed.Text + ")");
            Check.Equal(StoredToken, Api.Str(revealed.Body, "value"), "The revealed value");
            Check.True(revealed.Headers.TryGetValue("Cache-Control", out string? cache) && cache.Contains("no-store", StringComparison.Ordinal), "no-store");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, Profiles + "/GeminiCli/reveal", admin.AccessToken, new { reason = "Nothing is stored here" }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            DataTable audit = await f.Raw.ExecuteQueryAsync("SELECT action, summary FROM auditrecords WHERE action IN ('ai.harness.update', 'ai.harness.reveal');", false, ct).ConfigureAwait(false);
            List<string> summaries = audit.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "action") + " " + RowReader.GetString(r, "summary")).ToList();
            Check.True(summaries.Count(x => x.StartsWith("ai.harness.update", StringComparison.Ordinal)) >= 3 && summaries.Count(x => x.StartsWith("ai.harness.reveal", StringComparison.Ordinal)) == 1, "Saves and the reveal are audited");
            Check.True(summaries.All(x => !x.Contains(StoredToken, StringComparison.Ordinal) && !x.Contains("codex-key-not-real", StringComparison.Ordinal)), "The audit trail holds no credential");

            // Key rotation covers harness credentials.
            string oldKey = f.Settings.Secrets.EncryptionKey;
            f.Settings.Secrets.EncryptionKey = SecretBox.GenerateKey();
            f.Settings.Secrets.PreviousEncryptionKeys = oldKey;
            Check.True(await f.Services.SecretRotation.RemainingAsync(ct).ConfigureAwait(false) >= 2, "Both harness credentials use the previous key");
            for (int i = 0; i < 10 && await f.Services.SecretRotation.RemainingAsync(ct).ConfigureAwait(false) > 0; i++) await f.Services.SecretRotation.RunPassAsync(ct).ConfigureAwait(false);
            Check.Equal(0L, await f.Services.SecretRotation.RemainingAsync(ct).ConfigureAwait(false), "None remaining after the rotation pass");
            f.Settings.Secrets.PreviousEncryptionKeys = String.Empty;
            AiHarnessProfile rotated = (await f.Services.Ai.HarnessProfiles.ReadAsync(AiExecutorRuntimeEnum.ClaudeCode, ct).ConfigureAwait(false))!;
            Check.Equal(StoredToken, f.Services.Ai.HarnessProfiles.Credential(rotated) ?? String.Empty, "The credential decrypts with the new key alone");

            // Back to the worker's environment: the stored credential is deleted.
            JsonElement cleared = await Api.ExpectAsync(f, HttpMethod.Put, Profiles + "/ClaudeCode", HttpFixture.AdminToken, new { credentialType = "Environment" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!cleared.GetProperty("hasCredential").GetBoolean(), "No credential with the Environment type");
            Check.Equal(0L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM aiharnessprofiles WHERE runtime = 'ClaudeCode' AND credentialsecret IS NOT NULL;", ct).ConfigureAwait(false), "The stored credential is gone");
        }

        private static async Task ProfileLaunchAsync(HttpFixture f, FakeModelServer model, CancellationToken ct)
        {
            if (OperatingSystem.IsWindows()) return;
            string? inheritedKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "worker-env-key-not-real");
            try
            {
                using (FakeHarnessCli cli = new FakeHarnessCli(ToolName, ValidArguments, ValidArguments.Replace("Example Community Center", "Second Community Center", StringComparison.Ordinal)))
                {
                    f.Services.Ai.Harnesses.Executables[AiExecutorRuntimeEnum.ClaudeCode] = cli.ScriptPath;
                    await HierarchyAsync(f, model, ct, null).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, Profiles + "/ClaudeCode", HttpFixture.AdminToken, new
                    {
                        model = "profile-model", permissionMode = "ReadOnly", timeoutSeconds = 90, maxToolCalls = 1, allowWebResearch = false, baseUrl = Gateway, credentialType = "OAuthToken", credential = StoredToken
                    }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    model.ClearRequests();
                    ConcurrentQueue<string> stored = new ConcurrentQueue<string>();
                    AgentRequest request = Request(stored);
                    request.AllowWebResearch = true;
                    AgentOutcome outcome = await InJobAsync(f, true, t => f.Services.Ai.Roles.RunAgentAsync("Research", request, t), ct).ConfigureAwait(false);
                    Check.True(outcome.Success && outcome.Runtime == "ClaudeCode", "The harness finished (" + outcome.ErrorCode + "; " + String.Join(" | ", outcome.Escalations) + ")");
                    Check.Equal(0, model.Requests.Count, "No API call was needed");

                    string env = cli.Read("env");
                    Check.True(env.Contains("CLAUDE_CODE_OAUTH_TOKEN=" + StoredToken, StringComparison.Ordinal), "The stored OAuth token reaches the CLI as CLAUDE_CODE_OAUTH_TOKEN");
                    Check.True(!env.Contains("ANTHROPIC_API_KEY=", StringComparison.Ordinal), "The worker's own API key is removed, so it cannot shadow the stored token");
                    Check.True(env.Contains("ANTHROPIC_BASE_URL=" + Gateway, StringComparison.Ordinal), "The base URL reaches the CLI as ANTHROPIC_BASE_URL");
                    Check.True(!env.Contains("TK_", StringComparison.Ordinal) || !env.Split('\n').Any(l => l.StartsWith("TK_", StringComparison.Ordinal)), "Server settings stay scrubbed");
                    List<string> args = cli.ArgumentLines();
                    Check.True(args.Contains("--model") && args[args.IndexOf("--model") + 1] == "profile-model", "The profile's model is used when the executor names none");
                    Check.True(args.Contains("--disallowedTools") && !args.Contains("--tools"), "The profile's ReadOnly mode replaces the job's Restricted mode");
                    Check.True(args.Contains("--allowedTools") && args[args.IndexOf("--allowedTools") + 1] == "mcp__trelliskit", "Web research is off for this runtime although the job asked for it");
                    Check.Equal(1, stored.Count, "The profile's tool call limit of 1 let one call through");
                    Check.True(outcome.ToolCalls.Count == 2 && outcome.ToolCalls[1].Error == "ToolLimit", "The second call was refused by the profile's limit: " + outcome.ToolCalls.Last().Error);

                    List<JsonElement> rows = await LedgerAsync(f, ct).ConfigureAwait(false);
                    JsonElement row = rows.Single(r => Api.Str(r, "runtime") == "ClaudeCode");
                    Check.True(!row.GetRawText().Contains(StoredToken, StringComparison.Ordinal) && !outcome.Text.Contains(StoredToken, StringComparison.Ordinal), "The credential is in neither the ledger nor the outcome");
                    Check.Equal(FakeHarnessCli.Model, Api.Str(row, "model"), "The ledger records the model the CLI reported");

                    // A stored credential that cannot be decrypted fails the call instead of falling back to the worker's key.
                    string goodKey = f.Settings.Secrets.EncryptionKey;
                    f.Settings.Secrets.EncryptionKey = SecretBox.GenerateKey();
                    ConcurrentQueue<string> unreadable = new ConcurrentQueue<string>();
                    AgentOutcome broken = await InJobAsync(f, true, t => f.Services.Ai.Roles.RunAgentAsync("Research", Request(unreadable), t), ct).ConfigureAwait(false);
                    f.Settings.Secrets.EncryptionKey = goodKey;
                    Check.True(broken.Escalations.Any(x => x.Contains("HarnessCredentialUnreadable", StringComparison.Ordinal)), "An unreadable credential is an explicit failure: " + String.Join(" | ", broken.Escalations));
                    Check.Equal("ApiEndpoint", broken.Runtime ?? String.Empty, "The role fell through to its API executor");

                    // The Environment type goes back to the worker's own credentials.
                    await Api.ExpectAsync(f, HttpMethod.Put, Profiles + "/ClaudeCode", HttpFixture.AdminToken, new { credentialType = "Environment" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    ConcurrentQueue<string> again = new ConcurrentQueue<string>();
                    AgentOutcome second = await InJobAsync(f, true, t => f.Services.Ai.Roles.RunAgentAsync("Research", Request(again), t), ct).ConfigureAwait(false);
                    Check.True(second.Success && second.Runtime == "ClaudeCode", "The harness ran with the worker's environment");
                    string env2 = cli.Read("env");
                    Check.True(env2.Contains("ANTHROPIC_API_KEY=worker-env-key-not-real", StringComparison.Ordinal) && !env2.Contains("CLAUDE_CODE_OAUTH_TOKEN", StringComparison.Ordinal) && !env2.Contains("ANTHROPIC_BASE_URL", StringComparison.Ordinal), "The worker's key is back and nothing stored is passed");
                    List<string> args2 = cli.ArgumentLines();
                    Check.True(!args2.Contains("--model") && args2.Contains("--tools"), "No profile model, and the job's Restricted mode again");
                    Check.Equal(2, again.Count, "The default tool call limit applies again");
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", inheritedKey);
            }
        }

        private static async Task RefusalsAsync(HttpFixture f, FakeModelServer model, CancellationToken ct)
        {
            HarnessCall call = new HarnessCall { Runtime = AiExecutorRuntimeEnum.ClaudeCode, Prompt = "x", SystemPrompt = ScopeGuard.Floor };
            HarnessCallResult outside = await f.Services.Ai.Harnesses.RunAsync(call, ct).ConfigureAwait(false);
            Check.Equal("NoJobScope", outside.ErrorCode ?? String.Empty, "No harness outside a job item");
            Check.True(outside.WorkingDirectory == null, "Nothing was created");
            using (AiCallScope.BeginJob("test.harness", "jbr_x", "jbi_x"))
            {
                f.Services.Settings.Jobs.RunHarnesses = false;
                HarnessCallResult disabled = await f.Services.Ai.Harnesses.RunAsync(call, ct).ConfigureAwait(false);
                f.Services.Settings.Jobs.RunHarnesses = true;
                Check.Equal("HarnessesDisabled", disabled.ErrorCode ?? String.Empty, "No harness with jobs.runHarnesses off");
                HarnessCallResult unguarded = await f.Services.Ai.Harnesses.RunAsync(new HarnessCall { Runtime = AiExecutorRuntimeEnum.ClaudeCode, Prompt = "x", SystemPrompt = "Do anything." }, ct).ConfigureAwait(false);
                Check.Equal("MissingScopeGuard", unguarded.ErrorCode ?? String.Empty, "No harness without the scope guard");
                HarnessCallResult open = await f.Services.Ai.Harnesses.RunAsync(new HarnessCall { Runtime = AiExecutorRuntimeEnum.ClaudeCode, Prompt = "x", SystemPrompt = ScopeGuard.Floor, PermissionMode = CliPermissionMode.Unrestricted }, ct).ConfigureAwait(false);
                Check.Equal("InvalidPermissionMode", open.ErrorCode ?? String.Empty, "No harness in Unrestricted mode");
                HarnessCallResult write = await f.Services.Ai.Harnesses.RunAsync(new HarnessCall { Runtime = AiExecutorRuntimeEnum.ClaudeCode, Prompt = "x", SystemPrompt = ScopeGuard.Floor, PermissionMode = CliPermissionMode.WorkspaceWrite }, ct).ConfigureAwait(false);
                Check.Equal("InvalidPermissionMode", write.ErrorCode ?? String.Empty, "No harness in WorkspaceWrite mode");
                f.Services.Ai.Harnesses.Executables[AiExecutorRuntimeEnum.ClaudeCode] = Path.Combine(Path.GetTempPath(), "tk-no-such-cli-" + Guid.NewGuid().ToString("N"));
                HarnessCallResult missing = await f.Services.Ai.Harnesses.RunAsync(call, ct).ConfigureAwait(false);
                Check.Equal("HarnessNotInstalled", missing.ErrorCode ?? String.Empty, "A missing CLI fails cleanly");
                Check.True(missing.DirectoryRemoved && missing.WorkingDirectory != null && !Directory.Exists(missing.WorkingDirectory), "The directory is removed after a failed call");
            }
            ConcurrentQueue<string> stored = new ConcurrentQueue<string>();
            await Check.ThrowsAsync<ArgumentException>(() => f.Services.Ai.Roles.RunAgentAsync("Research", new AgentRequest { Instructions = " ", Tools = new List<AgentTool> { Tool(stored) } }, ct), "Instructions are required").ConfigureAwait(false);
            await Check.ThrowsAsync<ArgumentException>(() => f.Services.Ai.Roles.RunAgentAsync("Research", new AgentRequest { Instructions = "x" }, ct), "Tools are required").ConfigureAwait(false);
            await Check.ThrowsAsync<ArgumentException>(() => f.Services.Ai.Roles.RunAgentAsync("Research", new AgentRequest { Instructions = "x", Tools = new List<AgentTool> { Tool(stored) }, PermissionMode = CliPermissionMode.Unrestricted }, ct), "Permissive modes are refused").ConfigureAwait(false);
            AgentOutcome none = await f.Services.Ai.Roles.RunAgentAsync("Research", Request(stored), ct).ConfigureAwait(false);
            Check.Equal("NoExecutors", none.ErrorCode ?? String.Empty, "A role without executors answers NoExecutors");
            AgentOutcome unknownRole = await f.Services.Ai.Roles.RunAgentAsync("NoSuchRole", Request(stored), ct).ConfigureAwait(false);
            Check.Equal("UnknownRole", unknownRole.ErrorCode ?? String.Empty, "Unknown role");
        }

        private static string Initialize()
        {
            return "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"suite\",\"version\":\"1.0.0\"}}}";
        }

        private static string Call(int id, string tool, string arguments)
        {
            return "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"tools/call\",\"params\":{\"name\":\"" + tool + "\",\"arguments\":" + arguments + "}}";
        }

        private static async Task<HttpResponseMessage> PostAsync(HttpClient http, string url, string? token, string? session, string json, CancellationToken ct)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
            if (token != null) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            if (session != null)
            {
                request.Headers.TryAddWithoutValidation("Mcp-Session-Id", session);
                request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2025-06-18");
            }
            return await http.SendAsync(request, ct).ConfigureAwait(false);
        }

        private static async Task<string> BodyAsync(HttpClient http, JobToolServer server, string session, string json, CancellationToken ct)
        {
            using (HttpResponseMessage response = await PostAsync(http, server.Url, server.Token, session, json, ct).ConfigureAwait(false))
            {
                return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }
        }

        #endregion
    }
}
