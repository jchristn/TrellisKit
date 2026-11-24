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
    using Touchstone.Core;
    using TrellisKit.Server.Services;

    /// <summary>
    /// End-to-end HTTP behavior against a running server: health, authentication, CORS, settings, request capture,
    /// OpenAPI, and restart.
    /// </summary>
    public static class HttpSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Http";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "HTTP API (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Health.Anonymous", "Health is anonymous and reports version and provider", async (f, ct) =>
                {
                    JsonElement body = await SendAsync(f, HttpMethod.Get, "/api/v1/health", false, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Healthy", body.GetProperty("status").GetString(), "status");
                    Check.Equal("1.0.0", body.GetProperty("version").GetString(), "version");
                    Check.Equal(TestEnvironment.Provider.ToString(), body.GetProperty("database").GetString(), "database");
                    await SendAsync(f, HttpMethod.Get, "/api/v1/health/ready", false, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                }),
                Case("Auth.AdminRoutesRequireToken", "Admin routes return 401 without a valid token", async (f, ct) =>
                {
                    JsonElement body = await SendAsync(f, HttpMethod.Get, "/api/v1/settings", false, null, HttpStatusCode.Unauthorized, ct).ConfigureAwait(false);
                    Check.Equal("Unauthorized", body.GetProperty("error").GetString(), "error code");
                    await SendAsync(f, HttpMethod.Get, "/api/v1/request-history", false, null, HttpStatusCode.Unauthorized, ct).ConfigureAwait(false);
                    await SendAsync(f, HttpMethod.Get, "/api/v1/settings", true, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement missing = await SendAsync(f, HttpMethod.Get, "/api/v1/nothing-here", true, null, HttpStatusCode.NotFound, ct).ConfigureAwait(false);
                    Check.Equal("NotFound", missing.GetProperty("error").GetString(), "404 error code");
                }),
                Case("Cors.AllowList", "Preflight echoes allowed origins and omits CORS headers for others", async (f, ct) =>
                {
                    using (HttpRequestMessage allowed = f.Request(HttpMethod.Options, "/api/v1/settings", false))
                    {
                        allowed.Headers.Add("Origin", "http://localhost:5173");
                        using (HttpResponseMessage response = await f.Client.SendAsync(allowed, ct).ConfigureAwait(false))
                        {
                            Check.Equal("http://localhost:5173", response.Headers.TryGetValues("Access-Control-Allow-Origin", out System.Collections.Generic.IEnumerable<string>? values) ? values.First() : null, "Allowed origin");
                        }
                    }
                    using (HttpRequestMessage denied = f.Request(HttpMethod.Options, "/api/v1/settings", false))
                    {
                        denied.Headers.Add("Origin", "https://evil.example");
                        using (HttpResponseMessage response = await f.Client.SendAsync(denied, ct).ConfigureAwait(false))
                        {
                            Check.True(!response.Headers.Contains("Access-Control-Allow-Origin"), "No allow-origin header for a disallowed origin");
                        }
                    }
                }),
                Case("Settings.UpdateAndValidate", "Settings PUT validates per field and applies live changes", async (f, ct) =>
                {
                    JsonElement invalid = await SendAsync(f, HttpMethod.Put, "/api/v1/settings", true, "{\"values\":{\"requestHistory.retentionDays\":0,\"rest.port\":1}}", HttpStatusCode.BadRequest, ct).ConfigureAwait(false);
                    JsonElement errors = invalid.GetProperty("fieldErrors");
                    Check.True(errors.TryGetProperty("requestHistory.retentionDays", out JsonElement _), "Range error reported");
                    Check.True(errors.TryGetProperty("rest.port", out JsonElement _), "Environment-locked field reported");
                    await SendAsync(f, HttpMethod.Put, "/api/v1/settings", true, "not json", HttpStatusCode.BadRequest, ct).ConfigureAwait(false);
                    await SendAsync(f, HttpMethod.Put, "/api/v1/settings", true, "{\"values\":{\"requestHistory.retentionDays\":9}}", HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(9, f.Settings.RequestHistory.RetentionDays, "Live change applied");
                }),
                Case("RequestHistory.CaptureAndRedact", "Requests are captured with the Authorization header redacted", async (f, ct) =>
                {
                    await SendAsync(f, HttpMethod.Get, "/api/v1/settings?search=capture", true, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    string? id = null;
                    for (int attempt = 0; attempt < 40 && id == null; attempt++)
                    {
                        JsonElement page = await SendAsync(f, HttpMethod.Get, "/api/v1/request-history?pathContains=%2Fapi%2Fv1%2Fsettings&method=GET", true, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        JsonElement match = page.GetProperty("objects").EnumerateArray().FirstOrDefault(i => i.GetProperty("url").GetString() == "/api/v1/settings?search=capture");
                        if (match.ValueKind == JsonValueKind.Object) id = match.GetProperty("id").GetString();
                        else await Task.Delay(50, ct).ConfigureAwait(false);
                    }
                    Check.True(id != null, "Captured entry found");
                    JsonElement entry = await SendAsync(f, HttpMethod.Get, "/api/v1/request-history/" + id, true, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("***redacted***", entry.GetProperty("requestHeaders").GetProperty("Authorization").GetString(), "Authorization redacted");
                    Check.Equal("admin", entry.GetProperty("principalName").GetString(), "Principal recorded");
                    Check.True(entry.GetProperty("responseBody").GetString()!.Contains("\"objects\"", StringComparison.Ordinal), "Enumerated response body captured");
                    await SendAsync(f, HttpMethod.Delete, "/api/v1/request-history/" + id, true, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                    await SendAsync(f, HttpMethod.Get, "/api/v1/request-history/" + id, true, null, HttpStatusCode.NotFound, ct).ConfigureAwait(false);
                }),
                Case("RequestHistory.QueryValidation", "Malformed query parameters return field errors", async (f, ct) =>
                {
                    JsonElement body = await SendAsync(f, HttpMethod.Get, "/api/v1/request-history?statusCode=abc", true, null, HttpStatusCode.BadRequest, ct).ConfigureAwait(false);
                    Check.True(body.GetProperty("fieldErrors").TryGetProperty("filters.statusCode", out JsonElement _), "statusCode error");
                    body = await SendAsync(f, HttpMethod.Get, "/api/v1/request-history?createdAfterUtc=yesterday", true, null, HttpStatusCode.BadRequest, ct).ConfigureAwait(false);
                    Check.True(body.GetProperty("fieldErrors").TryGetProperty("createdAfterUtc", out JsonElement _), "createdAfterUtc error");
                    body = await SendAsync(f, HttpMethod.Get, "/api/v1/request-history?fromUtc=2026-01-01T00:00:00Z", true, null, HttpStatusCode.BadRequest, ct).ConfigureAwait(false);
                    Check.True(body.GetProperty("fieldErrors").TryGetProperty("filters.fromUtc", out JsonElement _), "fromUtc is not a list filter (use createdAfterUtc)");
                    await SendAsync(f, HttpMethod.Get, "/api/v1/request-history/summary", true, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    DateTime now = DateTime.UtcNow;
                    string from = Uri.EscapeDataString(now.AddHours(-1).ToString("o"));
                    string to = Uri.EscapeDataString(now.ToString("o"));
                    JsonElement summary = await SendAsync(f, HttpMethod.Get, "/api/v1/request-history/summary?fromUtc=" + from + "&toUtc=" + to + "&bucketMinutes=10", true, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(6, summary.GetProperty("objects").GetArrayLength(), "Bucket count");
                }),
                Case("OpenApi.Anonymous", "The OpenAPI document is anonymous and lists the API", async (f, ct) =>
                {
                    JsonElement doc = await SendAsync(f, HttpMethod.Get, "/openapi.json", false, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement paths = doc.GetProperty("paths");
                    foreach (string path in new[] { "/api/v1/health", "/api/v1/request-history", "/api/v1/request-history/summary", "/api/v1/request-history/{id}", "/api/v1/settings", "/api/v1/settings/restart" })
                    {
                        Check.True(paths.TryGetProperty(path, out JsonElement _), "OpenAPI documents " + path);
                    }
                }),
                Case("Settings.Restart", "Restart responds 202 and signals the host to exit with the restart code", async (f, ct) =>
                {
                    await SendAsync(f, HttpMethod.Post, "/api/v1/settings/restart", true, null, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
                    for (int attempt = 0; attempt < 40 && !f.Lifetime.Token.IsCancellationRequested; attempt++)
                    {
                        await Task.Delay(50, ct).ConfigureAwait(false);
                    }
                    Check.True(f.Lifetime.Token.IsCancellationRequested, "Shutdown signaled");
                    Check.True(f.Lifetime.RestartRequested, "Restart requested");
                    Check.Equal(75, f.Lifetime.ExitCode, "Exit code");
                }),
                Case("Regression.Http.NoRequestHeaderEcho", "Responses do not echo request headers such as Host, Cookie, or custom headers", async (f, ct) =>
                {
                    ApiResult result = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/health", null, null, ct, "web",
                        new Dictionary<string, string> { { "X-Echo-Probe", "probe-value" }, { "Cookie", "tk_probe=1" } }).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, result.Status, "Health");
                    foreach (string name in new[] { "Host", "X-Echo-Probe", "Cookie", "X-Client", "User-Agent" })
                        Check.True(!result.Headers.ContainsKey(name), "Response does not echo " + name + " (headers: " + String.Join(", ", result.Headers.Keys) + ")");
                }),
                Case("Regression.Http.HeadAllow", "HEAD on a GET route is 405 with Allow: GET and no body; HEAD on an unknown path is 404 like GET", async (f, ct) =>
                {
                    using (HttpRequestMessage head = f.Request(HttpMethod.Head, "/api/v1/health", false))
                    using (HttpResponseMessage response = await f.Client.SendAsync(head, ct).ConfigureAwait(false))
                    {
                        Check.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode, "HEAD health");
                        Check.True(response.Content.Headers.Allow.Contains("GET") || (response.Headers.TryGetValues("Allow", out IEnumerable<string>? allow) && allow.Contains("GET")), "Allow: GET");
                    }
                    using (HttpRequestMessage head = f.Request(HttpMethod.Head, "/api/v1/feed/fpo_x", false))
                    using (HttpResponseMessage response = await f.Client.SendAsync(head, ct).ConfigureAwait(false))
                    {
                        Check.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode, "HEAD on a parameter route");
                    }
                    ApiResult get = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/nothing-here", HttpFixture.AdminToken, null, ct).ConfigureAwait(false);
                    using (HttpRequestMessage head = f.Request(HttpMethod.Head, "/api/v1/nothing-here", true))
                    using (HttpResponseMessage response = await f.Client.SendAsync(head, ct).ConfigureAwait(false))
                    {
                        Check.Equal(HttpStatusCode.NotFound, get.Status, "GET unknown path");
                        Check.Equal(HttpStatusCode.NotFound, response.StatusCode, "HEAD unknown path matches GET");
                    }
                }),
                Case("Regression.Http.SummaryBucketBounds", "bucketMinutes outside 1 to 1440 on the request history summary is 400 ValidationFailed naming bucketMinutes", async (f, ct) =>
                {
                    string range = "/api/v1/request-history/summary?fromUtc=2026-11-11T18:00:00Z&toUtc=2026-11-11T19:00:00Z&bucketMinutes=";
                    foreach (string bad in new[] { "0", "1441", "-5" })
                    {
                        ApiResult result = await Api.SendAsync(f, HttpMethod.Get, range + bad, HttpFixture.AdminToken, null, ct).ConfigureAwait(false);
                        Check.Equal(HttpStatusCode.BadRequest, result.Status, "bucketMinutes=" + bad);
                        Check.True(result.Body.GetProperty("fieldErrors").TryGetProperty("filters.bucketMinutes", out JsonElement _), "Names bucketMinutes (" + bad + ")");
                    }
                    await Api.ExpectAsync(f, HttpMethod.Get, range + "1", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, range + "1440", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                }),
                SuiteCases.Unit(Id, "Regression.API03.ResolverRules", "The client IP honors X-Forwarded-For only from trusted proxies and takes the right-most untrusted hop", ct =>
                {
                    List<string> trusted = TrellisKit.Core.Settings.RestSettings.DefaultTrustedProxies.ToList();
                    Check.Equal("198.51.100.4", ClientAddressResolver.Resolve("198.51.100.4", "203.0.113.7", trusted), "Untrusted peer: header ignored");
                    Check.Equal("203.0.113.7", ClientAddressResolver.Resolve("172.18.0.3", "203.0.113.7", trusted), "Trusted peer: forwarded client");
                    Check.Equal("203.0.113.9", ClientAddressResolver.Resolve("10.0.0.2", "1.2.3.4, 203.0.113.9, 10.0.0.5", trusted), "Right-most untrusted hop, spoofed left entries ignored");
                    Check.Equal("203.0.113.7", ClientAddressResolver.Resolve("::ffff:192.168.1.1", "203.0.113.7:5123", trusted), "Mapped IPv4 peer and port stripped");
                    Check.Equal("2001:db8::1", ClientAddressResolver.Resolve("::1", "[2001:db8::1]:443", trusted), "Bracketed IPv6 hop");
                    Check.Equal("10.0.0.9", ClientAddressResolver.Resolve("127.0.0.1", "10.0.0.9", trusted), "All trusted: the left-most hop");
                    Check.Equal("127.0.0.1", ClientAddressResolver.Resolve("127.0.0.1", "garbage", trusted), "Invalid hop stops at the last trusted address");
                    Check.Equal("127.0.0.1", ClientAddressResolver.Resolve("127.0.0.1", "203.0.113.7", new List<string>()), "Empty list ignores the header");
                    Check.Equal("203.0.113.7", ClientAddressResolver.Resolve("192.0.2.10", "203.0.113.7", new List<string> { "192.0.2.10" }), "Single address entry");
                    bool rejected = false;
                    try { new TrellisKit.Core.Settings.RestSettings().TrustedProxies = new List<string> { "not-an-ip" }; }
                    catch (ArgumentException) { rejected = true; }
                    Check.True(rejected, "Invalid entries are rejected");
                    return Task.CompletedTask;
                }),
                SuiteCases.Unit(Id, "ClientIp.ViewerAddressHeader", "From a trusted proxy the CDN viewer address (IPv4 and IPv6 ip:port) names the client before X-Forwarded-For; untrusted peers and malformed or missing headers fall back", ct =>
                {
                    List<string> trusted = TrellisKit.Core.Settings.RestSettings.DefaultTrustedProxies.ToList();
                    Check.Equal("198.51.100.10", ClientAddressResolver.Resolve("10.0.1.5", "203.0.113.40, 10.0.0.2", "198.51.100.10:46532", trusted), "IPv4 viewer address wins over X-Forwarded-For");
                    Check.Equal("2001:db8:85a3::8a2e:370:7334", ClientAddressResolver.Resolve("10.0.1.5", "203.0.113.40", "2001:db8:85a3::8a2e:370:7334:46532", trusted), "IPv6 viewer address with a port");
                    Check.Equal("2001:db8::1", ClientAddressResolver.Resolve("10.0.1.5", null, "[2001:db8::1]:443", trusted), "Bracketed IPv6 viewer address");
                    Check.Equal("198.51.100.4", ClientAddressResolver.Resolve("198.51.100.4", "203.0.113.7", "192.0.2.99:1234", trusted), "Untrusted peer: spoofed viewer header ignored");
                    Check.Equal("203.0.113.40", ClientAddressResolver.Resolve("10.0.1.5", "203.0.113.40", "not-an-address", trusted), "Malformed viewer header: X-Forwarded-For");
                    Check.Equal("203.0.113.40", ClientAddressResolver.Resolve("10.0.1.5", "203.0.113.40", "1.2.3.4:5, 6.7.8.9:10", trusted), "A list is not a viewer address");
                    Check.Equal("203.0.113.40", ClientAddressResolver.Resolve("10.0.1.5", "203.0.113.40", null, trusted), "Missing viewer header: X-Forwarded-For");
                    Check.Equal("10.0.1.5", ClientAddressResolver.Resolve("10.0.1.5", null, null, trusted), "No headers: the peer");
                    // CloudFront edge -> ALB -> server: the ALB appends the edge address, which is trusted only through the additional ranges.
                    List<string> withCdn = new List<string>(trusted) { "130.176.0.0/16" };
                    Check.Equal("203.0.113.50", ClientAddressResolver.Resolve("10.0.1.5", "203.0.113.50, 130.176.12.9", null, withCdn), "Additional ranges: walk past the edge hop");
                    Check.Equal("130.176.12.9", ClientAddressResolver.Resolve("10.0.1.5", "203.0.113.50, 130.176.12.9", null, trusted), "Without them the edge is the client");
                    TrellisKit.Core.Settings.RestSettings rest = new TrellisKit.Core.Settings.RestSettings { AdditionalTrustedProxies = new List<string> { "130.176.0.0/16" } };
                    Check.True(rest.AllTrustedProxies.Contains("130.176.0.0/16") && rest.AllTrustedProxies.Contains("10.0.0.0/8"), "Combined trusted list");
                    Check.Equal("CloudFront-Viewer-Address", rest.ViewerAddressHeader, "Default viewer header");
                    bool rejected = false;
                    try { rest.AdditionalTrustedProxies = new List<string> { "nope" }; }
                    catch (ArgumentException) { rejected = true; }
                    Check.True(rejected, "Invalid additional entries are rejected");
                    return Task.CompletedTask;
                }),
                Case("ClientIp.ViewerAddressOverHttp", "Request info uses CloudFront-Viewer-Address from a trusted (loopback) peer; disabling the header falls back to X-Forwarded-For", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    Dictionary<string, string> headers = new Dictionary<string, string> { { "X-Forwarded-For", "203.0.113.61" }, { "CloudFront-Viewer-Address", "198.51.100.77:51000" } };
                    ApiResult login = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/auth/login", null, new { email = member.Email, password = member.Password }, ct, "web", headers).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, login.Status, "Login");
                    System.Data.DataTable rows = await f.Raw.ExecuteQueryAsync("SELECT sourceip FROM authsessions WHERE userid = '" + member.Id + "' ORDER BY createdutc DESC LIMIT 1;", false, ct).ConfigureAwait(false);
                    Check.Equal("198.51.100.77", rows.Rows[0]["sourceip"]?.ToString(), "Viewer address recorded");
                    f.Settings.Rest.ViewerAddressHeader = String.Empty;
                    await Api.SendAsync(f, HttpMethod.Post, "/api/v1/auth/login", null, new { email = member.Email, password = member.Password }, ct, "web", headers).ConfigureAwait(false);
                    rows = await f.Raw.ExecuteQueryAsync("SELECT sourceip FROM authsessions WHERE userid = '" + member.Id + "' ORDER BY createdutc DESC LIMIT 1;", false, ct).ConfigureAwait(false);
                    Check.Equal("203.0.113.61", rows.Rows[0]["sourceip"]?.ToString(), "Header disabled: X-Forwarded-For");
                }),
                Case("Regression.API03.ForwardedClientIp", "Sessions, audit, and request history record the forwarded client IP when the peer is a trusted proxy", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    Dictionary<string, string> forwarded = new Dictionary<string, string> { { "X-Forwarded-For", "198.51.100.23" } };
                    ApiResult login = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/auth/login", null, new { email = member.Email, password = member.Password }, ct, "mobile", forwarded).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, login.Status, "Login");
                    System.Data.DataTable sessions = await f.Raw.ExecuteQueryAsync("SELECT sourceip FROM authsessions WHERE userid = '" + member.Id + "' ORDER BY createdutc DESC;", false, ct).ConfigureAwait(false);
                    Check.Equal("198.51.100.23", sessions.Rows[0]["sourceip"]?.ToString(), "Session IP is the forwarded client (loopback peer is trusted)");
                    Check.Equal("127.0.0.1", sessions.Rows[sessions.Rows.Count - 1]["sourceip"]?.ToString(), "Without the header the peer is recorded");
                    string? ip = null;
                    for (int attempt = 0; attempt < 40 && ip == null; attempt++)
                    {
                        JsonElement page = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/request-history?pathContains=%2Fauth%2Flogin", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        if (page.GetProperty("objects").GetArrayLength() > 0) ip = Api.Str(page.GetProperty("objects")[0], "sourceIp");
                        else await Task.Delay(50, ct).ConfigureAwait(false);
                    }
                    Check.Equal("198.51.100.23", ip, "Request history IP");

                    f.Settings.Rest.TrustedProxies = new List<string>();
                    await Api.SendAsync(f, HttpMethod.Post, "/api/v1/auth/login", null, new { email = member.Email, password = member.Password }, ct, "mobile", forwarded).ConfigureAwait(false);
                    System.Data.DataTable latest = await f.Raw.ExecuteQueryAsync("SELECT sourceip FROM authsessions WHERE userid = '" + member.Id + "' ORDER BY createdutc DESC LIMIT 1;", false, ct).ConfigureAwait(false);
                    Check.Equal("127.0.0.1", latest.Rows[0]["sourceip"]?.ToString(), "Untrusted peer: the header is ignored (live setting)");
                })
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(Id, caseId, name, async ct =>
            {
                HttpFixture fixture = await HttpFixture.StartAsync(ct).ConfigureAwait(false);
                await using (fixture.ConfigureAwait(false))
                {
                    await body(fixture, ct).ConfigureAwait(false);
                }
            }, new[] { "http", "integration", TestEnvironment.Provider.ToString().ToLowerInvariant() });
        }

        private static async Task<JsonElement> SendAsync(HttpFixture fixture, HttpMethod method, string path, bool authenticated, string? body, HttpStatusCode expected, CancellationToken token)
        {
            using (HttpRequestMessage request = fixture.Request(method, path, authenticated))
            {
                if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await fixture.Client.SendAsync(request, token).ConfigureAwait(false))
                {
                    string text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    Check.Equal(expected, response.StatusCode, method + " " + path + " status (body: " + text + ")");
                    if (String.IsNullOrWhiteSpace(text)) return default;
                    using (JsonDocument doc = JsonDocument.Parse(text))
                    {
                        return doc.RootElement.Clone();
                    }
                }
            }
        }

        #endregion
    }
}
