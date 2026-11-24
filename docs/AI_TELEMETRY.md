# AI call telemetry

Every model interaction writes one row to the call ledger (`aicalls`), and every row carries the telemetry of that call. Telemetry is retained even when no pricing is configured.

The ledger is read with `POST /api/v1/admin/ai/calls/enumerate` ([REST_API.md](../REST_API.md), "Admin: AI task roles and call ledger") and shown on `/admin/ai-roles`, where a row opens its full detail. Rows are kept `ai.callRetentionDays` (default 90).

Licensed under the MIT License. See [LICENSE](../LICENSE).

## What counts as a model interaction

One row per call to a model, including calls that fail:

| Path | Purpose | Notes |
|------|---------|-------|
| Assistant chat, streaming or not | `Chat`, `ChatFallback` | One row per attempt and per tool round |
| Tool routing rounds | `ToolRouting` | |
| Title generation, query rewriting | `TitleGeneration`, `QueryRewrite` | Never streamed |
| Moderation classification | `Moderation` | |
| Embeddings | `Embedding` | One row per batch of `ai.embeddingBatchSize` |
| Rerank | `Rerank` | |
| Task role calls and agent tool loops | `Task` | With `role`, `executorPosition`, and `escalationReason` |
| CLI agent harness calls | `Task` | `runtime` is the harness |
| An administrator's endpoint test or validation | `EndpointTest` | Test traffic; validation of an unsaved endpoint has no `endpointId` |
| A health check in `Completion`, `Embedding`, or `Rerank` mode | `HealthCheck` | Test traffic. `Connectivity` checks list models and call none, so they write no row |
| A native tool call the model refused (HTTP 400 naming tools) | the call's purpose | The refused call and the retry with the prompt protocol each get a row |
| A call refused before it reached the provider | the call's purpose | Outcome `Unavailable` (endpoint disabled or unhealthy) or `Busy` (queue full) |

`EndpointTest` and `HealthCheck` rows are ledger only: they stay out of the usage rollups, so they never count toward budgets or analytics. Their cost is still computed from the endpoint's prices.

## Fields

Always present:

| Field | Meaning |
|-------|---------|
| `promptTokens`, `completionTokens`, `reasoningTokens` | As the provider reported them (completion tokens are estimated from the text when a provider reports none) |
| `inputTokens`, `outputTokens` | The same values as `promptTokens` and `completionTokens`, under provider-neutral names |
| `latencyMs` | Overall time of the call, from before the client is built to the end of the response |
| `costUsd` | See "Cost". 0 when the endpoint has no prices |
| `outcome`, `errorCode` | `Ok`, `Error`, `Timeout`, `Busy`, `Unavailable`, `Blocked`, `Cancelled` |

Optional (null when the provider or the path does not report the value):

| Field | Meaning |
|-------|---------|
| `totalTokens` | The provider's total, else prompt plus completion plus reasoning tokens |
| `cachedReadTokens` | Input tokens read from the provider's prompt cache |
| `cacheCreationTokens` | Input tokens written to the provider's prompt cache |
| `inputTextTokens`, `inputImageTokens`, `inputAudioTokens`, `inputVideoTokens` | Input tokens by modality |
| `timeToFirstTokenMs` | Time to the first streamed text or reasoning token. Null for a call that does not stream |
| `timeToLastTokenMs` | Time to the last token. For a call that does not stream, the time until the whole response arrived |
| `streamingMs` | Total streaming time: `timeToLastTokenMs` minus `timeToFirstTokenMs`. Null for a call that does not stream |
| `tokensPerSecond` | PolyPrompt's measurement for a stream; else output tokens over the provider's generation time (Ollama), else over the whole call |
| `queueMs` | Wait for the endpoint's concurrency slot (`limits.maxConcurrentRequests`) |
| `loadMs`, `promptEvalMs`, `evalMs` | Model load, prompt evaluation, and generation time (Ollama) |
| `toolCalls`, `toolExecMs` | See "Tool time" |
| `retries` | Earlier attempts of this call on the same endpoint. Each attempt has its own row |
| `statusCode` | HTTP status of the provider's response |
| `finishReason` | The provider's finish reason (`stop`, `length`, `tool_calls`, `end_turn`, ...) |
| `streamed`, `chunks` | Whether the call streamed, and how many chunks arrived |
| `requestBytes`, `responseBytes` | See "Request and response sizes" |
| `responseId` | The provider's response id (a harness's session id) |
| `providerModel` | The model the provider reported, which can differ from the configured `model` |
| `reasoningEffort` | The reasoning value actually sent on the wire (for example `high`, or a thinking budget) |
| `exitCode` | Exit code of a harness process |

Model: `AiCallDetail` (`backend/src/TrellisKit.Core/Models/AiCallDetail.cs`). Columns: migration 107.

### Cached tokens differ by provider

PolyPrompt reports what each provider sends, and providers disagree about what the prompt total includes:

- OpenAI, Azure OpenAI, OpenAI compatible servers, Gemini, Vertex AI, and Cohere count cached tokens **inside** `promptTokens`.
- Anthropic and Bedrock report cached tokens **in addition to** `promptTokens`, which is only the uncached part.

The ledger stores the values as reported. The cost calculation accounts for the difference.

## Tool time

Tool execution is attributed **to the model call that requested the tools**, not to the exchange and not to the next call.

