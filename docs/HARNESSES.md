# CLI agent harnesses and job tools

CLI agent harnesses (Claude Code, Codex, Gemini CLI, Cursor CLI, Mux, OpenCode, Pi) are executors that run their own tool loop. TrellisKit runs them through PolyPrompt's `CliAgentClientBase` clients for long, agentic background work such as research and structured extraction. A harness never writes to the database. It hands results back by calling typed **job tools** on a **job-scoped TrellisKit MCP server**, and each tool's handler validates and stores what it accepts.

Licensed under the MIT License. See [LICENSE](../LICENSE).

## Where harnesses run

A harness call runs only when all of these are true:

1. The instance has `jobs.runHarnesses` on. Only the worker image carries the CLIs (see "Enabling a worker").
2. The call is inside a job item (`AiCallScope.Current.JobRunId` and `JobItemId` are set by the job engine). Request paths, including the admin console's role test, never run a harness.
3. The role allows harnesses (`Research`, or a property-defined background completion role that allows harnesses). Interactive roles refuse harness executors when the hierarchy is saved.

When any of these is false, the role passes the harness executor over and the next executor runs. The reason (for example `#0 ClaudeCode: harnesses run only inside a job item, never in request paths`) is in the outcome's escalations and in the `escalationReason` of the next executor's ledger row.

## How a call flows

`services.Ai.Roles.RunAgentAsync(role, request, token)` walks the role's executor hierarchy:

1. The engine builds an `AgentToolSet` from the request's tools. Every call from any executor goes through it.
2. For a harness executor, `HarnessService`:
   - creates a throwaway directory under the system temporary folder (`work`, `home`, `tmp`, `config`, owner-only permissions);
   - starts a `JobToolServer` (an MCP server on `127.0.0.1` and a free port, with a random 256-bit bearer token) serving only this call's tools;
   - writes the runtime's MCP configuration into the sandbox (see "Runtime wiring"), so the harness loads that server and no other;
   - scrubs the environment and points `HOME`, `TMPDIR`, and the XDG folders into the sandbox;
   - runs the CLI with the executor's model, in `Restricted` (default) or `ReadOnly` permission mode, with the job's timeout (`jobs.harnessTimeoutSeconds`, default 600);
   - records the ledger row from the harness's `Completed` or `Failed` event, then stops the server and deletes the directory.
3. For an API endpoint executor, `AgentToolLoop` offers the same tools through `CompletionCall.Tools` (native tools or the JSON prompt protocol, by the endpoint's `toolCalling`), runs each requested call through the same `AgentToolSet`, and calls again until the model answers.
4. The `AgentOutcome` names the executor that finished and lists every tool call, accepted or not, from every executor.

`RunAgentAsync` never throws for provider or harness failures. It throws `ArgumentException` for a request the handler built wrongly (empty instructions, a permission mode other than `Restricted` or `ReadOnly`, no tools, duplicate names, a missing handler, or a schema that is not a JSON object).

The scope guard leads every harness's system instructions: the locked floor (`ScopeGuard.ForPolicy` with the property's `aiPolicy.allowCode`), the administrator's `platform.scopeGuardrail` text, the engine's agent rules (report only through the tools, change no files, run no commands), and then the handler's own system prompt. `HarnessService` refuses a call whose system prompt does not start with the floor. Claude Code and Mux receive it with `--append-system-prompt`; the other harnesses receive it at the head of standard input. The task instructions follow on standard input. API executors get the guard from `ModelInvoker`, as for every other model call.

`TaskRoleService.CompleteAsync` (and `PromptAsync`) also run harness executors for plain prompts inside job items, without job tools.

## Harness profiles

Everything about a harness that an administrator may want to change is in its **harness profile**, edited on `/admin/ai-roles` (the Harnesses table) or with `PUT /api/v1/admin/ai/harnesses/{runtime}` ([REST_API.md](../REST_API.md), "Harness profiles"). There is one profile per runtime and property (`aiharnessprofiles`, migration 107). A runtime without a saved profile behaves as before: the worker's environment and the `jobs.harness*` settings.

