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
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared.Fakes;
    using Touchstone.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Security;

    /// <summary>
    /// The AI part of the cross-property isolation suite (docs/MULTIPROPERTY_CONTRACT.md section 4.5): two properties
    /// with features.ai each get endpoints, assistants, conversations, messages, and feedback; every parameterized AI
    /// route is swept as A's administrator, A's member, and A's admin token with B's ids (no 2xx with B data, no 5xx),
    /// every AI enumeration of A is free of B's markers, and B's AI rows are unchanged.
    /// </summary>
    internal static class AiIsolationCases
    {
        internal static TestCaseDescriptor Sweep(string suiteId)
        {
            return new TestCaseDescriptor(suiteId, "Ai.NoCrossPropertyAccess", "AI routes as A's staff and member with B's endpoint, assistant, conversation, message, and feedback ids never return B data or change B's rows", async ct =>
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
                    string a = TestProperty.SampleId;
                    string marker = "AIISO-B-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                    List<string> foreign = new List<string>();
                    string aMember = String.Empty;
                    foreach (string property in new[] { a, b })
                    {
                        bool isB = property == b;
                        string tag = isB ? marker : "AIISO-A";
                        f.PropertyId = property;
                        string endpoint = await AiTestKit.EndpointAsync(f, AiTestKit.OllamaEndpoint(model, tag + " endpoint", "Completion", new { authType = "Bearer", authValue = tag + "-secret" }), ct).ConfigureAwait(false);
                        string assistant = await AiTestKit.AssistantAsync(f, endpoint, ct, new { description = tag + " assistant" }).ConfigureAwait(false);
                        TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                        JsonElement exchange = await AiTestKit.StartAsync(f, assistant, member.AccessToken, tag + " question", ct).ConfigureAwait(false);
                        string conversation = Api.Str(exchange.GetProperty("userMessage"), "conversationId");
                        string reply = Api.Str(exchange.GetProperty("reply"), "id");
                        await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/assistant-conversations/" + conversation + "/messages/" + reply + "/feedback", member.AccessToken, new { rating = "Down", note = tag + " note" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        if (!isB)
                        {
                            aMember = member.AccessToken;
                            continue;
                        }
                        DataTable feedback = await f.Raw.ExecuteQueryAsync("SELECT id FROM aifeedback WHERE propertyid = '" + b + "';", false, ct).ConfigureAwait(false);
                        foreign.AddRange(new[] { endpoint, assistant, conversation, reply, Api.Str(exchange.GetProperty("userMessage"), "id"), RowReader.GetString(feedback.Rows[0], "id") });
                    }
                    Dictionary<string, string> before = await SnapshotAsync(f, b, ct).ConfigureAwait(false);
                    f.PropertyId = a;
                    JsonElement doc = await Api.ExpectAsync(f, HttpMethod.Get, "/openapi.json", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    List<string> problems = new List<string>();
                    int swept = 0;
                    foreach (JsonProperty path in doc.GetProperty("paths").EnumerateObject())
                    {
                        if (!path.Name.Contains("/admin/ai", StringComparison.Ordinal) && !path.Name.Contains("assistant", StringComparison.Ordinal)) continue;
                        foreach (JsonProperty operation in path.Value.EnumerateObject())
                        {
                            HttpMethod? method = operation.Name switch { "get" => HttpMethod.Get, "post" => HttpMethod.Post, "put" => HttpMethod.Put, "delete" => HttpMethod.Delete, _ => null };
                            if (method == null) continue;
                            List<string> ids = path.Name.Contains('{') ? foreign : new List<string> { "*" };
                            foreach (string id in ids)
                            {
                                string route = Regex.Replace(path.Name, "\\{[^}]+\\}", id);
                                foreach (string bearer in new[] { HttpFixture.AdminToken, aMember })
                                {
                                    object? body = method == HttpMethod.Get || method == HttpMethod.Delete ? null : new { content = "probe", rating = "Up", name = "probe", reason = "isolation probe reason", field = "authValue", query = "question", messages = new[] { new { role = "user", content = "probe" } } };
                                    ApiResult result = await Api.SendAsync(f, method, route, bearer, body, ct).ConfigureAwait(false);
                                    swept++;
                                    int status = (int)result.Status;
                                    if (status >= 500) problems.Add(method + " " + path.Name + " (" + id + "): " + status + " " + result.Text);
                                    else if (status >= 200 && status < 300)
                                    {
                                        if (result.Text.Contains(marker, StringComparison.Ordinal)) problems.Add(method + " " + path.Name + " (" + id + "): returned B's marker");
                                        foreach (string other in foreign.Where(o => o != id))
                                            if (result.Text.Contains(other, StringComparison.Ordinal)) problems.Add(method + " " + path.Name + " (" + id + "): returned B's id " + other);
                                    }
                                }
                            }
                        }
                    }
                    Dictionary<string, string> after = await SnapshotAsync(f, b, ct).ConfigureAwait(false);
                    foreach (KeyValuePair<string, string> table in before)
                        if (after[table.Key] != table.Value) problems.Add("B's " + table.Key + " rows changed during the sweep");
                    Check.True(swept > 100, "Swept AI operations (" + swept + ")");
                    Check.True(problems.Count == 0, "AI isolation problems:\n  " + String.Join("\n  ", problems.Take(40)));
                }
            }, new[] { "http", "integration", "ai", "isolation", TestEnvironment.Provider.ToString().ToLowerInvariant() });
        }

        private static async Task<Dictionary<string, string>> SnapshotAsync(HttpFixture f, string propertyId, CancellationToken ct)
        {
            Dictionary<string, string> hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string table in PropertyTables.Owned.Where(t => t.StartsWith("ai", StringComparison.Ordinal) && t != "aiusagerollups" && t != "aiusageapplied" && t != "aidailyusage"))
            {
                DataTable rows = await f.Raw.ExecuteQueryAsync("SELECT COUNT(*) AS cnt, COALESCE(md5(string_agg(md5(t::text), '' ORDER BY md5(t::text))), '') AS h FROM " + table + " t WHERE t.propertyid = '" + propertyId + "';", false, ct).ConfigureAwait(false);
                hashes[table] = RowReader.GetLong(rows.Rows[0], "cnt") + ":" + RowReader.GetString(rows.Rows[0], "h");
            }
            return hashes;
        }
    }
}