- `toolCalls` is the number of tool calls a model call asked for. It is 0 for a call that was offered tools and asked for none, and null for a call that was offered none.
- `toolExecMs` is the wall time the engine spent running those calls, measured around the whole batch (so it includes argument validation and refusals). It is written when the batch finishes, by updating the row of the call that asked for it. It is null when no tools ran.
- `latencyMs` of a model call never includes tool time. The time of one exchange is the sum of `latencyMs` and `toolExecMs` over its rows.

This applies to the assistant tool loop (`AssistantToolLoop`) and the agent tool loop (`AgentToolLoop`). The per-tool histogram `trelliskit.ai.tool_call.duration` and the tool call rows of a reply are unchanged.

For a harness call there is one row for the whole CLI run. `toolCalls` is the larger of the tool uses the CLI reported and the job tool calls the engine served, and `toolExecMs` is the time spent in job tool handlers. A harness's own tools (web search, file reads) report no duration, so their time is inside `latencyMs` only.

## Cost

`costUsd` is computed per call from the endpoint's admin-entered prices, in US dollars per million tokens:

| Price | Applies to |
|-------|------------|
| `pricing.inputPerMillionTokens` | Uncached input tokens |
| `pricing.outputPerMillionTokens` | Completion plus reasoning tokens |
| `pricing.cacheReadPerMillionTokens` (optional) | `cachedReadTokens`. Falls back to the input price |
| `pricing.cacheWritePerMillionTokens` (optional) | `cacheCreationTokens`. Falls back to the input price |

For providers that count cached tokens inside the prompt, the cached tokens are taken out of the part billed at the input price. For Anthropic and Bedrock they are added. Every price is optional: an endpoint with no prices records a cost of 0, and its telemetry is stored all the same. A harness call records the cost the CLI reports (Claude Code), or 0.

## Request and response sizes

`requestBytes` is the sum of the request body lengths of the HTTP requests the call sent. `responseBytes` is the sum of the response body lengths the provider declared. Both come from the `Content-Length` values the endpoint's HTTP handler already sees (`EndpointAuthHandler` and `ModelCallProbe`), so measuring them costs nothing.

A streamed response declares no length, so `responseBytes` is null for streamed calls. Counting the bytes of a stream would mean wrapping the response stream of every provider client, which is not done.

## Harness calls

A harness row stores what the CLI reports: prompt, completion, reasoning, cached read, and cache creation tokens, the cost, the model, the session id as `responseId`, and the exit code. PolyPrompt stamps the CLI's events as they arrive, so `timeToFirstTokenMs` and `timeToLastTokenMs` are the first and last text or reasoning event, and `chunks` is the number of such events. `statusCode`, `finishReason`, `queueMs`, and the sizes do not apply and are null.

## What each provider reports

| Value | Reported by |
|-------|-------------|
| Prompt and completion tokens | Every completion provider |
| Reasoning tokens | OpenAI, Azure OpenAI, xAI, Gemini, Vertex AI. Anthropic and Bedrock bill thinking as output tokens |
| Cache read tokens | OpenAI, Azure OpenAI, OpenAI compatible, xAI, Gemini, Vertex AI, Cohere, Anthropic, Bedrock. Harnesses: whichever report them in their final usage (Claude Code, Codex, Gemini CLI, and the others when their CLI prints them) |
| Cache creation tokens | Anthropic, Bedrock. Harnesses: Claude Code and Codex (the others when their CLI prints them) |
| Input tokens by modality | Gemini, Vertex AI; audio tokens on the OpenAI compatible wire |
| Load, prompt evaluation, and generation time | Ollama, Ollama Cloud |
| Response id | OpenAI, Azure OpenAI, OpenAI compatible, xAI, Anthropic, Cohere, Gemini, and rerank providers |
| Embedding tokens | Read from the response JSON: `usage.prompt_tokens` or `usage.total_tokens` (OpenAI wire, Voyage AI), `prompt_eval_count` (Ollama), `meta.billed_units.input_tokens` (Cohere), `usageMetadata.promptTokenCount` (Gemini). PolyPrompt 5.0.1 does not fill `EmbeddingResponse.Usage` |
| Cost | Harnesses only (Claude Code). API calls use the endpoint's prices |

## Metrics

Beside the instruments in [AI_CONTRACT.md](AI_CONTRACT.md) section 16:

| Instrument | Type | Labels |
|------------|------|--------|
| `trelliskit.ai.tokens` | Counter | `direction` gains `cache_read` and `cache_creation` |
| `trelliskit.ai.time_to_last_token` | Histogram (s) | `api_type` |
| `trelliskit.ai.stream.duration` | Histogram (s) | `api_type` |
| `trelliskit.ai.tokens_per_second` | Histogram | `api_type` |
| `trelliskit.ai.call.tool_calls` | Histogram | `api_type` |
| `trelliskit.ai.call.tool_duration` | Histogram (s) | `api_type` |
| `trelliskit.ai.request.bytes` | Histogram (By) | `api_type` |
| `trelliskit.ai.response.bytes` | Histogram (By) | `api_type` |

For a harness, `api_type` is the runtime. Grafana queries these as `trelliskit_ai_*`.

## Not captured

- **Bytes of a streamed response.** See "Request and response sizes".
- **Per-tool time inside a harness.** The CLIs report tool uses without durations.
- **Input tokens by modality on most providers.** Only Gemini and Vertex AI report the split.
- **Cache token counts in the hourly usage rollups.** The rollups (`aiusagerollups`) keep prompt, completion, and reasoning tokens; cached tokens are per call in the ledger.
- **The provider's rate limit headers.** PolyPrompt does not surface response headers unless call detail capture is on (`ai.captureCallDetails`), and they are not copied to the ledger.