| Field | What it does | When it is not set |
|-------|--------------|--------------------|
| `model` | The model the CLI is started with when the role executor names none | The CLI's own default |
| `permissionMode` | `Restricted` or `ReadOnly` for every call of the runtime, whatever the job asked for | The mode the job asks for |
| `timeoutSeconds` | Longest one call runs (30 to 3600) | The job's timeout, else `jobs.harnessTimeoutSeconds` |
| `maxToolCalls` | Job tool calls per executor attempt (1 to 500) | The job's limit, else `jobs.harnessMaxToolCalls` |
| `allowWebResearch` | False turns web search and fetch off for the runtime even when a job asks for them | True |
| `baseUrl` | The vendor API or a gateway the CLI talks to | The vendor default |
| `credentialType`, `credential` | How the CLI authenticates | `Environment`: the worker's own vendor variables |

Order of precedence: an executor's `model` wins over the profile's; for the permission mode, timeout, and tool call limit the profile wins over the job's request, which wins over the settings. Web research needs both the job and the profile to allow it. The modes stay `Restricted` and `ReadOnly`: a profile cannot ask for more, and `HarnessService` refuses anything else whoever asks.

### Credentials

A profile's credential is one of:

- **`Environment`** (default): the vendor credential variables the worker process has, as in "Runtime wiring".
- **`ApiKey`**, **`OAuthToken`**, or **`AuthToken`**: a value stored in the profile. It is encrypted with `secrets.encryptionKey` under associated data naming the property and the runtime (the same envelope as endpoint secrets), rewritten by the key rotation worker and by `POST /api/v1/admin/ai/endpoints/reencrypt`, never returned by a read, never written to the audit trail or the ledger, and revealed only by `POST /api/v1/admin/ai/harnesses/{runtime}/reveal` under the endpoint reveal rules.

`HarnessLaunchPlanner.ApplyProfile` passes the stored value to the CLI process in the variable that runtime reads:

| Runtime | `ApiKey` | `OAuthToken` | `AuthToken` | `baseUrl` |
|---------|----------|--------------|-------------|-----------|
| `ClaudeCode` | `ANTHROPIC_API_KEY` | `CLAUDE_CODE_OAUTH_TOKEN` | `ANTHROPIC_AUTH_TOKEN` (needs `baseUrl`) | `ANTHROPIC_BASE_URL` |
| `Codex` | `CODEX_API_KEY` and `OPENAI_API_KEY` | not accepted | not accepted | `OPENAI_BASE_URL` |
| `GeminiCli` | `GEMINI_API_KEY` | not accepted | not accepted | `GOOGLE_GEMINI_BASE_URL` |
| `CursorCli` | `CURSOR_API_KEY` | not accepted | not accepted | not accepted |
| `Mux`, `OpenCode`, `Pi` | not accepted (they read their own provider configuration) | | | |

When a stored credential is used, every vendor credential variable of that runtime is first removed from the inherited environment (the Bedrock and Vertex switches included), so a key in the worker's environment can never shadow the stored one. A stored credential that is missing or cannot be decrypted (a missing key after a rotation) fails the call with `HarnessCredentialUnreadable`; the engine never falls back to the worker's key. If the profile itself cannot be read, the call is refused with `HarnessProfileUnavailable`.

The owner still checks each vendor's terms before using a consumer OAuth token for automated work; `OAuthToken` exists because Claude Code accepts `CLAUDE_CODE_OAUTH_TOKEN` (from `claude setup-token`), not as a recommendation over a business API key. `GOOGLE_GEMINI_BASE_URL` and `OPENAI_BASE_URL` are set as documented by those CLIs and are not verified against a live gateway.

## MCP transport

The job tool server uses **Streamable HTTP on 127.0.0.1**, through Voltaic's `McpHttpServer` (NuGet `Voltaic` 2.4.1). Reasons:

- Every harness that can take job tools speaks Streamable HTTP natively (Claude Code `type: http`, Codex `[mcp_servers.*] url`, Gemini CLI, Cursor CLI, and Mux `transport: http`). Nothing extra ships in the image, and the harness never has to launch a helper process.
- A stdio transport would need a shim executable that the harness starts and that relays calls back to the worker, which means more moving parts and another channel to secure.
- One server per harness call gives per-item isolation directly: the server lists only that item's tools, and its token is useless anywhere else. There is no shared routing table from tokens to tool sets.
- Voltaic implements the protocol versions the CLIs negotiate (2024-11-05 through 2026-07-28), session handling, schema validation of tool arguments, and loopback-only serving.

