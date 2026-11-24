namespace Test.Shared.Fakes
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// In-process model server speaking the Ollama API (/api/chat, /api/embed, /api/tags) and the OpenAI API
    /// (/v1/chat/completions, the Responses API /v1/responses, /v1/embeddings, /v1/models, and the Azure v1 routes under
    /// /openai/v1/), the Google generateContent API (/v1beta/models/{model}:generateContent and :streamGenerateContent),
    /// and the Anthropic Messages API (/v1/messages), each with native tool calls, so the real PolyPrompt clients, the client factory, and the
    /// endpoint auth handler run in tests. Streams NDJSON (Ollama) and SSE (OpenAI) word by word. Records every request
    /// (method, path, query, headers) for auth assertions. Behaviors are scripted per instance.
    /// Thread safety: safe for concurrent requests.
    /// </summary>
    public sealed class FakeModelServer : IAsyncDisposable
    {
        #region Public-Members

        /// <summary>
        /// Base URL, for example http://127.0.0.1:5123.
        /// </summary>
        public string BaseUrl { get; }

        /// <summary>
        /// Reply text of chat calls. Default "Open Account, then Profile, then tap your photo [1]."
        /// </summary>
        public string ReplyText { get; set; } = "Open Account, then Profile, then tap your photo [1].";

        /// <summary>
        /// Finish reason of chat replies (OpenAI finish_reason, Ollama done_reason). Default stop.
        /// </summary>
        public string FinishReason { get; set; } = "stop";

        /// <summary>
        /// Verdict text returned to the moderation classifier prompt. Default "Allow None".
        /// </summary>
        public string ModerationReply { get; set; } = "Allow None";

        /// <summary>
        /// Verdict returned when the classifier is asked about a reply (the classified text contains
        /// <see cref="ReplyText"/>), or null to use <see cref="ModerationReply"/> for replies too. Default null.
        /// </summary>
        public string? OutputModerationReply { get; set; } = null;

        /// <summary>
        /// Delay before the first streamed chunk in milliseconds. Default 0.
        /// </summary>
        public int FirstChunkDelayMs { get; set; } = 0;

        /// <summary>
        /// Delay between streamed chunks in milliseconds. Default 0.
        /// </summary>
        public int ChunkDelayMs { get; set; } = 0;

        /// <summary>
        /// Number of next chat or embedding calls that fail with <see cref="FailStatus"/>.
        /// </summary>
        public int FailNext { get; set; } = 0;

        /// <summary>
        /// Status of scripted failures. Default 503.
        /// </summary>
        public int FailStatus { get; set; } = 503;

        /// <summary>
        /// Embedding dimensions. Default 16.
        /// </summary>
        public int Dimensions { get; set; } = 16;

        /// <summary>
        /// Access token returned by the fake OAuth token endpoint (/token). Default "fake-access-token".
        /// </summary>
        public string AccessToken { get; set; } = "fake-access-token";

        /// <summary>
        /// Tool calls requested in each round while tools are offered (native tools in the request, or the prompt tool
        /// protocol in the messages). Empty means the model never calls tools.
        /// </summary>
        public List<FakeToolCall> ToolCalls { get; set; } = new List<FakeToolCall>();

        /// <summary>
        /// Rounds in which the model asks for <see cref="ToolCalls"/> before answering with <see cref="ReplyText"/>.
        /// Default 1; a large value keeps asking (loop limit tests).
        /// </summary>
        public int ToolRounds { get; set; } = 1;

        /// <summary>
        /// Text streamed before the tool calls of a round (native mode), or null. Default null.
        /// </summary>
        public string? ToolPreamble { get; set; } = null;

        /// <summary>
        /// Answer HTTP 400 "this model does not support tools" whenever native tools are in the request. Default false.
        /// </summary>
        public bool RejectNativeTools { get; set; } = false;

        /// <summary>
        /// Replies used first, one per chat request, before the default reply logic (scripted multi-turn answers).
        /// </summary>
        public ConcurrentQueue<string> ScriptedReplies { get; } = new ConcurrentQueue<string>();

        /// <summary>
        /// Optional responder for completion requests: given the request's messages as JSON, return the reply text, or
        /// null for the default behavior. Default null.
        /// </summary>
        public Func<string, string?>? Script { get; set; } = null;

        /// <summary>
        /// A path prefix the server is mounted under (for example /v1.0/api/model-a), or null for the root. With a
        /// prefix, requests outside it answer 404, like a gateway that routes a model by path.
        /// </summary>
        public string? PathPrefix { get; set; } = null;

        /// <summary>
        /// Prompt tokens reported as read from the prompt cache (0 reports none): prompt_tokens_details.cached_tokens on
        /// the OpenAI chat completions wire and cache_read_input_tokens on the Anthropic wire.
        /// </summary>
        public int CachedTokens { get; set; } = 0;

        /// <summary>
        /// Prompt tokens reported as written to the prompt cache on the Anthropic wire (0 reports none).
        /// </summary>
        public int CacheCreationTokens { get; set; } = 0;

        /// <summary>
        /// Load time the Ollama wire reports, in nanoseconds.
        /// </summary>
        public const long OllamaLoadNs = 5000000;

        /// <summary>
        /// Prompt evaluation time the Ollama wire reports, in nanoseconds.
        /// </summary>
        public const long OllamaPromptEvalNs = 7000000;

        /// <summary>
        /// Generation time the Ollama wire reports, in nanoseconds.
        /// </summary>
        public const long OllamaEvalNs = 20000000;

        /// <summary>
        /// Recorded requests in arrival order.
        /// </summary>
        public List<FakeModelRequest> Requests
        {
            get { return _Requests.ToList(); }
        }

        #endregion

        #region Private-Members

        private readonly HttpListener _Listener = new HttpListener();
        private readonly CancellationTokenSource _Stop = new CancellationTokenSource();
        private readonly ConcurrentQueue<FakeModelRequest> _Requests = new ConcurrentQueue<FakeModelRequest>();
        private readonly Task _Loop;
        private int _Failures = 0;

        #endregion

        #region Constructors-and-Factories

        private FakeModelServer(int port)
        {
            BaseUrl = "http://127.0.0.1:" + port;
            _Listener.Prefixes.Add(BaseUrl + "/");
            try { _Listener.Start(); }
            catch { _Listener.Close(); _Stop.Dispose(); throw; }
            _Loop = Task.Run(LoopAsync);
        }

        /// <summary>
        /// Start a server on a free loopback port.
        /// </summary>
        /// <returns>Server.</returns>
        public static FakeModelServer Start()
        {
            // The OS can reuse a probed port before HttpListener binds it, particularly during parallel suites.
            for (int attempt = 0; ; attempt++)
            {
                TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                int port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();
                try { return new FakeModelServer(port); }
                catch (SocketException error) when (attempt < 4 && error.SocketErrorCode == SocketError.AddressAlreadyInUse) { }
                catch (HttpListenerException error) when (attempt < 4 && (error.ErrorCode == 48 || error.ErrorCode == 98 || error.ErrorCode == 10048 || error.ErrorCode == 183)) { }
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Clear the recorded requests.
        /// </summary>
        public void ClearRequests()
        {
            while (_Requests.TryDequeue(out FakeModelRequest? _))
            {
            }
        }

        /// <summary>
        /// Deterministic embedding of a text (bag of hashed words, normalized).
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="dimensions">Dimensions.</param>
        /// <returns>Vector.</returns>
        public static float[] Embed(string text, int dimensions)
        {
            float[] vector = new float[dimensions];
            foreach (string word in (text ?? String.Empty).ToLowerInvariant().Split(new[] { ' ', '\n', '.', ',', '?', '!', ':', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int hash = 17;
                foreach (char c in word) hash = unchecked(hash * 31 + c);
                vector[Math.Abs(hash % dimensions)] += 1f;
            }
            double norm = Math.Sqrt(vector.Sum(v => v * v));
            if (norm > 0) for (int i = 0; i < vector.Length; i++) vector[i] = (float)(vector[i] / norm);
            else vector[0] = 1f;
            return vector;
        }

        /// <summary>
        /// Stop the server.
        /// </summary>
        /// <returns>Task.</returns>
        public async ValueTask DisposeAsync()
        {
            _Stop.Cancel();
            try
            {
                _Listener.Stop();
                _Listener.Close();
            }
            catch (ObjectDisposedException)
            {
                // Already closed.
            }
            try
            {
                await _Loop.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Stopped.
            }
            _Stop.Dispose();
        }

        #endregion

        #region Private-Methods

        private async Task LoopAsync()
        {
            while (!_Stop.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _Listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }
                _ = Task.Run(() => HandleAsync(ctx));
            }
        }

        private async Task HandleAsync(HttpListenerContext ctx)
        {
            try
            {
                string body;
                using (StreamReader reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8)) body = await reader.ReadToEndAsync().ConfigureAwait(false);
                Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string? key in ctx.Request.Headers.AllKeys) if (key != null) headers[key] = ctx.Request.Headers[key] ?? String.Empty;
                string path = ctx.Request.Url?.AbsolutePath ?? "/";
                _Requests.Enqueue(new FakeModelRequest { Method = ctx.Request.HttpMethod, Path = path, Query = ctx.Request.Url?.Query ?? String.Empty, Headers = headers, Body = body });
                string? prefix = String.IsNullOrEmpty(PathPrefix) ? null : PathPrefix.TrimEnd('/');
                if (prefix != null)
                {
                    if (!path.StartsWith(prefix + "/", StringComparison.Ordinal))
                    {
                        await WriteAsync(ctx, 404, "{\"error\":\"not found\"}").ConfigureAwait(false);
                        return;
                    }
                    path = path.Substring(prefix.Length);
                }
                JsonNode? json = String.IsNullOrWhiteSpace(body) || path == "/token" ? null : JsonNode.Parse(body);
                // Azure OpenAI deployment paths speak the OpenAI wire format.
                if (path.StartsWith("/openai/deployments/", StringComparison.Ordinal) && path.EndsWith("/chat/completions", StringComparison.Ordinal)) path = "/v1/chat/completions";
                // Azure OpenAI v1 API (PolyPrompt 4.0): /openai/v1/responses, /openai/v1/embeddings, /openai/v1/models.
                else if (path.StartsWith("/openai/v1/", StringComparison.Ordinal)) path = "/v1/" + path.Substring("/openai/v1/".Length);
                else if (path.StartsWith("/openai/deployments/", StringComparison.Ordinal) && path.EndsWith("/embeddings", StringComparison.Ordinal)) path = "/v1/embeddings";
                else if (path == "/openai/models") path = "/v1/models";
                bool gemini = path.StartsWith("/v1beta/models/", StringComparison.Ordinal) && (path.EndsWith(":generateContent", StringComparison.Ordinal) || path.EndsWith(":streamGenerateContent", StringComparison.Ordinal));
                bool isChat = path == "/api/chat" || path == "/v1/chat/completions" || path == "/v1/responses" || path == "/v1/messages" || gemini;
                bool isEmbed = path == "/api/embed" || path == "/v1/embeddings";
                if ((isChat || isEmbed) && FailNext > 0 && Interlocked.Increment(ref _Failures) <= FailNext)
                {
                    await WriteAsync(ctx, FailStatus, "{\"error\":\"scripted failure\"}").ConfigureAwait(false);
                    return;
                }
                if (gemini)
                {
                    await GeminiAsync(ctx, json, body, path.EndsWith(":streamGenerateContent", StringComparison.Ordinal)).ConfigureAwait(false);
                    return;
                }
                switch (path)
                {
                    case "/api/tags":
                        await WriteAsync(ctx, 200, "{\"models\":[{\"name\":\"fake-chat:latest\",\"model\":\"fake-chat:latest\",\"size\":1000},{\"name\":\"fake-embed:latest\",\"model\":\"fake-embed:latest\",\"size\":500}]}").ConfigureAwait(false);
                        return;
                    case "/v1/models":
                        await WriteAsync(ctx, 200, "{\"object\":\"list\",\"data\":[{\"id\":\"fake-chat\",\"object\":\"model\",\"owned_by\":\"test\"},{\"id\":\"fake-embed\",\"object\":\"model\",\"owned_by\":\"test\"}]}").ConfigureAwait(false);
                        return;
                    case "/api/embed":
                        await WriteAsync(ctx, 200, EmbedResponse(json, false)).ConfigureAwait(false);
                        return;
                    case "/v1/embeddings":
                        await WriteAsync(ctx, 200, EmbedResponse(json, true)).ConfigureAwait(false);
                        return;
                    case "/api/chat":
                        await ChatAsync(ctx, json, false).ConfigureAwait(false);
                        return;
                    case "/v1/chat/completions":
                        await ChatAsync(ctx, json, true).ConfigureAwait(false);
                        return;
                    case "/v1/responses":
                        await ResponsesAsync(ctx, json, body).ConfigureAwait(false);
                        return;
                    case "/v1/messages":
                        await AnthropicAsync(ctx, json, body).ConfigureAwait(false);
                        return;
                    case "/token":
                        // OAuth token exchange for service account credentials (token_uri pointed here).
                        await WriteAsync(ctx, 200, "{\"access_token\":\"" + AccessToken + "\",\"token_type\":\"Bearer\",\"expires_in\":3600}").ConfigureAwait(false);
                        return;
                    default:
                        await WriteAsync(ctx, 404, "{\"error\":\"not found\"}").ConfigureAwait(false);
                        return;
                }
            }
            catch (Exception)
            {
                try
                {
                    ctx.Response.Abort();
                }
                catch (Exception)
                {
                    // Gone.
                }
            }
        }

        private async Task ChatAsync(HttpListenerContext ctx, JsonNode? json, bool openAi)
        {
            bool stream = json?["stream"]?.GetValue<bool>() ?? false;
            string all = json?["messages"]?.ToJsonString() ?? String.Empty;
            bool nativeTools = json?["tools"] is JsonArray offered && offered.Count > 0;
            if (nativeTools && RejectNativeTools)
            {
                await WriteAsync(ctx, 400, "{\"error\":\"registry.ollama.ai/library/fake-chat does not support tools\"}").ConfigureAwait(false);
                return;
            }
            bool promptTools = all.Contains("You can call tools to look things up.", StringComparison.Ordinal);
            if ((nativeTools || promptTools) && ToolCalls.Count > 0 && ToolRound(json, promptTools) < ToolRounds)
            {
                await ToolCallsAsync(ctx, json, openAi, stream && nativeTools, promptTools && !nativeTools, all).ConfigureAwait(false);
                return;
            }
            string text = ReplyFor(all);
            int promptTokens = Math.Max(1, all.Length / 4);
            List<string> words = text.Split(' ').Select((w, i) => i == 0 ? w : " " + w).ToList();
            if (!stream)
            {
                if (openAi)
                    await WriteAsync(ctx, 200, "{\"id\":\"cmpl-1\",\"object\":\"chat.completion\",\"model\":\"fake-chat\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":" + JsonSerializer.Serialize(text) +
                        "},\"finish_reason\":\"" + FinishReason + "\"}],\"usage\":{\"prompt_tokens\":" + promptTokens + ",\"completion_tokens\":" + words.Count + ",\"total_tokens\":" + (promptTokens + words.Count) + OpenAiCacheDetail() + "}}").ConfigureAwait(false);
                else
                    await WriteAsync(ctx, 200, "{\"model\":\"fake-chat\",\"created_at\":\"2026-01-01T00:00:00Z\",\"message\":{\"role\":\"assistant\",\"content\":" + JsonSerializer.Serialize(text) +
                        "},\"done\":true,\"done_reason\":\"" + FinishReason + "\",\"prompt_eval_count\":" + promptTokens + ",\"eval_count\":" + words.Count + ",\"total_duration\":" + (OllamaLoadNs + OllamaPromptEvalNs + OllamaEvalNs) + ",\"load_duration\":" + OllamaLoadNs + ",\"prompt_eval_duration\":" + OllamaPromptEvalNs + ",\"eval_duration\":" + OllamaEvalNs + "}").ConfigureAwait(false);
                return;
            }
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = openAi ? "text/event-stream" : "application/x-ndjson";
            ctx.Response.SendChunked = true;
            Stream output = ctx.Response.OutputStream;
            if (FirstChunkDelayMs > 0) await Task.Delay(FirstChunkDelayMs).ConfigureAwait(false);
            foreach (string word in words)
            {
                string line = openAi
                    ? "data: {\"id\":\"cmpl-1\",\"object\":\"chat.completion.chunk\",\"model\":\"fake-chat\",\"choices\":[{\"index\":0,\"delta\":{\"content\":" + JsonSerializer.Serialize(word) + "},\"finish_reason\":null}]}\n\n"
                    : "{\"model\":\"fake-chat\",\"created_at\":\"2026-01-01T00:00:00Z\",\"message\":{\"role\":\"assistant\",\"content\":" + JsonSerializer.Serialize(word) + "},\"done\":false}\n";
                byte[] bytes = Encoding.UTF8.GetBytes(line);
                await output.WriteAsync(bytes).ConfigureAwait(false);
                await output.FlushAsync().ConfigureAwait(false);
                if (ChunkDelayMs > 0) await Task.Delay(ChunkDelayMs).ConfigureAwait(false);
            }
            string final = openAi
                ? "data: {\"id\":\"cmpl-1\",\"object\":\"chat.completion.chunk\",\"model\":\"fake-chat\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"" + FinishReason + "\"}],\"usage\":{\"prompt_tokens\":" + promptTokens + ",\"completion_tokens\":" + words.Count + ",\"total_tokens\":" + (promptTokens + words.Count) + OpenAiCacheDetail() + "}}\n\ndata: [DONE]\n\n"
                : "{\"model\":\"fake-chat\",\"created_at\":\"2026-01-01T00:00:00Z\",\"message\":{\"role\":\"assistant\",\"content\":\"\"},\"done\":true,\"done_reason\":\"" + FinishReason + "\",\"prompt_eval_count\":" + promptTokens + ",\"eval_count\":" + words.Count + ",\"total_duration\":" + (OllamaLoadNs + OllamaPromptEvalNs + OllamaEvalNs) + ",\"load_duration\":" + OllamaLoadNs + ",\"prompt_eval_duration\":" + OllamaPromptEvalNs + ",\"eval_duration\":" + OllamaEvalNs + "}\n";
            await output.WriteAsync(Encoding.UTF8.GetBytes(final)).ConfigureAwait(false);
            await output.FlushAsync().ConfigureAwait(false);
            output.Close();
        }

        private string ReplyFor(string all)
        {
            string? scripted = Script?.Invoke(all);
            if (scripted != null) return scripted;
            bool classifier = all.Contains("content safety classifier", StringComparison.Ordinal);
            if (!classifier && ScriptedReplies.TryDequeue(out string? queued)) return queued;
            return classifier && OutputModerationReply != null && all.Contains(ReplyText, StringComparison.Ordinal) ? OutputModerationReply
                : classifier ? ModerationReply
                : all.Contains("Write a short title", StringComparison.Ordinal) ? "Changing your profile photo"
                : all.Contains("standalone search query", StringComparison.Ordinal) ? "change profile photo"
                : ReplyText;
        }

        private async Task ResponsesAsync(HttpListenerContext ctx, JsonNode? json, string all)
        {
            // OpenAI Responses API (PolyPrompt 4.0 OpenAiClient and AzureOpenAiClient): output items, usage, and SSE events.
            bool stream = json?["stream"]?.GetValue<bool>() ?? false;
            bool nativeTools = json?["tools"] is JsonArray offered && offered.Count > 0;
            if (nativeTools && RejectNativeTools)
            {
                await WriteAsync(ctx, 400, "{\"error\":{\"message\":\"this model does not support tools\",\"type\":\"invalid_request_error\"}}").ConfigureAwait(false);
                return;
            }
            bool promptTools = all.Contains("You can call tools to look things up.", StringComparison.Ordinal);
            int round = 0;
            if (json?["input"] is JsonArray input)
            {
                string? previous = null;
                foreach (JsonNode? item in input)
                {
                    string? type = item?["type"]?.GetValue<string>();
                    if (type == "function_call" && previous != "function_call") round++;
                    if (promptTools && item?["role"]?.GetValue<string>() == "assistant" && (item["content"]?.ToJsonString() ?? String.Empty).Contains("tool_calls", StringComparison.Ordinal)) round++;
                    previous = type;
                }
            }
            int promptTokens = Math.Max(1, all.Length / 4);
            List<string> output = new List<string>();
            string text;
            if ((nativeTools || promptTools) && ToolCalls.Count > 0 && round < ToolRounds)
            {
                if (nativeTools)
                {
                    if (!String.IsNullOrEmpty(ToolPreamble)) output.Add(MessageItem(ToolPreamble));
                    output.AddRange(ToolCalls.Select((c, i) => "{\"type\":\"function_call\",\"id\":\"fc_" + round + "_" + i + "\",\"call_id\":\"call_" + round + "_" + i + "\",\"name\":" + JsonSerializer.Serialize(c.Name) +
                        ",\"arguments\":" + JsonSerializer.Serialize(c.ArgumentsJson) + ",\"status\":\"completed\"}"));
                    text = nativeTools ? (ToolPreamble ?? String.Empty) : String.Empty;
                }
                else
                {
                    text = "{\"tool_calls\":[" + String.Join(",", ToolCalls.Select(c => "{\"name\":" + JsonSerializer.Serialize(c.Name) + ",\"arguments\":" + c.ArgumentsJson + "}")) + "]}";
                    output.Add(MessageItem(text));
                }
            }
            else
            {
                text = ReplyFor(all);
                output.Add(MessageItem(text));
            }
            List<string> words = text.Length == 0 ? new List<string>() : text.Split(' ').Select((w, i) => i == 0 ? w : " " + w).ToList();
            string response = "{\"id\":\"resp_1\",\"object\":\"response\",\"status\":\"completed\",\"model\":\"fake-chat\",\"output\":[" + String.Join(",", output) + "],\"usage\":{\"input_tokens\":" + promptTokens +
                ",\"output_tokens\":" + Math.Max(1, words.Count) + ",\"total_tokens\":" + (promptTokens + Math.Max(1, words.Count)) + "}}";
            if (!stream)
            {
                await WriteAsync(ctx, 200, response).ConfigureAwait(false);
                return;
            }
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.SendChunked = true;
            Stream body = ctx.Response.OutputStream;
            if (FirstChunkDelayMs > 0) await Task.Delay(FirstChunkDelayMs).ConfigureAwait(false);
            await SseAsync(body, "response.created", "{\"type\":\"response.created\",\"response\":{\"id\":\"resp_1\",\"model\":\"fake-chat\",\"status\":\"in_progress\",\"output\":[]}}").ConfigureAwait(false);
            bool textItem = output.Count > 0 && output[0].Contains("\"type\":\"message\"", StringComparison.Ordinal);
            if (textItem)
                foreach (string word in words)
                {
                    await SseAsync(body, "response.output_text.delta", "{\"type\":\"response.output_text.delta\",\"item_id\":\"msg_1\",\"output_index\":0,\"content_index\":0,\"delta\":" + JsonSerializer.Serialize(word) + "}").ConfigureAwait(false);
                    if (ChunkDelayMs > 0) await Task.Delay(ChunkDelayMs).ConfigureAwait(false);
                }
            await SseAsync(body, "response.completed", "{\"type\":\"response.completed\",\"response\":" + response + "}").ConfigureAwait(false);
            body.Close();
        }

        private async Task GeminiAsync(HttpListenerContext ctx, JsonNode? json, string all, bool stream)
        {
            // Google generateContent (PolyPrompt 4.0 GeminiClient): candidates[0].content.parts with text or functionCall
            // parts, usageMetadata, and SSE chunks of the same shape for :streamGenerateContent?alt=sse.
            bool nativeTools = json?["tools"] is JsonArray offered && offered.Any(t => t?["functionDeclarations"] is JsonArray d && d.Count > 0);
            if (nativeTools && RejectNativeTools)
            {
                await WriteAsync(ctx, 400, "{\"error\":{\"code\":400,\"message\":\"Function calling is not enabled for this model\",\"status\":\"INVALID_ARGUMENT\"}}").ConfigureAwait(false);
                return;
            }
            bool promptTools = all.Contains("You can call tools to look things up.", StringComparison.Ordinal);
            int round = 0;
            if (json?["contents"] is JsonArray contents)
            {
                foreach (JsonNode? c in contents)
                {
                    if (c?["role"]?.GetValue<string>() != "model") continue;
                    string parts = c["parts"]?.ToJsonString() ?? String.Empty;
                    if (parts.Contains("\"functionCall\"", StringComparison.Ordinal) || (promptTools && parts.Contains("tool_calls", StringComparison.Ordinal))) round++;
                }
            }
            int promptTokens = Math.Max(1, all.Length / 4);
            List<string> parts1 = new List<string>();
            string text;
            bool toolRound = (nativeTools || promptTools) && ToolCalls.Count > 0 && round < ToolRounds;
            if (toolRound && nativeTools)
            {
                text = ToolPreamble ?? String.Empty;
                if (text.Length > 0) parts1.Add("{\"text\":" + JsonSerializer.Serialize(text) + "}");
                parts1.AddRange(ToolCalls.Select(c => "{\"functionCall\":{\"name\":" + JsonSerializer.Serialize(c.Name) + ",\"args\":" + c.ArgumentsJson + "}}"));
            }
            else
            {
                text = toolRound ? "{\"tool_calls\":[" + String.Join(",", ToolCalls.Select(c => "{\"name\":" + JsonSerializer.Serialize(c.Name) + ",\"arguments\":" + c.ArgumentsJson + "}")) + "]}" : ReplyFor(all);
                parts1.Add("{\"text\":" + JsonSerializer.Serialize(text) + "}");
            }
            List<string> words = text.Length == 0 ? new List<string>() : text.Split(' ').Select((w, i) => i == 0 ? w : " " + w).ToList();
            string usage = "\"usageMetadata\":{\"promptTokenCount\":" + promptTokens + ",\"candidatesTokenCount\":" + Math.Max(1, words.Count) + ",\"totalTokenCount\":" + (promptTokens + Math.Max(1, words.Count)) + "}";
            if (!stream)
            {
                await WriteAsync(ctx, 200, "{\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[" + String.Join(",", parts1) + "]},\"finishReason\":\"STOP\",\"index\":0}]," + usage +
                    ",\"modelVersion\":\"fake-chat\",\"responseId\":\"gem_1\"}").ConfigureAwait(false);
                return;
            }
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.SendChunked = true;
            Stream output = ctx.Response.OutputStream;
            if (FirstChunkDelayMs > 0) await Task.Delay(FirstChunkDelayMs).ConfigureAwait(false);
            List<string> chunks = new List<string>();
            if (toolRound && nativeTools)
            {
                if (text.Length > 0) chunks.Add("{\"text\":" + JsonSerializer.Serialize(text) + "}");
                chunks.Add(String.Join(",", parts1.Skip(text.Length > 0 ? 1 : 0)));
            }
            else chunks.AddRange(words.Select(w => "{\"text\":" + JsonSerializer.Serialize(w) + "}"));
            foreach (string chunk in chunks)
            {
                await output.WriteAsync(Encoding.UTF8.GetBytes("data: {\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[" + chunk + "]},\"index\":0}],\"modelVersion\":\"fake-chat\",\"responseId\":\"gem_1\"}\r\n\r\n")).ConfigureAwait(false);
                await output.FlushAsync().ConfigureAwait(false);
                if (ChunkDelayMs > 0) await Task.Delay(ChunkDelayMs).ConfigureAwait(false);
            }
            await output.WriteAsync(Encoding.UTF8.GetBytes("data: {\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"text\":\"\"}]},\"finishReason\":\"STOP\",\"index\":0}]," + usage +
                ",\"modelVersion\":\"fake-chat\",\"responseId\":\"gem_1\"}\r\n\r\n")).ConfigureAwait(false);
            await output.FlushAsync().ConfigureAwait(false);
            output.Close();
        }

        private async Task AnthropicAsync(HttpListenerContext ctx, JsonNode? json, string all)
        {
            // Anthropic Messages (PolyPrompt 4.0 AnthropicClient): content blocks (text, tool_use), stop_reason, usage, and the
            // message_start / content_block_* / message_delta / message_stop stream events.
            bool stream = json?["stream"]?.GetValue<bool>() ?? false;
            bool nativeTools = json?["tools"] is JsonArray offered && offered.Count > 0;
            if (nativeTools && RejectNativeTools)
            {
                await WriteAsync(ctx, 400, "{\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\",\"message\":\"this model does not support tools\"}}").ConfigureAwait(false);
                return;
            }
            bool promptTools = all.Contains("You can call tools to look things up.", StringComparison.Ordinal);
            int round = 0;
            if (json?["messages"] is JsonArray messages)
            {
                foreach (JsonNode? m in messages)
                {
                    if (m?["role"]?.GetValue<string>() != "assistant") continue;
                    string content = m["content"]?.ToJsonString() ?? String.Empty;
                    if (content.Contains("\"tool_use\"", StringComparison.Ordinal) || (promptTools && content.Contains("tool_calls", StringComparison.Ordinal))) round++;
                }
            }
            int promptTokens = Math.Max(1, all.Length / 4);
            bool toolRound = (nativeTools || promptTools) && ToolCalls.Count > 0 && round < ToolRounds;
            string text = toolRound && nativeTools ? ToolPreamble ?? String.Empty
                : toolRound ? "{\"tool_calls\":[" + String.Join(",", ToolCalls.Select(c => "{\"name\":" + JsonSerializer.Serialize(c.Name) + ",\"arguments\":" + c.ArgumentsJson + "}")) + "]}"
                : ReplyFor(all);
            List<string> words = text.Length == 0 ? new List<string>() : text.Split(' ').Select((w, i) => i == 0 ? w : " " + w).ToList();
            bool useTools = toolRound && nativeTools;
            string stop = useTools ? "tool_use" : "end_turn";
            if (!stream)
            {
                List<string> blocks = new List<string>();
                if (text.Length > 0) blocks.Add("{\"type\":\"text\",\"text\":" + JsonSerializer.Serialize(text) + "}");
                if (useTools) blocks.AddRange(ToolCalls.Select((c, i) => "{\"type\":\"tool_use\",\"id\":\"toolu_" + round + "_" + i + "\",\"name\":" + JsonSerializer.Serialize(c.Name) + ",\"input\":" + c.ArgumentsJson + "}"));
                await WriteAsync(ctx, 200, "{\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\",\"model\":\"fake-chat\",\"content\":[" + String.Join(",", blocks) + "],\"stop_reason\":\"" + stop +
                    "\",\"usage\":{\"input_tokens\":" + promptTokens + ",\"output_tokens\":" + Math.Max(1, words.Count) + AnthropicCacheDetail() + "}}").ConfigureAwait(false);
                return;
            }
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.SendChunked = true;
            Stream output = ctx.Response.OutputStream;
            if (FirstChunkDelayMs > 0) await Task.Delay(FirstChunkDelayMs).ConfigureAwait(false);
            await SseAsync(output, "message_start", "{\"type\":\"message_start\",\"message\":{\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\",\"model\":\"fake-chat\",\"content\":[],\"usage\":{\"input_tokens\":" + promptTokens + ",\"output_tokens\":0" + AnthropicCacheDetail() + "}}}").ConfigureAwait(false);
            int index = 0;
            if (words.Count > 0)
            {
                await SseAsync(output, "content_block_start", "{\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}").ConfigureAwait(false);
                foreach (string word in words)
                {
                    await SseAsync(output, "content_block_delta", "{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":" + JsonSerializer.Serialize(word) + "}}").ConfigureAwait(false);
                    if (ChunkDelayMs > 0) await Task.Delay(ChunkDelayMs).ConfigureAwait(false);
                }
                await SseAsync(output, "content_block_stop", "{\"type\":\"content_block_stop\",\"index\":0}").ConfigureAwait(false);
                index = 1;
            }
            if (useTools)
            {
                for (int i = 0; i < ToolCalls.Count; i++)
                {
                    FakeToolCall c = ToolCalls[i];
                    int at = index + i;
                    await SseAsync(output, "content_block_start", "{\"type\":\"content_block_start\",\"index\":" + at + ",\"content_block\":{\"type\":\"tool_use\",\"id\":\"toolu_" + round + "_" + i + "\",\"name\":" + JsonSerializer.Serialize(c.Name) + ",\"input\":{}}}").ConfigureAwait(false);
                    await SseAsync(output, "content_block_delta", "{\"type\":\"content_block_delta\",\"index\":" + at + ",\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":" + JsonSerializer.Serialize(c.ArgumentsJson) + "}}").ConfigureAwait(false);
                    await SseAsync(output, "content_block_stop", "{\"type\":\"content_block_stop\",\"index\":" + at + "}").ConfigureAwait(false);
                }
            }
            await SseAsync(output, "message_delta", "{\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"" + stop + "\",\"stop_sequence\":null},\"usage\":{\"output_tokens\":" + Math.Max(1, words.Count) + "}}").ConfigureAwait(false);
            await SseAsync(output, "message_stop", "{\"type\":\"message_stop\"}").ConfigureAwait(false);
            output.Close();
        }

        private string OpenAiCacheDetail()
        {
            return CachedTokens > 0 ? ",\"prompt_tokens_details\":{\"cached_tokens\":" + CachedTokens + "}" : String.Empty;
        }

        private string AnthropicCacheDetail()
        {
            return (CachedTokens > 0 ? ",\"cache_read_input_tokens\":" + CachedTokens : String.Empty) + (CacheCreationTokens > 0 ? ",\"cache_creation_input_tokens\":" + CacheCreationTokens : String.Empty);
        }

        private static string MessageItem(string text)
        {
            return "{\"type\":\"message\",\"id\":\"msg_1\",\"role\":\"assistant\",\"status\":\"completed\",\"content\":[{\"type\":\"output_text\",\"text\":" + JsonSerializer.Serialize(text) + ",\"annotations\":[]}]}";
        }

        private static async Task SseAsync(Stream output, string name, string data)
        {
            await output.WriteAsync(Encoding.UTF8.GetBytes("event: " + name + "\ndata: " + data + "\n\n")).ConfigureAwait(false);
            await output.FlushAsync().ConfigureAwait(false);
        }

        private static int ToolRound(JsonNode? json, bool prompt)
        {
            int rounds = 0;
            if (json?["messages"] is not JsonArray messages) return 0;
            foreach (JsonNode? m in messages)
            {
                if (m?["role"]?.GetValue<string>() != "assistant") continue;
                if (m["tool_calls"] is JsonArray calls && calls.Count > 0) rounds++;
                else if (prompt && (m["content"]?.ToJsonString() ?? String.Empty).Contains("tool_calls", StringComparison.Ordinal)) rounds++;
            }
            return rounds;
        }

        private async Task ToolCallsAsync(HttpListenerContext ctx, JsonNode? json, bool openAi, bool stream, bool prompt, string all)
        {
            int round = ToolRound(json, prompt);
            int promptTokens = Math.Max(1, all.Length / 4);
            if (prompt)
            {
                string protocol = "{\"tool_calls\":[" + String.Join(",", ToolCalls.Select(c => "{\"name\":" + JsonSerializer.Serialize(c.Name) + ",\"arguments\":" + c.ArgumentsJson + "}")) + "]}";
                if (openAi)
                    await WriteAsync(ctx, 200, "{\"id\":\"cmpl-t\",\"object\":\"chat.completion\",\"model\":\"fake-chat\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":" + JsonSerializer.Serialize(protocol) +
                        "},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":" + promptTokens + ",\"completion_tokens\":10,\"total_tokens\":" + (promptTokens + 10) + "}}").ConfigureAwait(false);
                else
                    await WriteAsync(ctx, 200, "{\"model\":\"fake-chat\",\"created_at\":\"2026-01-01T00:00:00Z\",\"message\":{\"role\":\"assistant\",\"content\":" + JsonSerializer.Serialize(protocol) +
                        "},\"done\":true,\"done_reason\":\"stop\",\"prompt_eval_count\":" + promptTokens + ",\"eval_count\":10,\"total_duration\":" + (OllamaLoadNs + OllamaPromptEvalNs + OllamaEvalNs) + ",\"load_duration\":" + OllamaLoadNs + ",\"prompt_eval_duration\":" + OllamaPromptEvalNs + ",\"eval_duration\":" + OllamaEvalNs + "}").ConfigureAwait(false);
                return;
            }
            string preamble = ToolPreamble ?? String.Empty;
            List<string> openAiCalls = ToolCalls.Select((c, i) => "{\"index\":" + i + ",\"id\":\"call_" + round + "_" + i + "\",\"type\":\"function\",\"function\":{\"name\":" + JsonSerializer.Serialize(c.Name) +
                ",\"arguments\":" + JsonSerializer.Serialize(c.ArgumentsJson) + "}}").ToList();
            List<string> ollamaCalls = ToolCalls.Select(c => "{\"function\":{\"name\":" + JsonSerializer.Serialize(c.Name) + ",\"arguments\":" + c.ArgumentsJson + "}}").ToList();
            string usage = "\"usage\":{\"prompt_tokens\":" + promptTokens + ",\"completion_tokens\":12,\"total_tokens\":" + (promptTokens + 12) + "}";
            if (!stream)
            {
                if (openAi)
                    await WriteAsync(ctx, 200, "{\"id\":\"cmpl-t\",\"object\":\"chat.completion\",\"model\":\"fake-chat\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":" +
                        (preamble.Length == 0 ? "null" : JsonSerializer.Serialize(preamble)) + ",\"tool_calls\":[" + String.Join(",", openAiCalls) + "]},\"finish_reason\":\"tool_calls\"}]," + usage + "}").ConfigureAwait(false);
                else
                    await WriteAsync(ctx, 200, "{\"model\":\"fake-chat\",\"created_at\":\"2026-01-01T00:00:00Z\",\"message\":{\"role\":\"assistant\",\"content\":" + JsonSerializer.Serialize(preamble) +
                        ",\"tool_calls\":[" + String.Join(",", ollamaCalls) + "]},\"done\":true,\"done_reason\":\"stop\",\"prompt_eval_count\":" + promptTokens + ",\"eval_count\":12,\"total_duration\":" + (OllamaLoadNs + OllamaPromptEvalNs + OllamaEvalNs) + ",\"load_duration\":" + OllamaLoadNs + ",\"prompt_eval_duration\":" + OllamaPromptEvalNs + ",\"eval_duration\":" + OllamaEvalNs + "}").ConfigureAwait(false);
                return;
            }
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = openAi ? "text/event-stream" : "application/x-ndjson";
            ctx.Response.SendChunked = true;
            Stream output = ctx.Response.OutputStream;
            StringBuilder body = new StringBuilder();
            if (openAi)
            {
                if (preamble.Length > 0) body.Append("data: {\"id\":\"cmpl-t\",\"object\":\"chat.completion.chunk\",\"model\":\"fake-chat\",\"choices\":[{\"index\":0,\"delta\":{\"content\":" + JsonSerializer.Serialize(preamble) + "},\"finish_reason\":null}]}\n\n");
                body.Append("data: {\"id\":\"cmpl-t\",\"object\":\"chat.completion.chunk\",\"model\":\"fake-chat\",\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[" + String.Join(",", openAiCalls) + "]},\"finish_reason\":null}]}\n\n");
                body.Append("data: {\"id\":\"cmpl-t\",\"object\":\"chat.completion.chunk\",\"model\":\"fake-chat\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"tool_calls\"}]," + usage + "}\n\ndata: [DONE]\n\n");
            }
            else
            {
                if (preamble.Length > 0) body.Append("{\"model\":\"fake-chat\",\"created_at\":\"2026-01-01T00:00:00Z\",\"message\":{\"role\":\"assistant\",\"content\":" + JsonSerializer.Serialize(preamble) + "},\"done\":false}\n");
                body.Append("{\"model\":\"fake-chat\",\"created_at\":\"2026-01-01T00:00:00Z\",\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[" + String.Join(",", ollamaCalls) + "]},\"done\":false}\n");
                body.Append("{\"model\":\"fake-chat\",\"created_at\":\"2026-01-01T00:00:00Z\",\"message\":{\"role\":\"assistant\",\"content\":\"\"},\"done\":true,\"done_reason\":\"stop\",\"prompt_eval_count\":" + promptTokens + ",\"eval_count\":12,\"total_duration\":" + (OllamaLoadNs + OllamaPromptEvalNs + OllamaEvalNs) + ",\"load_duration\":" + OllamaLoadNs + ",\"prompt_eval_duration\":" + OllamaPromptEvalNs + ",\"eval_duration\":" + OllamaEvalNs + "}\n");
            }
            await output.WriteAsync(Encoding.UTF8.GetBytes(body.ToString())).ConfigureAwait(false);
            await output.FlushAsync().ConfigureAwait(false);
            output.Close();
        }

        private string EmbedResponse(JsonNode? json, bool openAi)
        {
            List<string> inputs = new List<string>();
            JsonNode? input = json?["input"];
            if (input is JsonArray array) inputs.AddRange(array.Select(i => i?.GetValue<string>() ?? String.Empty));
            else if (input != null) inputs.Add(input.GetValue<string>());
            List<string> vectors = inputs.Select(i => "[" + String.Join(",", Embed(i, Dimensions).Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture))) + "]").ToList();
            if (!openAi) return "{\"model\":\"fake-embed\",\"embeddings\":[" + String.Join(",", vectors) + "],\"prompt_eval_count\":" + inputs.Sum(i => i.Length / 4 + 1) + "}";
            return "{\"object\":\"list\",\"model\":\"fake-embed\",\"data\":[" + String.Join(",", vectors.Select((v, i) => "{\"object\":\"embedding\",\"index\":" + i + ",\"embedding\":" + v + "}")) +
                "],\"usage\":{\"prompt_tokens\":" + inputs.Sum(i => i.Length / 4 + 1) + ",\"total_tokens\":" + inputs.Sum(i => i.Length / 4 + 1) + "}}";
        }

        private static async Task WriteAsync(HttpListenerContext ctx, int status, string body)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            ctx.Response.OutputStream.Close();
        }

        #endregion
    }
}
