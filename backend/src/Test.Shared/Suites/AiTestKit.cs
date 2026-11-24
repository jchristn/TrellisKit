namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared.Fakes;
    using Touchstone.Core;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Security;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Helpers of the AI suites: a fixture with features.ai on, a secrets key, and an in-process model server; endpoint
    /// and assistant creation; and an SSE reader.
    /// </summary>
    internal static class AiTestKit
    {
        internal static TestCaseDescriptor Case(string suiteId, string caseId, string name, Func<HttpFixture, FakeModelServer, CancellationToken, Task> body, Action<AppSettings>? settings = null, Action<PropertyConfiguration>? property = null)
        {
            return new TestCaseDescriptor(suiteId, caseId, name, async ct =>
            {
                HttpFixture fixture = await HttpFixture.StartAsync(ct, p =>
                {
                    p.Features.Ai = true;
                    property?.Invoke(p);
                }, s =>
                {
                    s.Secrets.EncryptionKey = TrellisKit.Core.Security.SecretBox.GenerateKey();
                    settings?.Invoke(s);
                }).ConfigureAwait(false);
                FakeModelServer model = FakeModelServer.Start();
                await using (fixture.ConfigureAwait(false))
                await using (model.ConfigureAwait(false))
                {
                    await body(fixture, model, ct).ConfigureAwait(false);
                }
            }, new[] { "http", "integration", "ai", TestEnvironment.Provider.ToString().ToLowerInvariant() });
        }

        internal static object OllamaEndpoint(FakeModelServer model, string name, string kind = "Completion", object? extra = null)
        {
            Dictionary<string, object?> body = new Dictionary<string, object?>
            {
                ["name"] = name,
                ["kind"] = kind,
                ["apiType"] = "Ollama",
                ["model"] = kind == "Embedding" ? "fake-embed" : "fake-chat",
                ["endpointUrl"] = model.BaseUrl,
                ["parameters"] = new { streaming = true, timeoutMs = 10000 },
                ["limits"] = new { maxRetries = 0 },
                ["healthCheck"] = new { enabled = false }
            };
            if (extra != null)
                foreach (JsonProperty p in JsonSerializer.SerializeToElement(extra).EnumerateObject()) body[p.Name] = p.Value;
            return body;
        }

        internal static async Task<string> EndpointAsync(HttpFixture f, object body, CancellationToken ct)
        {
            JsonElement created = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints", HttpFixture.AdminToken, body, HttpStatusCode.Created, ct).ConfigureAwait(false);
            return Api.Str(created, "id");
        }

        internal static async Task<string> AssistantAsync(HttpFixture f, string chatEndpointId, CancellationToken ct, object? extra = null, string name = "Helper")
        {
            Dictionary<string, object?> body = new Dictionary<string, object?>
            {
                ["name"] = name + " " + Guid.NewGuid().ToString("N").Substring(0, 6),
                ["systemPrompt"] = "You are the helper for {name}. Be brief.",
                ["models"] = new { chat = chatEndpointId },
                ["audience"] = "Members",
                ["starterPrompts"] = new[] { "How do I change my profile photo?" }
            };
            if (extra != null)
                foreach (JsonProperty p in JsonSerializer.SerializeToElement(extra).EnumerateObject()) body[p.Name] = p.Value;
            JsonElement created = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/assistants", HttpFixture.AdminToken, body, HttpStatusCode.Created, ct).ConfigureAwait(false);
            return Api.Str(created, "id");
        }

        internal static async Task<string> ReadyAsync(HttpFixture f, FakeModelServer model, CancellationToken ct, object? assistantExtra = null)
        {
            string chat = await EndpointAsync(f, OllamaEndpoint(model, "Chat " + Guid.NewGuid().ToString("N").Substring(0, 6)), ct).ConfigureAwait(false);
            return await AssistantAsync(f, chat, ct, assistantExtra).ConfigureAwait(false);
        }

        internal static async Task<List<SseEvent>> SseAsync(HttpFixture f, string path, string? bearer, object body, CancellationToken ct, Dictionary<string, string>? headers = null)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, path))
            {
                if (bearer != null) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer);
                request.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
                request.Headers.TryAddWithoutValidation("X-Client", "web");
                if (headers != null) foreach (KeyValuePair<string, string> h in headers) request.Headers.TryAddWithoutValidation(h.Key, h.Value);
                f.Decorate(request);
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await f.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    string text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, response.StatusCode, "SSE status (" + text + ")");
                    Check.True((response.Content.Headers.ContentType?.MediaType ?? String.Empty) == "text/event-stream", "SSE content type");
                    return Parse(text);
                }
            }
        }

        internal static List<SseEvent> Parse(string text)
        {
            List<SseEvent> events = new List<SseEvent>();
            foreach (string block in text.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
            {
                string? name = null;
                StringBuilder data = new StringBuilder();
                foreach (string line in block.Split('\n'))
                {
                    if (line.StartsWith("event: ", StringComparison.Ordinal)) name = line.Substring(7);
                    else if (line.StartsWith("data: ", StringComparison.Ordinal)) data.Append(line.Substring(6));
                }
                if (name == null) continue;
                using (JsonDocument doc = JsonDocument.Parse(data.Length == 0 ? "{}" : data.ToString()))
                    events.Add(new SseEvent { Name = name, Data = doc.RootElement.Clone() });
            }
            return events;
        }

        internal static async Task<JsonElement> StartAsync(HttpFixture f, string assistantId, string bearer, string message, CancellationToken ct)
        {
            return await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/assistants/" + assistantId + "/conversations", bearer, new { message }, HttpStatusCode.Created, ct).ConfigureAwait(false);
        }
    }
}