Server settings: loopback clients only (`RestrictToLoopbackClients`), no CORS, no legacy `/rpc` or `/events` endpoints, no diagnostic tools, and tool exception messages hidden. The server is named `trelliskit`, so Claude Code shows the tools as `mcp__trelliskit__<tool>`.

## Security model

| A harness can | A harness cannot |
|---------------|------------------|
| Call the job tools of its own job item over the loopback MCP server, with its own token | Reach another item's tools (a different server and token; a wrong token is `401`), or any tool the handler did not declare |
| Read what its permission mode allows inside its empty working directory | Write to the database (only tool handlers store results, after validation) |
| Use web search and fetch when the handler sets `AllowWebResearch` and the CLI's mode allows it | Load other MCP servers: Claude Code and Mux run with `--strict-mcp-config`, Codex, Gemini CLI, and Cursor CLI read configuration only from the throwaway home |
| Read its own vendor credential variables (the worker's, or the profile's stored credential) | Read server settings from its environment: every inherited variable is removed except an allowlist (PATH, locale, terminal, proxies, CA bundles, its own vendor's key variables, and `jobs.harnessPassEnvironment`, which can never re-add a `TK_` variable) |
| Run until `jobs.harnessTimeoutSeconds` | Outlive its call: the process tree is killed at the timeout or when the item is cancelled, the server stops, and the directory is deleted |
| Make up to `jobs.harnessMaxToolCalls` tool calls per executor attempt | Get past argument validation: invalid arguments return an MCP tool error (`isError: true`) and the handler does not run; unknown tools are JSON-RPC errors |

Other rules:

- Permission modes are `Restricted` (default) or `ReadOnly`. `WorkspaceWrite` and `Unrestricted` are refused by both `RunAgentAsync` and `HarnessService`.
- No repository, property folder, secrets, or settings file is mounted into the worker for harnesses, and the working directory starts empty.
- User-level CLI configuration (Claude Code settings and hooks, Codex and Gemini CLI configuration, Cursor rules) is never read: `--setting-sources project,local` for Claude Code and a throwaway `HOME`, `CLAUDE_CONFIG_DIR`, and `CODEX_HOME` for all of them.
- Harness output is data. Tool handlers treat arguments as untrusted input from the internet.
- Production authentication uses vendor API keys or business accounts (`ANTHROPIC_API_KEY`, `CODEX_API_KEY`, `GEMINI_API_KEY`, `CURSOR_API_KEY`), from the worker's environment or stored in the runtime's harness profile. A CLI login stored in the worker user's home is not visible to the throwaway home.

Residual risks (tracked follow-ups):

- The harness runs as the server's operating system user inside the worker container. A harness whose sandbox lets it read arbitrary files (Codex's read-only sandbox, for example) could read the server process's environment through `/proc` or the settings file on the `server-config` volume. Mitigation today: run workers with only the settings they need, and keep vendor keys scoped to the worker. Follow-up: run each harness under a separate unprivileged user.
- The test suite exercises a fake CLI end to end. Live vendor calls and every supported CLI version need verification in the deployment environment before production use.

## Runtime wiring

| Runtime | Job tools | How the job tool server is configured | Credential variables kept |
|---------|-----------|----------------------------------------|---------------------------|
| `ClaudeCode` | Yes | `--strict-mcp-config --mcp-config <sandbox>/config/mcp.json --setting-sources project,local --allowedTools mcp__trelliskit` (plus `WebSearch,WebFetch` with `AllowWebResearch`) | `ANTHROPIC_*`, `CLAUDE_CODE_OAUTH_TOKEN`, Bedrock and Vertex switches |
| `Codex` | Yes | `CODEX_HOME=<sandbox>/home/.codex` with `config.toml` `[mcp_servers.trelliskit]` (url and Authorization header) | `CODEX_API_KEY` (what `codex exec` reads), `OPENAI_API_KEY`, `OPENAI_BASE_URL` |
| `GeminiCli` | Yes | `HOME=<sandbox>/home` with `.gemini/settings.json` (the server is trusted, so its tools run without a prompt), and the working directory trusted (`GEMINI_CLI_TRUST_WORKSPACE`, `trustedFolders.json`), because Gemini CLI disables MCP servers in untrusted folders | `GEMINI_API_KEY`, `GOOGLE_*` (with `GOOGLE_GEMINI_BASE_URL`) |
| `CursorCli` | Yes | `HOME=<sandbox>/home` with `.cursor/mcp.json` and `--approve-mcps` | `CURSOR_API_KEY` |
| `Mux` | Yes | `--strict-mcp-config --mcp-config <sandbox>/config/mux-mcp.json` (bearer auth); `MUX_CONFIG_DIR` keeps the worker's endpoints; the executor's `endpointId` is the Mux endpoint name | `MUX_CONFIG_DIR` |
| `OpenCode` | No | Its restricted permission rules deny every MCP tool; plain prompts only | provider keys |
| `Pi` | No | Runs with `--no-mcp` in restricted modes; plain prompts only | provider keys |

