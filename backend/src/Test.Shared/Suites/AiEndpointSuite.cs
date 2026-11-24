namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Test.Shared.Fakes;
    using Touchstone.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Security;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Model endpoints (docs/AI_CONTRACT.md sections 1 to 4): the secret protector, the redactor, the feature gate,
    /// validation per apiType and authType, write-only secrets on every read path, each auth type on the wire against
    /// the in-process model server (including streaming and model listing), test, health, model listing, duplicate,
    /// reveal rules, and audit.
    /// </summary>
    public static class AiEndpointSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "AiEndpoints";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "AI model endpoints (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Unit(Id, "Secrets.SecretBoxWithAssociatedDataAndRotation", "Secrets round trip through the engine SecretBox; another row's associated data fails; previous keys decrypt; rotation is detected; the envelope holds SecretBox parts", ct =>
                {
                    string oldKey = SecretBox.GenerateKey();
                    string newKey = SecretBox.GenerateKey();
                    SecretSettings settings = new SecretSettings { EncryptionKey = oldKey };
                    SecretBoxProtector protector = new SecretBoxProtector(() => SecretBoxProvider.For(settings), () => SecretBox.IsValidKey(settings.EncryptionKey));
                    string ad = SecretBoxProtector.EndpointAssociatedData("app_a", "mep_1", "authValue");
                    string stored = protector.Protect("sk-secret-value", ad);
                    Check.True(!stored.Contains("sk-secret-value", StringComparison.Ordinal), "Ciphertext hides the value");
                    Check.Equal("sk-secret-value", protector.Unprotect(stored, ad), "Round trip");
                    Check.Throws<CryptographicException>(() => protector.Unprotect(stored, SecretBoxProtector.EndpointAssociatedData("app_b", "mep_1", "authValue")), "Other property");
                    Check.Throws<CryptographicException>(() => protector.Unprotect(stored, SecretBoxProtector.EndpointAssociatedData("app_a", "mep_2", "authValue")), "Other endpoint");
                    Check.Equal(new SecretBox(oldKey).KeyId, protector.CurrentKeyId, "Key id is the SecretBox key id");
                    SecretCiphertext parts = SecretBoxProtector.Unpack(stored)!;
                    Check.Equal("sk-secret-value", new SecretBox(oldKey).Decrypt(parts.Ciphertext, parts.Nonce, parts.KeyId, ad), "SecretBox decrypts the envelope directly");
                    settings.EncryptionKey = newKey;
                    Check.Throws<CryptographicException>(() => protector.Unprotect(stored, ad), "Unknown key without previous keys");
                    settings.PreviousEncryptionKeys = oldKey;
                    Check.Equal("sk-secret-value", protector.Unprotect(stored, ad), "Previous key decrypts");
                    Check.True(protector.NeedsRotation(stored), "Rotation detected");
                    Check.True(!protector.NeedsRotation(protector.Protect("x", ad)), "Current key needs no rotation");
                    return Task.CompletedTask;
                }),

                SuiteCases.Unit(Id, "Redactor.RemovesEverySecretForm", "Exact secrets, the query parameter value, and configured header values are redacted", ct =>
                {
                    EndpointCredentials credentials = new EndpointCredentials
                    {
                        AuthType = ModelAuthTypeEnum.QueryParameter,
                        QueryParameter = "key",
                        AuthValue = "q-secret-123",
                        ExtraHeaders = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("X-Team", "team-secret") }
                    };
                    AiSecretRedactor redactor = new AiSecretRedactor(credentials);
                    string text = redactor.Redact("GET https://h/v1?a=1&key=other-value failed; token q-secret-123; X-Team: team-secret, \"X-Team\":\"zz\"")!;
                    Check.True(!text.Contains("q-secret-123", StringComparison.Ordinal), "Secret removed");
                    Check.True(!text.Contains("other-value", StringComparison.Ordinal), "Query value removed");
                    Check.True(!text.Contains("team-secret", StringComparison.Ordinal) && !text.Contains("zz", StringComparison.Ordinal), "Header values removed");
                    return Task.CompletedTask;
                }),

                SuiteCases.Http(Id, "Gate.OffMeansNotAvailable", "Without features.ai every admin and member AI route is 404 NotAvailable and features.ai is false", async (f, ct) =>
                {
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints/enumerate", HttpFixture.AdminToken, new { }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/ai/assistants/enumerate", HttpFixture.AdminToken, new { }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/assistants/enumerate", member.AccessToken, new { }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
                    JsonElement property = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(property.GetProperty("features").GetProperty("ai").ValueKind == JsonValueKind.False, "features.ai false");
                }),

                AiTestKit.Case(Id, "Endpoints.CrudAndWriteOnlySecrets", "Create, read, list, update, and duplicate never return secrets; the database holds ciphertext; omitted secrets are kept; headers keep values by name; audits name fields only", async (f, model, ct) =>
                {
                    JsonElement property = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(property.GetProperty("features").GetProperty("ai").GetBoolean(), "features.ai true");
                    object body = AiTestKit.OllamaEndpoint(model, "Secret chat", "Completion", new
                    {
                        authType = "Header",
                        authHeaderName = "X-API-Key",
                        authValue = "hdr-secret-value",
                        extraHeaders = new[] { new { name = "X-Team", value = "team-secret-value" } },
                        pricing = new { inputPerMillionTokens = 1.5, outputPerMillionTokens = 6.0 },
                        labels = new[] { "local" }
                    });
                    ApiResult created = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints", HttpFixture.AdminToken, body, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Created, created.Status, "Created (" + created.Text + ")");
                    string id = Api.Str(created.Body, "id");
                    Check.True(created.Body.GetProperty("hasAuthValue").GetBoolean(), "hasAuthValue");
                    Check.True(created.Body.GetProperty("extraHeaders")[0].GetProperty("hasValue").GetBoolean(), "Header hasValue");
                    foreach (string path in new[] { "/api/v1/admin/ai/endpoints/" + id, "/api/v1/admin/ai/endpoints" })
                    {
                        ApiResult read = await Api.SendAsync(f, HttpMethod.Get, path, HttpFixture.AdminToken, null, ct).ConfigureAwait(false);
                        Check.True(!read.Text.Contains("hdr-secret-value", StringComparison.Ordinal) && !read.Text.Contains("team-secret-value", StringComparison.Ordinal), "No secret on " + path);
                    }
                    Check.True(!created.Text.Contains("hdr-secret-value", StringComparison.Ordinal), "No secret on create");
                    DataTable row = await f.Raw.ExecuteQueryAsync("SELECT authvaluesecret FROM aiendpoints WHERE id = '" + id + "';", false, ct).ConfigureAwait(false);
                    string stored = RowReader.GetString(row.Rows[0], "authvaluesecret");
                    Check.True(stored.StartsWith("v1:", StringComparison.Ordinal) && !stored.Contains("hdr-secret-value", StringComparison.Ordinal), "Ciphertext at rest");

                    JsonElement renamed = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/ai/endpoints/" + id, HttpFixture.AdminToken,
                        new { description = "renamed", extraHeaders = new[] { new { name = "x-team" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(renamed.GetProperty("hasAuthValue").GetBoolean(), "Omitted authValue kept");
                    Check.True(renamed.GetProperty("extraHeaders")[0].GetProperty("hasValue").GetBoolean(), "Header without value keeps the stored value");
                    model.ClearRequests();
                    JsonElement test = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints/" + id + "/test", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(test.GetProperty("success").GetBoolean(), "Test succeeded (" + test + ")");
                    FakeModelRequest sent = model.Requests.Last(r => r.Path == "/api/chat");
                    Check.Equal("hdr-secret-value", sent.Headers["X-API-Key"], "Auth header on the wire");
                    Check.Equal("team-secret-value", sent.Headers["X-Team"], "Kept header value on the wire");

                    JsonElement cleared = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/ai/endpoints/" + id, HttpFixture.AdminToken, new { authValue = "" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!cleared.GetProperty("hasAuthValue").GetBoolean(), "Empty clears");
                    JsonElement copy = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints/" + id + "/duplicate", HttpFixture.AdminToken, new { name = "Copy", includeSecrets = false }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Check.True(!copy.GetProperty("enabled").GetBoolean(), "Copy disabled");
                    Check.True(!copy.GetProperty("extraHeaders")[0].GetProperty("hasValue").GetBoolean(), "Copy without secrets");
                    Check.Equal("local", copy.GetProperty("labels")[0].GetString(), "Labels copied");

                    DataTable audit = await f.Raw.ExecuteQueryAsync("SELECT action, summary FROM auditrecords WHERE targetid = '" + id + "' ORDER BY createdutc;", false, ct).ConfigureAwait(false);
                    List<string> actions = audit.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "action")).ToList();
                    Check.True(actions.Contains("ai.endpoint.create") && actions.Contains("ai.endpoint.update") && actions.Contains("ai.endpoint.test"), "Audited (" + String.Join(",", actions) + ")");
                    Check.True(audit.Rows.Cast<DataRow>().All(r => !(RowReader.GetNullableString(r, "summary") ?? String.Empty).Contains("secret-value", StringComparison.Ordinal)), "No secret in audit");
                    ApiResult history = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/request-history?maxResults=100", HttpFixture.AdminToken, null, ct).ConfigureAwait(false);
                    Check.True(!history.Text.Contains("hdr-secret-value", StringComparison.Ordinal), "No secret in request history");
                }),

                AiTestKit.Case(Id, "Secrets.RotationWorker", "After a secrets key rotation the engine re-encryption pass rewrites endpoint credentials and header values with the current key, and the endpoint still authenticates", async (f, model, ct) =>
                {
                    object body = AiTestKit.OllamaEndpoint(model, "Rotating chat", "Completion", new
                    {
                        authType = "Header",
                        authHeaderName = "X-API-Key",
                        authValue = "rotating-secret-value",
                        extraHeaders = new[] { new { name = "X-Team", value = "rotating-team-value" } }
                    });
                    string id = await AiTestKit.EndpointAsync(f, body, ct).ConfigureAwait(false);
                    string oldKey = f.Settings.Secrets.EncryptionKey;
                    string newKey = SecretBox.GenerateKey();
                    f.Settings.Secrets.EncryptionKey = newKey;
                    f.Settings.Secrets.PreviousEncryptionKeys = oldKey;
                    Check.True(await f.Services.SecretRotation.RemainingAsync(ct).ConfigureAwait(false) >= 2, "Both endpoint secrets use the previous key");
                    for (int i = 0; i < 10 && await f.Services.SecretRotation.RemainingAsync(ct).ConfigureAwait(false) > 0; i++) await f.Services.SecretRotation.RunPassAsync(ct).ConfigureAwait(false);
                    Check.Equal(0L, await f.Services.SecretRotation.RemainingAsync(ct).ConfigureAwait(false), "None remaining");
                    string current = new SecretBox(newKey).KeyId;
                    DataTable auth = await f.Raw.ExecuteQueryAsync("SELECT authvaluesecret FROM aiendpoints WHERE id = '" + id + "';", false, ct).ConfigureAwait(false);
                    DataTable header = await f.Raw.ExecuteQueryAsync("SELECT valuesecret FROM aiendpointheaders WHERE endpointid = '" + id + "';", false, ct).ConfigureAwait(false);
                    Check.Equal(current, SecretBoxProtector.KeyIdOf(auth.Rows[0]["authvaluesecret"].ToString()), "Credential uses the current key");
                    Check.Equal(current, SecretBoxProtector.KeyIdOf(header.Rows[0]["valuesecret"].ToString()), "Header uses the current key");
                    f.Settings.Secrets.PreviousEncryptionKeys = String.Empty;
                    TrellisKit.Core.Models.ModelEndpoint endpoint = (await f.Database.Ai.Endpoints.ReadAsync(id, ct).ConfigureAwait(false))!;
                    EndpointCredentials credentials = f.Services.Ai.Factory.Credentials(endpoint);
                    Check.Equal("rotating-secret-value", credentials.AuthValue, "Decrypts with the current key alone");
                    Check.Equal("rotating-team-value", credentials.ExtraHeaders[0].Value, "Header decrypts with the current key alone");
                }),

                AiTestKit.Case(Id, "Endpoints.Validation", "Unsupported kind, auth type, URL, header names, and an unknown endpoint are rejected with the contract errors", async (f, model, ct) =>
                {
                    ApiResult kind = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints", HttpFixture.AdminToken, new { name = "x", kind = "Completion", apiType = "Tei", model = "m", endpointUrl = model.BaseUrl }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, kind.Status, "Tei completion");
                    Check.True(kind.Text.Contains("kind", StringComparison.Ordinal), "fieldErrors.kind");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints", HttpFixture.AdminToken, new { name = "x", kind = "Completion", apiType = "XAI", model = "m", authType = "Header", authHeaderName = "X-Key", authValue = "v" }, HttpStatusCode.BadRequest, "AuthTypeNotSupported", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints", HttpFixture.AdminToken, new { name = "x", kind = "Completion", apiType = "Ollama", model = "m", endpointUrl = model.BaseUrl + "?key=1" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints", HttpFixture.AdminToken, new { name = "x", kind = "Completion", apiType = "Ollama", model = "m", endpointUrl = model.BaseUrl, authType = "Header", authHeaderName = "Host", authValue = "v" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints", HttpFixture.AdminToken, new { name = "x", kind = "Completion", apiType = "Ollama", model = "m" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    string first = await AiTestKit.EndpointAsync(f, AiTestKit.OllamaEndpoint(model, "Same"), ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints", HttpFixture.AdminToken, AiTestKit.OllamaEndpoint(model, "same"), HttpStatusCode.Conflict, "Conflict", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/ai/endpoints/mep_missing", HttpFixture.AdminToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                    string assistant = await AiTestKit.AssistantAsync(f, first, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/admin/ai/endpoints/" + first, HttpFixture.AdminToken, null, HttpStatusCode.Conflict, "EndpointInUse", ct).ConfigureAwait(false);
                    JsonElement usage = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints/" + first + "/usage/enumerate", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(assistant, usage.GetProperty("objects")[0].GetProperty("assistantId").GetString(), "usedBy enumerate");
                    JsonElement validated = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints/validate", HttpFixture.AdminToken, AiTestKit.OllamaEndpoint(model, "Draft"), HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(validated.GetProperty("success").GetBoolean(), "Validate runs a test without storing");
                }),

                AiTestKit.Case(Id, "Providers.CredentialsAndSettings", "AzureOpenAI, Bedrock, and VertexAI are built through PolyPrompt: provider settings are validated per type, Azure sends api-key or an Entra bearer to the v1 responses route with the deployment as the model, Bedrock requests are SigV4 signed, and Vertex AI exchanges the service account for a bearer token", async (f, model, ct) =>
                {
                    object Endpoint(string name, string apiType, string modelName, object auth, object? providerSettings)
                    {
                        Dictionary<string, object?> body = new Dictionary<string, object?>
                        {
                            ["name"] = name, ["kind"] = "Completion", ["apiType"] = apiType, ["model"] = modelName, ["endpointUrl"] = model.BaseUrl,
                            ["parameters"] = new { streaming = false, timeoutMs = 10000 }, ["limits"] = new { maxRetries = 0 }, ["healthCheck"] = new { enabled = false }
                        };
                        foreach (JsonProperty p in JsonSerializer.SerializeToElement(auth).EnumerateObject()) body[p.Name] = p.Value;
                        if (providerSettings != null) body["providerSettings"] = providerSettings;
                        return body;
                    }

                    async Task<string> FieldErrorAsync(object body)
                    {
                        ApiResult result = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints", HttpFixture.AdminToken, body, ct).ConfigureAwait(false);
                        Check.Equal(HttpStatusCode.BadRequest, result.Status, "Rejected: " + result.Text);
                        return result.Text;
                    }

                    // Validation per provider (docs/AI_CONTRACT.md sections 1.2 and 3.3).
                    Check.True((await FieldErrorAsync(Endpoint("az-missing", "AzureOpenAI", "gpt", new { authType = "ApiKey", authValue = "k" }, null)).ConfigureAwait(false)).Contains("providerSettings.deployment", StringComparison.Ordinal), "Azure needs a deployment");
                    Check.True((await FieldErrorAsync(Endpoint("az-region", "AzureOpenAI", "gpt", new { authType = "ApiKey", authValue = "k" }, new { deployment = "d", region = "us-east-1" })).ConfigureAwait(false)).Contains("providerSettings.region", StringComparison.Ordinal), "Azure does not use region");
                    Check.True((await FieldErrorAsync(Endpoint("br-key", "Bedrock", "m", new { authType = "ApiKey", authValue = "secret" }, new { region = "us-east-1" })).ConfigureAwait(false)).Contains("providerSettings.accessKeyId", StringComparison.Ordinal), "Bedrock ApiKey needs accessKeyId");
                    Check.True((await FieldErrorAsync(Endpoint("br-none", "Bedrock", "m", new { authType = "None" }, new { region = "us-east-1", accessKeyId = "AKID" })).ConfigureAwait(false)).Contains("providerSettings.accessKeyId", StringComparison.Ordinal), "Bedrock None takes no accessKeyId");
                    Check.True((await FieldErrorAsync(Endpoint("vx-region", "VertexAI", "m", new { authType = "None" }, new { project = "example-project" })).ConfigureAwait(false)).Contains("providerSettings.region", StringComparison.Ordinal), "Vertex AI needs a region");
                    Check.True((await FieldErrorAsync(Endpoint("vx-json", "VertexAI", "m", new { authType = "ApiKey", authValue = "not json" }, new { project = "example-project", region = "us-central1" })).ConfigureAwait(false)).Contains("authValue", StringComparison.Ordinal), "Vertex AI ApiKey takes service account JSON");
                    Check.True((await FieldErrorAsync(Endpoint("ol-region", "Ollama", "m", new { authType = "None" }, new { region = "us-east-1" })).ConfigureAwait(false)).Contains("providerSettings.region", StringComparison.Ordinal), "Other providers take no provider settings");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints", HttpFixture.AdminToken, Endpoint("br-header", "Bedrock", "m", new { authType = "Header", authHeaderName = "X-Key", authValue = "v" }, new { region = "us-east-1" }), HttpStatusCode.BadRequest, "AuthTypeNotSupported", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints", HttpFixture.AdminToken, Endpoint("vx-query", "VertexAI", "m", new { authType = "QueryParameter", authQueryParameter = "k", authValue = "v" }, new { project = "p1", region = "us-central1" }), HttpStatusCode.BadRequest, "AuthTypeNotSupported", ct).ConfigureAwait(false);

                    // Azure OpenAI with an api-key (PolyPrompt 4.0 v1 API): /openai/v1/responses with the deployment as the model, no api-version, and the api-key header.
                    string azure = await AiTestKit.EndpointAsync(f, Endpoint("Azure key", "AzureOpenAI", "gpt-informational", new { authType = "ApiKey", authValue = "az-key-123" }, new { deployment = "chat-dep", apiVersion = "2024-10-21" }), ct).ConfigureAwait(false);
                    JsonElement read = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/ai/endpoints/" + azure, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("chat-dep", Api.Str(read.GetProperty("providerSettings"), "deployment"), "Deployment stored");
                    Check.Equal("2024-10-21", Api.Str(read.GetProperty("providerSettings"), "apiVersion"), "API version stored");
                    model.ClearRequests();
                    JsonElement azureTest = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints/" + azure + "/test", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(azureTest.GetProperty("success").GetBoolean(), "Azure test (" + azureTest + ")");
                    FakeModelRequest? azureCall = model.Requests.FirstOrDefault(r => r.Path == "/openai/v1/responses");
                    Check.True(azureCall != null, "Azure v1 responses path (" + String.Join(", ", model.Requests.Select(r => r.Method + " " + r.Path + r.Query)) + ")");
                    Check.Equal("az-key-123", azureCall!.Headers.TryGetValue("api-key", out string? apiKey) ? apiKey : String.Empty, "Azure api-key header");
                    Check.True(!azureCall.Query.Contains("api-version", StringComparison.Ordinal), "No api-version on the v1 API (" + azureCall.Query + ")");
                    Check.True(azureCall.Body.Contains("\"model\":\"chat-dep\"", StringComparison.Ordinal), "The deployment is sent as the model");
                    JsonElement updated = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/ai/endpoints/" + azure, HttpFixture.AdminToken, new { providerSettings = new { deployment = "chat-dep-2" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("chat-dep-2", Api.Str(updated.GetProperty("providerSettings"), "deployment"), "Partial update replaces the deployment");
                    Check.Equal("2024-10-21", Api.Str(updated.GetProperty("providerSettings"), "apiVersion"), "Omitted provider settings stay");

                    // Azure OpenAI with an Entra token.
                    string entra = await AiTestKit.EndpointAsync(f, Endpoint("Azure entra", "AzureOpenAI", "gpt", new { authType = "Bearer", authValue = "entra-token" }, new { deployment = "chat-dep" }), ct).ConfigureAwait(false);
                    model.ClearRequests();
                    JsonElement entraTest = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints/" + entra + "/test", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(entraTest.GetProperty("success").GetBoolean(), "Azure Entra test (" + entraTest + ")");
                    FakeModelRequest? entraCall = model.Requests.FirstOrDefault(r => r.Path == "/openai/v1/responses");
                    Check.True(entraCall != null, "Azure Entra v1 path");
                    Check.Equal("Bearer entra-token", entraCall!.Headers.TryGetValue("Authorization", out string? bearer) ? bearer : String.Empty, "Entra bearer");
                    Check.True(!entraCall.Headers.ContainsKey("api-key"), "No api-key with Entra");

                    // Bedrock with static keys: requests are SigV4 signed for the region and the bedrock service.
                    string bedrock = await AiTestKit.EndpointAsync(f, Endpoint("Bedrock keys", "Bedrock", "example.model-v1", new { authType = "ApiKey", authValue = "bedrock-secret" }, new { region = "us-east-1", accessKeyId = "AKIDEXAMPLE" }), ct).ConfigureAwait(false);
                    model.ClearRequests();
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints/" + bedrock + "/test", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    FakeModelRequest? signed = model.Requests.FirstOrDefault(r => r.Headers.TryGetValue("Authorization", out string? a) && a.StartsWith("AWS4-HMAC-SHA256", StringComparison.Ordinal));
                    Check.True(signed != null, "A SigV4-signed Bedrock request reached the endpoint (" + String.Join(", ", model.Requests.Select(r => r.Method + " " + r.Path)) + ")");
                    Check.True(signed!.Headers["Authorization"].Contains("Credential=AKIDEXAMPLE/", StringComparison.Ordinal) && signed.Headers["Authorization"].Contains("/us-east-1/bedrock/aws4_request", StringComparison.Ordinal), "Signed with the access key for us-east-1 (" + signed.Headers["Authorization"] + ")");
                    Check.True(!signed.Headers["Authorization"].Contains("bedrock-secret", StringComparison.Ordinal) && !signed.Body.Contains("bedrock-secret", StringComparison.Ordinal), "The secret key never travels");

                    // Vertex AI with a service account: the key is exchanged at its token_uri for a bearer token.
                    string pem;
                    using (RSA rsa = RSA.Create(2048)) pem = rsa.ExportPkcs8PrivateKeyPem();
                    string serviceAccount = JsonSerializer.Serialize(new Dictionary<string, string>
                    {
                        ["type"] = "service_account", ["project_id"] = "example-project", ["private_key_id"] = "k1", ["private_key"] = pem,
                        ["client_email"] = "assistant@example-project.iam.example.test", ["token_uri"] = model.BaseUrl + "/token"
                    });
                    model.AccessToken = "vertex-token-123";
                    string vertex = await AiTestKit.EndpointAsync(f, Endpoint("Vertex sa", "VertexAI", "example-model", new { authType = "ApiKey", authValue = serviceAccount }, new { project = "example-project", region = "us-central1" }), ct).ConfigureAwait(false);
                    model.ClearRequests();
                    JsonElement vertexTest = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints/" + vertex + "/test", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(model.Requests.Any(r => r.Path == "/token"), "The service account was exchanged at its token_uri (" + String.Join(", ", model.Requests.Select(r => r.Method + " " + r.Path)) + ")");
                    FakeModelRequest? vertexCall = model.Requests.FirstOrDefault(r => r.Path.Contains("/projects/example-project/locations/us-central1/", StringComparison.Ordinal));
                    Check.True(vertexCall != null, "Vertex AI call to the project and region path (" + vertexTest.GetRawText() + " " + String.Join(", ", model.Requests.Select(r => r.Method + " " + r.Path + " " + (r.Headers.TryGetValue("Authorization", out string? h) ? h : "-"))) + ")");
                    Check.Equal("Bearer vertex-token-123", vertexCall!.Headers.TryGetValue("Authorization", out string? vtoken) ? vtoken : String.Empty, "Vertex AI bearer from the exchange");
                    JsonElement vertexRead = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/ai/endpoints/" + vertex, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!vertexRead.GetRawText().Contains("PRIVATE KEY", StringComparison.Ordinal), "The service account key is write-only");
                }),

                AiTestKit.Case(Id, "Test.FinishReasonAndBudget", "A completion test returns the text, finish reason, token counts, and effective cap (default 1024, at most 8192), and explains an empty or truncated answer", async (f, model, ct) =>
                {
                    foreach (bool streaming in new[] { false, true })
                    {
                        string id = await AiTestKit.EndpointAsync(f, AiTestKit.OllamaEndpoint(model, "Budget " + streaming, "Completion", new { parameters = new { streaming, timeoutMs = 10000 } }), ct).ConfigureAwait(false);
                        model.ReplyText = "Hello there friend";
                        model.FinishReason = "stop";
                        JsonElement ok = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints/" + id + "/test", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.True(ok.GetProperty("success").GetBoolean(), "Succeeded (" + ok + ")");
                        Check.Equal("Hello there friend", Api.Str(ok, "output"), "Output text returned");
                        // PolyPrompt reports the Ollama finish reason on streams; its non-streaming Ollama chat has none (the cap then counts tokens).
                        if (streaming || ok.GetProperty("finishReason").ValueKind == JsonValueKind.String) Check.Equal("stop", Api.Str(ok, "finishReason"), "Finish reason returned");
                        Check.Equal(1024, ok.GetProperty("maxTokens").GetInt32(), "Default cap");
                        Check.True(!ok.GetProperty("truncated").GetBoolean() && ok.GetProperty("hint").ValueKind == JsonValueKind.Null, "No hint for a complete answer");
                        Check.True(ok.GetProperty("usage").GetProperty("completionTokens").GetInt32() > 0, "Token counts returned");

                        model.ClearRequests();
                        model.ReplyText = String.Empty;
                        model.FinishReason = "length";
                        JsonElement empty = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints/" + id + "/test", HttpFixture.AdminToken, new { maxTokens = 99999 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.Equal(8192, empty.GetProperty("maxTokens").GetInt32(), "Cap clamped to 8192");
                        Check.True(model.Requests.Any(r => r.Path == "/api/chat" && r.Body.Contains("8192", StringComparison.Ordinal)), "Cap sent on the wire");
                        bool reported = empty.GetProperty("finishReason").ValueKind == JsonValueKind.String;
                        Check.True(streaming || reported || Api.Str(empty, "hint") == "EmptyOutput", "A reply without a finish reason still explains empty output (" + empty + ")");
                        if (streaming || reported)
                        {
                            Check.True(empty.GetProperty("truncated").GetBoolean(), "Length finish is truncated (" + empty + ")");
                            Check.Equal("ReasoningConsumedBudget", Api.Str(empty, "hint"), "Empty output at the cap explains the budget");
                        }

                        model.ReplyText = "Partial answer";
                        JsonElement cut = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints/" + id + "/test", HttpFixture.AdminToken, new { maxTokens = 2048 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.Equal(2048, cut.GetProperty("maxTokens").GetInt32(), "Configured cap");
                        if (streaming || cut.GetProperty("finishReason").ValueKind == JsonValueKind.String) Check.Equal("Truncated", Api.Str(cut, "hint"), "Truncated answer hint");
                    }
                    model.FinishReason = "stop";
                }),

                AiTestKit.Case(Id, "Auth.EachTypeOnTheWire", "None, ApiKey, Bearer, Header, QueryParameter, and Basic each put exactly the expected credential on chat, streaming, and model listing requests", async (f, model, ct) =>
                {
                    Dictionary<string, object> types = new Dictionary<string, object>
                    {
                        ["None"] = new { authType = "None" },
                        ["ApiKey"] = new { authType = "ApiKey", authValue = "key-123" },
                        ["Bearer"] = new { authType = "Bearer", authValue = "bear-123" },
                        ["Header"] = new { authType = "Header", authHeaderName = "X-Custom-Key", authValue = "hdr-123" },
                        ["QueryParameter"] = new { authType = "QueryParameter", authQueryParameter = "api_key", authValue = "q 123" },
                        ["Basic"] = new { authType = "Basic", authUsername = "user", authValue = "pass-123" }
                    };
                    foreach (KeyValuePair<string, object> type in types)
                    {
                        string id = await AiTestKit.EndpointAsync(f, AiTestKit.OllamaEndpoint(model, "Auth " + type.Key, "Completion", type.Value), ct).ConfigureAwait(false);
                        model.ClearRequests();
                        JsonElement test = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints/" + id + "/test", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.True(test.GetProperty("success").GetBoolean(), type.Key + " test (" + test + ")");
                        await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints/" + id + "/models/enumerate", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        foreach (FakeModelRequest request in model.Requests)
                        {
                            string auth = request.Headers.TryGetValue("Authorization", out string? a) ? a : String.Empty;
                            switch (type.Key)
                            {
                                case "None":
                                    Check.Equal(String.Empty, auth, "None sends no Authorization");
                                    break;
                                case "ApiKey":
                                    Check.Equal("Bearer key-123", auth, "Ollama native key is a bearer");
                                    break;
                                case "Bearer":
                                    Check.Equal("Bearer bear-123", auth, "Bearer");
                                    break;
                                case "Header":
                                    Check.Equal("hdr-123", request.Headers["X-Custom-Key"], "Custom header");
                                    break;
                                case "QueryParameter":
                                    Check.True(request.Query.Contains("api_key=q%20123", StringComparison.Ordinal), "Query parameter on " + request.Path + " (" + request.Query + ")");
                                    break;
                                case "Basic":
                                    Check.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("user:pass-123")), auth, "Basic");
                                    break;
                            }
                        }
                        Check.True(model.Requests.Any(r => r.Path == "/api/tags"), type.Key + " model listing reached the server");
                    }
                }),

                AiTestKit.Case(Id, "Health.ThresholdsAndModels", "A check now records a result and moves the status; an unreachable endpoint becomes Unhealthy; the model list comes from the provider", async (f, model, ct) =>
                {
                    string id = await AiTestKit.EndpointAsync(f, AiTestKit.OllamaEndpoint(model, "Healthy", "Completion", new { healthCheck = new { enabled = true, healthyThreshold = 1, unhealthyThreshold = 1 } }), ct).ConfigureAwait(false);
                    JsonElement check = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints/" + id + "/health/check", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(check.GetProperty("success").GetBoolean(), "Check passed");
                    JsonElement health = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/ai/endpoints/" + id + "/health", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Healthy", health.GetProperty("health").GetProperty("status").GetString(), "Healthy");
                    Check.Equal(100.0, health.GetProperty("health").GetProperty("uptimePercentage").GetDouble(), "Uptime");
                    Check.Equal(1, health.GetProperty("recent").GetProperty("objects").GetArrayLength(), "Recent records");
                    string dead = await AiTestKit.EndpointAsync(f, new { name = "Dead", kind = "Completion", apiType = "Ollama", model = "m", endpointUrl = "http://127.0.0.1:9", healthCheck = new { enabled = true, timeoutMs = 2000, unhealthyThreshold = 1 } }, ct).ConfigureAwait(false);
                    JsonElement failed = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints/" + dead + "/health/check", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!failed.GetProperty("success").GetBoolean(), "Dead endpoint fails");
                    JsonElement list = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints/enumerate", HttpFixture.AdminToken, new { ordering = "HealthStatus" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(dead, list.GetProperty("objects")[0].GetProperty("id").GetString(), "Unhealthy first");
                    JsonElement models = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints/" + id + "/models/enumerate", HttpFixture.AdminToken, new { search = "chat" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("fake-chat:latest", models.GetProperty("objects")[0].GetProperty("name").GetString(), "Provider model");
                    JsonElement emptyModels = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints/" + id + "/models/enumerate", HttpFixture.AdminToken, new { labels = new[] { "absent" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(0, emptyModels.GetProperty("objects").GetArrayLength(), "Model metadata filters honored");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints/" + id + "/models/enumerate", HttpFixture.AdminToken, new { continuationToken = "1" }, HttpStatusCode.BadRequest, "InvalidContinuationToken", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints/" + id + "/models/enumerate", HttpFixture.AdminToken, new { createdAfterUtc = DateTime.UtcNow, createdBeforeUtc = DateTime.UtcNow.AddDays(-1) }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    int ran = await f.Services.Ai.Health.RunDueAsync(ct).ConfigureAwait(false);
                    Check.True(ran >= 0, "Worker pass runs");
                }),

                AiTestKit.Case(Id, "Reveal.Safeguards", "The admin token cannot reveal; a freshly signed-in SystemAdmin can with a reason; the reveal is audited without the value", async (f, model, ct) =>
                {
                    string id = await AiTestKit.EndpointAsync(f, AiTestKit.OllamaEndpoint(model, "Reveal", "Completion", new { authType = "Bearer", authValue = "reveal-me-123" }), ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints/" + id + "/reveal", HttpFixture.AdminToken, new { field = "authValue", reason = "Rotating the provider key" }, HttpStatusCode.Forbidden, "RevealNotAllowed", ct).ConfigureAwait(false);
                    TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints/" + id + "/reveal", admin.AccessToken, new { field = "authValue", reason = "short" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    ApiResult revealed = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints/" + id + "/reveal", admin.AccessToken, new { field = "authValue", reason = "Rotating the provider key" }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, revealed.Status, "Revealed (" + revealed.Text + ")");
                    Check.Equal("reveal-me-123", Api.Str(revealed.Body, "value"), "Value");
                    Check.True(revealed.Headers.TryGetValue("Cache-Control", out string? cache) && cache.Contains("no-store", StringComparison.Ordinal), "no-store");
                    DataTable audit = await f.Raw.ExecuteQueryAsync("SELECT summary FROM auditrecords WHERE action = 'ai.endpoint.reveal' AND targetid = '" + id + "';", false, ct).ConfigureAwait(false);
                    Check.Equal(1, audit.Rows.Count, "Reveal audited");
                    Check.True(!RowReader.GetString(audit.Rows[0], "summary").Contains("reveal-me-123", StringComparison.Ordinal), "Audit has no value");
                    f.Settings.Ai.AllowSecretReveal = false;
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints/" + id + "/reveal", admin.AccessToken, new { field = "authValue", reason = "Rotating the provider key" }, HttpStatusCode.Forbidden, "RevealNotAllowed", ct).ConfigureAwait(false);
                }),

                AiTestKit.Case(Id, "Ollama.PathPrefixBearerOverHttp", "An Ollama endpoint whose base URL has a path prefix, over plain http, with a Bearer token, is accepted for a public host (and never called by this suite), keeps the prefix in front of /api/chat, sends the token as an Authorization header, and never returns the token", async (f, model, ct) =>
                {
                    // The owner's case, saved disabled and without health checks so nothing ever calls that host.
                    ApiResult owner = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints", HttpFixture.AdminToken, new
                    {
                        name = "Owner gateway", kind = "Completion", apiType = "Ollama", model = "gpt-oss:20b", enabled = false,
                        endpointUrl = "http://view.homedns.org:8900/v1.0/api/gpt-oss-20b/", authType = "Bearer", authValue = "bearer-token-not-real",
                        parameters = new { temperature = 0.2, topP = 0.9, maxTokens = 2048, contextWindow = 16384, reasoningEffort = "Low", streaming = true, toolCalling = "Prompt", timeoutMs = 120000, firstResponseTimeoutMs = 30000 },
                        limits = new { maxConcurrentRequests = 1, maxQueueDepth = 5, maxRetries = 1 },
                        healthCheck = new { enabled = false },
                        pricing = new { inputPerMillionTokens = 0, outputPerMillionTokens = 0 }
                    }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Created, owner.Status, "A plain http public endpoint with a path prefix is accepted (" + owner.Text + ")");
                    Check.Equal("http://view.homedns.org:8900/v1.0/api/gpt-oss-20b/", Api.Str(owner.Body, "endpointUrl"), "The base URL is stored as entered");
                    Check.True(owner.Body.GetProperty("hasAuthValue").GetBoolean() && Api.Str(owner.Body, "authType") == "Bearer" && !owner.Text.Contains("bearer-token-not-real", StringComparison.Ordinal), "The Bearer token is stored and never returned");
                    JsonElement p = owner.Body.GetProperty("parameters");
                    Check.True(Api.Str(p, "reasoningEffort") == "Low" && Api.Str(p, "toolCalling") == "Prompt" && p.GetProperty("contextWindow").GetInt32() == 16384 && p.GetProperty("firstResponseTimeoutMs").GetInt32() == 30000, "Every model parameter round trips");
                    Check.True(model.Requests.Count == 0, "Saving called no model server");

                    // The same shape against the in-process server: the prefix stays in front of /api/chat and the token is a Bearer header.
                    model.PathPrefix = "/v1.0/api/gpt-oss-20b";
                    string id = await AiTestKit.EndpointAsync(f, new
                    {
                        name = "Prefixed", kind = "Completion", apiType = "Ollama", model = "fake-chat", endpointUrl = model.BaseUrl + "/v1.0/api/gpt-oss-20b/", authType = "Bearer", authValue = "bearer-token-not-real",
                        parameters = new { streaming = true, timeoutMs = 10000 }, limits = new { maxRetries = 0 }, healthCheck = new { enabled = false }
                    }, ct).ConfigureAwait(false);
                    JsonElement test = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints/" + id + "/test", HttpFixture.AdminToken, new { prompt = "Say hello in five words." }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(test.GetProperty("success").GetBoolean(), "The prefixed endpoint answers: " + test.GetRawText());
                    FakeModelRequest sent = model.Requests.Last(r => r.Method == "POST");
                    Check.Equal("/v1.0/api/gpt-oss-20b/api/chat", sent.Path, "The client appends /api/chat to the prefixed base URL");
                    Check.Equal("Bearer bearer-token-not-real", sent.Headers.TryGetValue("Authorization", out string? authorization) ? authorization : String.Empty, "The token travels as a Bearer Authorization header");
                    JsonElement models = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints/" + id + "/models/enumerate", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(models.GetProperty("objects").GetArrayLength() > 0 && model.Requests.Any(r => r.Path == "/v1.0/api/gpt-oss-20b/api/tags"), "Model listing uses the prefix too");
                    Check.True(model.Requests.All(r => r.Path.StartsWith("/v1.0/api/gpt-oss-20b/", StringComparison.Ordinal)), "No request left the prefix");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/ai/endpoints", HttpFixture.AdminToken, new { name = "Query", kind = "Completion", apiType = "Ollama", model = "m", endpointUrl = "http://view.homedns.org:8900/v1.0/api/gpt-oss-20b/?key=x" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                })
            });
        }

        #endregion
    }
}
