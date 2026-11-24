namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;

    /// <summary>
    /// HTTP helpers for end-to-end cases: requests with a bearer token, member creation, and grants.
    /// </summary>
    internal static class Api
    {
        internal const string Password = "correct horse battery";

        private static int _Counter = 0;

        /// <summary>
        /// The sample property's separately accepted sign-up documents at their configured versions, for register bodies.
        /// </summary>
        internal static readonly object[] SignupDocuments = TestProperty.LoadSample().Documents?
            .Where(d => d.Acceptance == AcceptanceRequired && d.SeparateCheckbox)
            .Select(d => (object)new { key = d.Key, version = d.Version }).ToArray() ?? Array.Empty<object>();

        private const DocumentAcceptanceModeEnum AcceptanceRequired = DocumentAcceptanceModeEnum.RequiredAtSignup;

        internal static string UniqueEmail(string prefix = "member")
        {
            return prefix + "." + Interlocked.Increment(ref _Counter) + "." + Guid.NewGuid().ToString("N").Substring(0, 6) + "@example.com";
        }

        internal static async Task<ApiResult> SendAsync(HttpFixture f, HttpMethod method, string path, string? bearer, object? body, CancellationToken token,
            string client = "mobile", Dictionary<string, string>? headers = null, HttpContent? content = null)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(method, path))
            {
                if (bearer != null) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer);
                request.Headers.TryAddWithoutValidation("X-Client", client);
                if (headers != null) foreach (KeyValuePair<string, string> h in headers) request.Headers.TryAddWithoutValidation(h.Key, h.Value);
                f.Decorate(request);
                if (content != null) request.Content = content;
                else if (body is string text) request.Content = new StringContent(text, Encoding.UTF8, "application/json");
                else if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await f.Client.SendAsync(request, token).ConfigureAwait(false))
                {
                    ApiResult result = new ApiResult
                    {
                        Status = response.StatusCode,
                        Text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false),
                        SetCookie = response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies) ? cookies.FirstOrDefault() : null,
                        ContentType = response.Content.Headers.ContentType?.ToString(),
                        RetryAfter = response.Headers.TryGetValues("Retry-After", out IEnumerable<string>? retry) ? retry.FirstOrDefault() : null
                    };
                    foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers) result.Headers[header.Key] = String.Join(",", header.Value);
                    foreach (KeyValuePair<string, IEnumerable<string>> header in response.Content.Headers) if (!result.Headers.ContainsKey(header.Key)) result.Headers[header.Key] = String.Join(",", header.Value);
                    if (!String.IsNullOrWhiteSpace(result.Text) && (result.ContentType ?? String.Empty).Contains("json"))
                    {
                        using (JsonDocument doc = JsonDocument.Parse(result.Text)) result.Body = doc.RootElement.Clone();
                    }
                    return result;
                }
            }
        }

        internal static async Task<JsonElement> ExpectAsync(HttpFixture f, HttpMethod method, string path, string? bearer, object? body, HttpStatusCode expected, CancellationToken token, string client = "mobile")
        {
            ApiResult result = await SendAsync(f, method, path, bearer, body, token, client).ConfigureAwait(false);
            Check.Equal(expected, result.Status, method + " " + path + " status (body: " + result.Text + ")");
            return result.Body;
        }

        internal static async Task ExpectErrorAsync(HttpFixture f, HttpMethod method, string path, string? bearer, object? body, HttpStatusCode expected, string error, CancellationToken token)
        {
            ApiResult result = await SendAsync(f, method, path, bearer, body, token).ConfigureAwait(false);
            Check.Equal(expected, result.Status, method + " " + path + " status (body: " + result.Text + ")");
            Check.Equal(error, result.Error, method + " " + path + " error code");
        }

        internal static async Task<TestMember> RegisterAsync(HttpFixture f, CancellationToken token, string? email = null, string displayName = "Test Member")
        {
            TestMember member = new TestMember { Email = email ?? UniqueEmail(), Password = Password };
            JsonElement body = await ExpectAsync(f, HttpMethod.Post, "/api/v1/auth/register", null,
                new { email = member.Email, password = member.Password, displayName, dateOfBirth = "1990-01-01", acceptTerms = true, acceptedDocuments = Api.SignupDocuments }, HttpStatusCode.Created, token).ConfigureAwait(false);
            member.Id = body.GetProperty("user").GetProperty("id").GetString()!;
            member.AccessToken = body.GetProperty("accessToken").GetString()!;
            member.RefreshToken = body.GetProperty("refreshToken").GetString();
            return member;
        }

        internal static async Task<TestMember> LoginAsync(HttpFixture f, TestMember member, CancellationToken token)
        {
            JsonElement body = await ExpectAsync(f, HttpMethod.Post, "/api/v1/auth/login", null, new { email = member.Email, password = member.Password }, HttpStatusCode.OK, token).ConfigureAwait(false);
            member.AccessToken = body.GetProperty("accessToken").GetString()!;
            member.RefreshToken = body.GetProperty("refreshToken").GetString();
            return member;
        }

        internal static async Task<TestMember> MemberAsync(HttpFixture f, CancellationToken token, string? email = null)
        {
            TestMember member = await RegisterAsync(f, token, email).ConfigureAwait(false);
            await GrantCompAsync(f, member.Id, token).ConfigureAwait(false);
            return member;
        }

        internal static async Task<TestMember> AdminAsync(HttpFixture f, CancellationToken token)
        {
            TestMember member = await RegisterAsync(f, token, UniqueEmail("admin"), "Admin Person").ConfigureAwait(false);
            await f.Database.RoleAssignments.AddAsync(new RoleAssignment
            {
                Id = IdGenerator.GenerateRoleAssignmentId(),
                UserId = member.Id,
                Role = RoleEnum.SystemAdmin,
                ResourceType = ResourceTypeEnum.Global,
                ResourceId = "*"
            }, token).ConfigureAwait(false);
            f.Services.Tokens.InvalidateUser(member.Id);
            return member;
        }

        internal static Task GrantCompAsync(HttpFixture f, string userId, CancellationToken token)
        {
            return ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/members/" + userId + "/comp", HttpFixture.AdminToken,
                new { grantType = "Lifetime", reason = "test" }, HttpStatusCode.OK, token);
        }

        internal static async Task<TestMember> SocialAsync(HttpFixture f, CancellationToken token, string provider = "google", string? email = null)
        {
            string tokenText = "tok-" + Guid.NewGuid().ToString("N");
            string subject = "sub-" + Guid.NewGuid().ToString("N");
            string mail = email ?? UniqueEmail("social");
            object body;
            if (provider == "apple") { f.Fakes.Apple.Script(tokenText, subject, mail); body = new { identityToken = tokenText, nonce = "n1", givenName = "Ann", familyName = "Apple" }; }
            else if (provider == "facebook") { f.Fakes.Facebook.Script(tokenText, subject, mail); body = new { token = tokenText, tokenType = "AccessToken" }; }
            else { f.Fakes.Google.Script(tokenText, subject, mail, true, "Gus Google"); body = new { idToken = tokenText }; }
            JsonElement result = await ExpectAsync(f, HttpMethod.Post, "/api/v1/auth/" + provider, null, body, HttpStatusCode.OK, token).ConfigureAwait(false);
            return new TestMember
            {
                Id = result.GetProperty("user").GetProperty("id").GetString()!,
                Email = mail,
                AccessToken = result.GetProperty("accessToken").GetString()!,
                RefreshToken = result.GetProperty("refreshToken").GetString()
            };
        }

        internal static string Str(JsonElement element, string name)
        {
            return element.GetProperty(name).GetString() ?? String.Empty;
        }
    }
}