Claude Code's `Restricted` mode exposes only `Read`, `Glob`, and `Grep` among its built-in tools, so web research with Claude Code needs `PermissionMode = ReadOnly` together with `AllowWebResearch`.

## Writing a handler with job tools

```csharp
public async Task<JobItemOutcome> ExecuteAsync(JobItemContext context, CancellationToken token)
{
    AgentTool submit = new AgentTool
    {
        Name = "submit_source",
        Description = "Submit one reference source found during research.",
        ParametersSchema = "{\"type\":\"object\",\"properties\":{" +
            "\"name\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":200}," +
            "\"website\":{\"type\":\"string\",\"maxLength\":500}," +
            "\"city\":{\"type\":\"string\",\"maxLength\":100}}," +
            "\"required\":[\"name\",\"city\"],\"additionalProperties\":false}",
        Handler = async (call, ct) =>
        {
            string name = call.Arguments.GetProperty("name").GetString()!;
            if (context.IsDryRun) return AgentToolResult.Accept("Noted (dry run).");
            // Store idempotently, keyed by the item and an external key, never trusting the arguments.
            await StoreCandidateAsync(context, call, ct);
            return AgentToolResult.Accept("Recorded " + name + ".", call.ArgumentsJson);
        }
    };

    AgentOutcome outcome = await context.Services.Ai.Roles.RunAgentAsync("Research", new AgentRequest
    {
        Instructions = "Find reference sources for this topic: " + context.Item.Payload,
        Tools = new List<AgentTool> { submit },
        PermissionMode = CliPermissionMode.ReadOnly,
        AllowWebResearch = true
    }, token);

    if (!outcome.Success) return JobItemOutcome.Fail(outcome.ErrorCode ?? "AgentFailed", outcome.Retryable);
    return JobItemOutcome.Ok("{\"candidates\":" + outcome.Accepted.Count + "}");
}
```

Set `RequiresHarness = true` on the handler's defaults when its first executors are harnesses, so only workers claim its items. A handler that also has API executors can leave it off: on an API instance the harness is passed over and the API executor runs the same tools.

## Adding a tool

- **Name**: lower snake case, 1 to 64 characters, unique within the request (`submit_document_draft`, `report_source`). At most 32 tools per request.
- **Schema**: an object schema in the subset the engine and Voltaic both validate: `type`, `properties`, `required`, `additionalProperties: false`, `enum`, `minimum`, `maximum`, `minLength`, `maxLength`, `items`, `maxItems` (arrays are capped at 50 items). Always set `additionalProperties: false` and length limits.
- **Handler**: runs on the worker. Validate anything the schema cannot express, refuse with `AgentToolResult.Reject(message)` (the agent reads the message and can retry), and store through the normal services. Handlers must be idempotent, because a later executor (or a retried item) may report the same result again. Honor `context.IsDryRun`.
- **Limits**: arguments are at most 32,768 characters, and an executor attempt makes at most `jobs.harnessMaxToolCalls` calls (default 50). A handler exception is logged and returned to the agent as a generic tool error.

## Settings

| Key | Default | What it does |
|-----|---------|--------------|
| `jobs.runHarnesses` (`TK_JOBS_RUN_HARNESSES`) | `false` | Claim harness job items and run harness executors on this instance |
| `jobs.harnessTimeoutSeconds` (`TK_JOBS_HARNESS_TIMEOUT_SECONDS`) | 600 (30 to 3600) | Longest one harness call runs before its process tree is killed; keep it below `jobs.itemLockMinutes` |
| `jobs.harnessMaxToolCalls` | 50 (1 to 500) | Job tool calls per executor attempt |
| `jobs.harnessPassEnvironment` (`TK_JOBS_HARNESS_PASS_ENVIRONMENT`) | empty | Extra environment variable names passed to harnesses (comma separated; `TK_` names are ignored) |

## Ledger and cost

Each harness call writes one `aicalls` row with `runtime` set to the harness (`ClaudeCode`, `Codex`, ...), the model the harness reported (or the executor's model), prompt, completion, and reasoning tokens, latency, the outcome (`Ok`, `Error`, `Timeout`, `Busy`), an error code such as `HarnessNotInstalled` or `HarnessRateLimited`, and the job, run, and item. The cost is the harness's reported `CostUsd` from its `Completed` event (Claude Code reports it; harnesses that report no cost record 0). It is added to the job item and run like any model cost, and to the usage rollups under the endpoint key `harness:<runtime>`, so it counts toward the property's monthly AI budget. Metrics use `api_type` set to the runtime.

The row also stores the telemetry the CLI reports ([AI_TELEMETRY.md](AI_TELEMETRY.md), "Harness calls"): cached read and cache creation tokens, time to the first and last text event, the number of text events, tool calls and the time spent in job tool handlers, the session id, and the exit code.

## Enabling a worker

Locally:

```bash
cd docker
# Vendor API keys for the harnesses, in docker/.env.local (never committed):
#   TK_HARNESS_ANTHROPIC_API_KEY=...   TK_HARNESS_OPENAI_API_KEY=...   TK_HARNESS_GEMINI_API_KEY=...
#   Claude Code can use one of these instead of an API key:
#   TK_HARNESS_CLAUDE_OAUTH_TOKEN=...            (a Claude Code OAuth token from `claude setup-token`)
#   TK_HARNESS_ANTHROPIC_AUTH_TOKEN=...  TK_HARNESS_ANTHROPIC_BASE_URL=...   (a bearer token for a gateway such as Conductor)
docker compose --env-file .env --env-file .env.local --profile worker up -d --build worker
```

The `worker` service runs the same server, built from the Dockerfile's `worker` target (the local target plus Claude Code, Codex, and Gemini CLI at pinned versions), with `TK_JOBS_RUN_HARNESSES=true`. It publishes no host port; its internal server listens on port 8000. It shares the database, settings, and data volumes with `server`, and uses the same property and settings mounts as the server. The API server image (`combined`, `local`, `runtime` targets) never carries the CLIs.

Pinned CLI versions (build arguments, verified on the npm registry on 2026-11-21):

| Build argument | Compose variable | Version |
|----------------|------------------|---------|
| `CLAUDE_CODE_VERSION` (`@anthropic-ai/claude-code`) | `TK_CLAUDE_CODE_VERSION` | 2.1.330 |
| `CODEX_VERSION` (`@openai/codex`) | `TK_CODEX_VERSION` | 0.171.0 |
| `GEMINI_CLI_VERSION` (`@google/gemini-cli`) | `TK_GEMINI_CLI_VERSION` | 0.70.0 |
| `MUX_CLI_VERSION` (`Mux.Cli` .NET tool) | `TK_MUX_CLI_VERSION` | empty (not installed; Mux.Cli is not on nuget.org yet, set `MUX_NUGET_SOURCE` to a feed that has it) |

Cursor CLI is not installed in the image (its installer is a download script, not a pinned package).

In production, build the `worker-runtime` target (no ffmpeg) and run it as its own service with `TK_JOBS_RUN_HARNESSES=true` and the vendor keys from a secret store. Before enabling a harness in production, the owner checks each vendor's terms for automated and commercial use and provisions business API keys.

Then put the harness first in a role's hierarchy from `/admin/ai-roles` (for example `ClaudeCode` with model `sonnet`, then an API endpoint).

## Testing

The `Harness` Touchstone suite runs a fake Claude Code CLI (`backend/src/Test.Shared/Fakes/FakeHarnessCli.cs`): a POSIX shell script that PolyPrompt launches as the `claude` executable (`services.Ai.Harnesses.Executables`). It records its arguments, standard input, working directory, and environment, calls the job tool server with `curl` (initialize, tools/list, a valid and an invalid `tools/call`), and prints Claude Code's stream-json events with usage and cost. The suite also tests the MCP server directly over HTTP (token isolation and argument validation), the launch plans for every runtime, the refusals, and harness profiles: validation per runtime, the encrypted and write-only credential, the reveal rules, key rotation, and a stored OAuth token, base URL, model, permission mode, and tool call limit reaching the fake CLI's environment and arguments.
