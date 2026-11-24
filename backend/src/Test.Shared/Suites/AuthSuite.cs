namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Phase 1 authentication over HTTP: registration, password sign-in and lockout, refresh rotation and reuse
    /// detection (mobile body and web cookie), sign-out, email verification, password reset, social sign-in and
    /// account linking, age attestation, the bootstrap administrator, and provider availability.
    /// </summary>
    public static class AuthSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Auth";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Authentication (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Register.Success", "Registration returns 201 AuthResponse, sends a verification email, and returns the refresh token to mobile", async (f, ct) =>
                {
                    string email = Api.UniqueEmail();
                    JsonElement body = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/auth/register", null,
                        new { email = email.ToUpperInvariant(), password = Api.Password, displayName = "  New Person ", dateOfBirth = "1990-05-05", acceptTerms = true, acceptedDocuments = Api.SignupDocuments }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Check.True(body.GetProperty("isNewAccount").GetBoolean(), "isNewAccount");
                    Check.True(!String.IsNullOrEmpty(Api.Str(body, "accessToken")) && !String.IsNullOrEmpty(Api.Str(body, "refreshToken")), "Tokens returned");
                    Check.True(body.GetProperty("accessTokenExpiresUtc").GetDateTime() > DateTime.UtcNow.AddMinutes(55), "Access token lasts about an hour");
                    JsonElement me = body.GetProperty("user");
                    Check.Equal(email, Api.Str(me, "email"), "Email normalized");
                    Check.Equal("New Person", Api.Str(me, "displayName"), "Display name trimmed");
                    Check.True(!me.GetProperty("emailVerified").GetBoolean(), "Not verified yet");
                    Check.True(me.GetProperty("ageAttested").GetBoolean(), "Age attested at registration");
                    Check.True(!me.GetProperty("profileComplete").GetBoolean(), "Profile incomplete without handle and rank");
                    Check.Equal("Email", me.GetProperty("signInMethods")[0].GetString(), "Sign-in methods");
                    Check.True(!me.GetProperty("membership").GetProperty("isActive").GetBoolean(), "No membership");
                    Check.Equal(1, f.Fakes.Email.To(email, EmailService.VerifyEmailCategory).Count, "Verification email sent");
                    Check.True(f.Fakes.Email.Messages.Last().TextBody.Contains(f.Services.Property.PublicBaseUrl + "/verify-email?token=", StringComparison.Ordinal), "Link to the website");
                }),
                Case("Register.Validation", "Invalid registrations return 400 ValidationFailed with camelCase fieldErrors", async (f, ct) =>
                {
                    ApiResult result = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/auth/register", null,
                        new { email = "not-an-email", password = "short", displayName = "X", dateOfBirth = "1990-13-45", acceptTerms = false }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, result.Status, "Status");
                    Check.Equal("ValidationFailed", result.Error, "Error");
                    JsonElement errors = result.Body.GetProperty("fieldErrors");
                    foreach (string field in new[] { "email", "password", "displayName", "dateOfBirth", "acceptTerms" })
                        Check.True(errors.TryGetProperty(field, out JsonElement _), "fieldErrors." + field);
                    string email = Api.UniqueEmail();
                    ApiResult same = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/auth/register", null,
                        new { email, password = email, displayName = "Same Pass", dateOfBirth = "1990-01-01", acceptTerms = true, acceptedDocuments = Api.SignupDocuments }, ct).ConfigureAwait(false);
                    Check.True(same.Body.GetProperty("fieldErrors").TryGetProperty("password", out JsonElement _), "Password equal to email rejected");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/auth/register", null, "{not json", HttpStatusCode.BadRequest, "InvalidJson", ct).ConfigureAwait(false);
                }),
                Case("Register.Underage", "Members under the property minimum age get 403 AgeRequirementNotMet and nothing is stored", async (f, ct) =>
                {
                    string email = Api.UniqueEmail();
                    string dob = DateTime.UtcNow.AddYears(1 - f.Property.Membership.MinimumAge).ToString("yyyy-MM-dd");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/auth/register", null,
                        new { email, password = Api.Password, displayName = "Young One", dateOfBirth = dob, acceptTerms = true, acceptedDocuments = Api.SignupDocuments }, HttpStatusCode.Forbidden, "AgeRequirementNotMet", ct).ConfigureAwait(false);
                    Check.True(await f.Database.Users.ReadByEmailAsync(email, ct).ConfigureAwait(false) == null, "No account stored");
                    Check.Equal(0, f.Fakes.Email.To(email, EmailService.VerifyEmailCategory).Count, "No email sent");
                }),
                Case("Register.EmailInUse", "A second registration with the same email in another case is 409 EmailInUse", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/auth/register", null,
                        new { email = member.Email.ToUpperInvariant(), password = Api.Password, displayName = "Dupe", dateOfBirth = "1990-01-01", acceptTerms = true, acceptedDocuments = Api.SignupDocuments }, HttpStatusCode.Conflict, "EmailInUse", ct).ConfigureAwait(false);
                }),
                Case("Login.InvalidCredentials", "Unknown email and wrong password both return 401 InvalidCredentials with the same message", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    ApiResult unknown = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/auth/login", null, new { email = Api.UniqueEmail(), password = Api.Password }, ct).ConfigureAwait(false);
                    ApiResult wrong = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/auth/login", null, new { email = member.Email, password = "wrong password!!" }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Unauthorized, unknown.Status, "Unknown email status");
                    Check.Equal(HttpStatusCode.Unauthorized, wrong.Status, "Wrong password status");
                    Check.Equal("InvalidCredentials", unknown.Error, "Unknown email code");
                    Check.Equal(Api.Str(unknown.Body, "message"), Api.Str(wrong.Body, "message"), "Same message");
                    await Api.LoginAsync(f, member, ct).ConfigureAwait(false);
                }),
                Case("Login.Lockout", "Ten failures in the window return 423 AccountLocked, even with the right password", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    for (int i = 1; i < f.Settings.Auth.LockoutThreshold; i++)
                        await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/auth/login", null, new { email = member.Email, password = "wrong password!!" }, HttpStatusCode.Unauthorized, "InvalidCredentials", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/auth/login", null, new { email = member.Email, password = "wrong password!!" }, (HttpStatusCode)423, "AccountLocked", ct).ConfigureAwait(false);
                    ApiResult locked = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/auth/login", null, new { email = member.Email, password = member.Password }, ct).ConfigureAwait(false);
                    Check.Equal((HttpStatusCode)423, locked.Status, "Right password while locked");
                    Check.True(Api.Str(locked.Body, "message").Contains("Try again in", StringComparison.Ordinal), "Message says when to retry");
                    TrellisKit.Core.Models.User? user = await f.Database.Users.ReadByEmailAsync(member.Email, ct).ConfigureAwait(false);
                    user!.LockedUntilUtc = DateTime.UtcNow.AddSeconds(-1);
                    await f.Database.Users.UpdateAsync(user, ct).ConfigureAwait(false);
                    await Api.LoginAsync(f, member, ct).ConfigureAwait(false);
                }),
                Case("Login.WebCookie", "Web sign-in sets the HttpOnly tk_refresh cookie, returns refreshToken null, and refresh and logout use the cookie", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    ApiResult login = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/auth/login", null, new { email = member.Email, password = member.Password }, ct, "web").ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, login.Status, "Login status");
                    Check.True(login.Body.GetProperty("refreshToken").ValueKind == JsonValueKind.Null, "refreshToken null for web");
                    string cookie = login.SetCookie ?? String.Empty;
                    Check.True(cookie.StartsWith("tk_refresh=", StringComparison.Ordinal), "Cookie name");
                    Check.True(cookie.Contains("Path=/api/v1/auth", StringComparison.Ordinal) && cookie.Contains("HttpOnly", StringComparison.Ordinal) && cookie.Contains("SameSite=Lax", StringComparison.Ordinal), "Cookie attributes");
                    Check.True(!cookie.Contains("Secure", StringComparison.Ordinal), "Not Secure on http loopback");
                    string value = cookie.Split(';')[0];

                    ApiResult refreshed = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/auth/refresh", null, null, ct, "web", new Dictionary<string, string> { { "Cookie", value } }).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, refreshed.Status, "Cookie refresh (body: " + refreshed.Text + ")");
                    Check.True(refreshed.Body.GetProperty("user").GetProperty("id").GetString() == member.Id, "Refresh returns the full AuthResponse with user");
                    string rotated = (refreshed.SetCookie ?? String.Empty).Split(';')[0];
                    Check.True(rotated.StartsWith("tk_refresh=", StringComparison.Ordinal) && rotated != value, "Cookie rotated");

                    ApiResult logout = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/auth/logout", null, null, ct, "web", new Dictionary<string, string> { { "Cookie", rotated } }).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.NoContent, logout.Status, "Logout status");
                    Check.True((logout.SetCookie ?? String.Empty).Contains("Max-Age=0", StringComparison.Ordinal), "Cookie cleared");
                    ApiResult after = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/auth/refresh", null, null, ct, "web", new Dictionary<string, string> { { "Cookie", rotated } }).ConfigureAwait(false);
                    Check.Equal("InvalidRefreshToken", after.Error, "Signed-out cookie rejected");
                }),
                SuiteCases.Http(Id, "Refresh.RotationAndReuse", "Refresh rotates the token; reusing an old token after the race grace window revokes the whole session",
                    (Action<TrellisKit.Core.Settings.AppSettings>)(s => s.Auth.RefreshReuseGraceSeconds = 0), async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    string first = member.RefreshToken!;
                    JsonElement refreshed = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/auth/refresh", null, new { refreshToken = first }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    string second = Api.Str(refreshed, "refreshToken");
                    string access = Api.Str(refreshed, "accessToken");
                    Check.True(second != first, "New refresh token");
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/auth/me", access, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/auth/refresh", null, new { refreshToken = first }, HttpStatusCode.Unauthorized, "InvalidRefreshToken", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/auth/refresh", null, new { refreshToken = second }, HttpStatusCode.Unauthorized, "InvalidRefreshToken", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/auth/me", access, null, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/auth/refresh", null, new { refreshToken = "garbage" }, HttpStatusCode.Unauthorized, "InvalidRefreshToken", ct).ConfigureAwait(false);
                }),
                Case("Regression.API01.ConcurrentRefresh", "Concurrent refreshes with one token: exactly one succeeds and its new tokens stay valid (mobile body and web cookie)", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    List<Task<ApiResult>> attempts = Enumerable.Range(0, 12)
                        .Select(_ => Api.SendAsync(f, HttpMethod.Post, "/api/v1/auth/refresh", null, new { refreshToken = member.RefreshToken }, ct)).ToList();
                    ApiResult[] results = await Task.WhenAll(attempts).ConfigureAwait(false);
                    List<ApiResult> winners = results.Where(r => r.Status == HttpStatusCode.OK).ToList();
                    Check.Equal(1, winners.Count, "Exactly one refresh succeeded");
                    Check.True(results.Where(r => r.Status != HttpStatusCode.OK).All(r => r.Error == "InvalidRefreshToken"), "The rest are InvalidRefreshToken");
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/auth/me", Api.Str(winners[0].Body, "accessToken"), null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/auth/refresh", null, new { refreshToken = Api.Str(winners[0].Body, "refreshToken") }, HttpStatusCode.OK, ct).ConfigureAwait(false);

                    TestMember web = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    ApiResult login = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/auth/login", null, new { email = web.Email, password = web.Password }, ct, "web").ConfigureAwait(false);
                    string cookie = (login.SetCookie ?? String.Empty).Split(';')[0];
                    List<Task<ApiResult>> webAttempts = Enumerable.Range(0, 8)
                        .Select(_ => Api.SendAsync(f, HttpMethod.Post, "/api/v1/auth/refresh", null, null, ct, "web", new Dictionary<string, string> { { "Cookie", cookie } })).ToList();
                    ApiResult[] webResults = await Task.WhenAll(webAttempts).ConfigureAwait(false);
                    List<ApiResult> webWinners = webResults.Where(r => r.Status == HttpStatusCode.OK).ToList();
                    Check.Equal(1, webWinners.Count, "Exactly one cookie refresh succeeded");
                    string rotated = (webWinners[0].SetCookie ?? String.Empty).Split(';')[0];
                    ApiResult again = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/auth/refresh", null, null, ct, "web", new Dictionary<string, string> { { "Cookie", rotated } }).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, again.Status, "Winner's rotated cookie still refreshes (body: " + again.Text + ")");
                }),
                Case("Logout.Mobile", "Logout revokes the session; its refresh and access tokens stop working", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/auth/logout", null, new { refreshToken = member.RefreshToken }, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/auth/refresh", null, new { refreshToken = member.RefreshToken }, HttpStatusCode.Unauthorized, "InvalidRefreshToken", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/auth/me", member.AccessToken, null, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/auth/logout", null, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                }),
                Case("Email.VerifyAndResend", "The emailed token verifies once; resend is rate limited", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/auth/email/resend", member.AccessToken, null, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
                    string token = f.Fakes.Email.LatestToken(member.Email, EmailService.VerifyEmailCategory);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/auth/email/verify", null, new { token }, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                    JsonElement me = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/auth/me", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(me.GetProperty("emailVerified").GetBoolean(), "Verified");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/auth/email/verify", null, new { token }, HttpStatusCode.BadRequest, "InvalidToken", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/auth/email/verify", null, new { token = "nope" }, HttpStatusCode.BadRequest, "InvalidToken", ct).ConfigureAwait(false);

                    TestMember other = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    for (int i = 0; i < f.Settings.Auth.VerificationResendsPerHour; i++)
                        await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/auth/email/resend", other.AccessToken, null, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/auth/email/resend", other.AccessToken, null, (HttpStatusCode)429, "RateLimited", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/auth/email/resend", null, null, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);
                }),
                Case("Password.ForgotAndReset", "Forgot is 202 for any email; reset validates, revokes every session, and is single use", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/auth/password/forgot", null, new { email = Api.UniqueEmail() }, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/auth/password/forgot", null, new { email = member.Email }, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
                    string token = f.Fakes.Email.LatestToken(member.Email, EmailService.PasswordResetCategory);
                    Check.True(f.Fakes.Email.To(member.Email, EmailService.PasswordResetCategory).Last().TextBody.Contains("/reset-password?token=", StringComparison.Ordinal), "Reset link");
                    ApiResult weak = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/auth/password/reset", null, new { token, newPassword = "short" }, ct).ConfigureAwait(false);
                    Check.Equal("ValidationFailed", weak.Error, "Weak password");
                    Check.True(weak.Body.GetProperty("fieldErrors").TryGetProperty("newPassword", out JsonElement _), "fieldErrors.newPassword");
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/auth/password/reset", null, new { token, newPassword = "a brand new passphrase" }, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/auth/refresh", null, new { refreshToken = member.RefreshToken }, HttpStatusCode.Unauthorized, "InvalidRefreshToken", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/auth/password/reset", null, new { token, newPassword = "another passphrase" }, HttpStatusCode.BadRequest, "InvalidToken", ct).ConfigureAwait(false);
                    member.Password = "a brand new passphrase";
                    await Api.LoginAsync(f, member, ct).ConfigureAwait(false);
                }),
                Case("Password.ForgotRateLimited", "Repeated reset requests for one email return 429 RateLimited", async (f, ct) =>
                {
                    string email = Api.UniqueEmail();
                    for (int i = 0; i < f.Settings.Auth.PasswordResetRequestsPerHour; i++)
                        await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/auth/password/forgot", null, new { email }, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/auth/password/forgot", null, new { email }, (HttpStatusCode)429, "RateLimited", ct).ConfigureAwait(false);
                }),
                Case("BootstrapAdmin", "The bootstrap email becomes SystemAdmin once verified", async (f, ct) =>
                {
                    TestMember boss = await Api.RegisterAsync(f, ct, HttpFixture.BootstrapAdminEmail).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/members", boss.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    string token = f.Fakes.Email.LatestToken(boss.Email, EmailService.VerifyEmailCategory);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/auth/email/verify", null, new { token }, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/members", boss.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement me = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/auth/me", boss.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("SystemAdmin", me.GetProperty("roles")[0].GetProperty("role").GetString(), "Role listed");
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/users/" + boss.Id, boss.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                }),
                SuiteCases.Http(Id, "BootstrapAdmin.CreatedWhenNoMembers", "With a bootstrap email and password, startup creates a verified SystemAdmin when the property has no members, once",
                    s => { s.Auth.BootstrapAdminEmail = "Admin@LocalHost"; s.Auth.BootstrapAdminPassword = "password"; },
                    async (f, ct) =>
                    {
                        JsonElement session = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/auth/login", null, new { email = "admin@localhost", password = "password" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        string access = Api.Str(session, "accessToken");
                        JsonElement me = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/auth/me", access, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.True(me.GetProperty("emailVerified").GetBoolean(), "Email verified");
                        Check.Equal("SystemAdmin", me.GetProperty("roles")[0].GetProperty("role").GetString(), "SystemAdmin");
                        await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/members", access, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.True(await f.Server.Services.Accounts.CreateBootstrapAdminIfNoMembersAsync(ct).ConfigureAwait(false) == null, "Not created again once members exist");
                        await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/auth/login", null, new { email = "admin@localhost", password = "wrong password" }, HttpStatusCode.Unauthorized, "InvalidCredentials", ct).ConfigureAwait(false);
                    }),
                SuiteCases.Http(Id, "BootstrapAdmin.NotCreatedWithoutPassword", "Without a bootstrap password, startup creates no account",
                    s => { s.Auth.BootstrapAdminPassword = String.Empty; },
                    async (f, ct) =>
                    {
                        Check.True(await f.Server.Services.Accounts.CreateBootstrapAdminIfNoMembersAsync(ct).ConfigureAwait(false) == null, "Nothing created");
                        await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/auth/login", null, new { email = HttpFixture.BootstrapAdminEmail, password = "password" }, HttpStatusCode.Unauthorized, "InvalidCredentials", ct).ConfigureAwait(false);
                    }),
                Case("Social.NewAccountAndAttestation", "Social sign-up starts unattested; member routes need the attestation; repeat sign-in finds the account", async (f, ct) =>
                {
                    string tokenText = f.Fakes.Google.Script("google-token-1", "google-sub-1", "gus@example.com", true, "Gus Google");
                    JsonElement first = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/auth/google", null, new { idToken = tokenText }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(first.GetProperty("isNewAccount").GetBoolean(), "New account");
                    JsonElement me = first.GetProperty("user");
                    Check.True(!me.GetProperty("ageAttested").GetBoolean(), "Not attested");
                    Check.True(me.GetProperty("emailVerified").GetBoolean(), "Provider-verified email");
                    Check.Equal("Gus Google", Api.Str(me, "displayName"), "Name from the provider");
                    Check.Equal("Google", me.GetProperty("signInMethods")[0].GetString(), "Sign-in method");
                    string access = Api.Str(first, "accessToken");
                    string id = Api.Str(me, "id");
                    await Api.GrantCompAsync(f, id, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/users/" + id, access, null, HttpStatusCode.Forbidden, "AgeAttestationRequired", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/signup-codes/validate", access, new { code = "ANYCODE" }, HttpStatusCode.Forbidden, "AgeAttestationRequired", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/auth/me", access, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    ApiResult bad = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/me/age-attestation", access, new { dateOfBirth = "nope" }, ct).ConfigureAwait(false);
                    Check.True(bad.Body.GetProperty("fieldErrors").TryGetProperty("dateOfBirth", out JsonElement _), "fieldErrors.dateOfBirth");
                    JsonElement attested = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/age-attestation", access, new { dateOfBirth = "1985-03-03", acceptTerms = true, acceptedDocuments = Api.SignupDocuments }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(attested.GetProperty("ageAttested").GetBoolean(), "Attested");
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/users/" + id, access, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement second = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/auth/google", null, new { idToken = tokenText }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!second.GetProperty("isNewAccount").GetBoolean(), "Existing account");
                    Check.Equal(id, Api.Str(second.GetProperty("user"), "id"), "Same member");
                }),
                Case("Social.UnderageClosesAccount", "Attesting under the property minimum age closes the account and signs it out", async (f, ct) =>
                {
                    TestMember member = await Api.SocialAsync(f, ct, "apple").ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/me/age-attestation", member.AccessToken, new { dateOfBirth = DateTime.UtcNow.AddYears(1 - f.Property.Membership.MinimumAge).ToString("yyyy-MM-dd") },
                        HttpStatusCode.Forbidden, "AgeRequirementNotMet", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/auth/me", member.AccessToken, null, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);
                    TrellisKit.Core.Models.User? user = await f.Database.Users.ReadAsync(member.Id, ct).ConfigureAwait(false);
                    Check.True(user!.IsDeleted && user.Email == null, "Anonymized");
                    Check.Equal("n1", f.Fakes.Apple.LastNonce, "Apple receives the raw nonce");
                }),
                Case("Social.AccountLinkRequired", "A provider email that belongs to another account returns 409 AccountLinkRequired with existingMethods", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    string tokenText = f.Fakes.Google.Script("link-token", "link-sub", member.Email.ToUpperInvariant());
                    ApiResult result = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/auth/google", null, new { idToken = tokenText }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Conflict, result.Status, "Status");
                    Check.Equal("AccountLinkRequired", result.Error, "Error");
                    Check.Equal("Email", result.Body.GetProperty("existingMethods")[0].GetString(), "existingMethods");
                    JsonElement linked = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/identities/google", member.AccessToken, new { idToken = tokenText }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(2, linked.GetProperty("signInMethods").GetArrayLength(), "Linked");
                    JsonElement signIn = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/auth/google", null, new { idToken = tokenText }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(member.Id, Api.Str(signIn.GetProperty("user"), "id"), "Google now signs in to the same account");
                }),
                Case("Social.InvalidTokenAndNonce", "Unverifiable tokens return 401 InvalidIdentityToken; Facebook passes the nonce and token type", async (f, ct) =>
                {
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/auth/google", null, new { idToken = "forged" }, HttpStatusCode.Unauthorized, "InvalidIdentityToken", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/auth/apple", null, new { identityToken = "forged", nonce = "x" }, HttpStatusCode.Unauthorized, "InvalidIdentityToken", ct).ConfigureAwait(false);
                    string tokenText = f.Fakes.Facebook.Script("fb-jwt", "fb-sub", null, false, "Fran Facebook");
                    JsonElement body = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/auth/facebook", null, new { token = tokenText, tokenType = "Jwt", nonce = "raw-nonce" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("raw-nonce", f.Fakes.Facebook.LastNonce, "Nonce passed to the verifier");
                    Check.Equal(ExternalTokenTypeEnum.Jwt, f.Fakes.Facebook.LastTokenType, "Token type passed");
                    Check.True(body.GetProperty("user").GetProperty("email").ValueKind == JsonValueKind.Null, "No email from Facebook");
                }),
                Case("Providers.AvailabilityAndNotConfigured", "Providers are anonymous; unconfigured providers report disabled and their routes return 503", async (f, ct) =>
                {
                    f.Fakes.Apple.Configured = false;
                    f.Settings.OAuth.GoogleWebClientId = "web-client.apps.googleusercontent.com";
                    JsonElement providers = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/auth/providers", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(providers.GetProperty("email").GetBoolean(), "Email enabled");
                    Check.True(!providers.GetProperty("apple").GetProperty("enabled").GetBoolean(), "Apple disabled");
                    Check.True(providers.GetProperty("google").GetProperty("enabled").GetBoolean(), "Google enabled");
                    Check.Equal("web-client.apps.googleusercontent.com", providers.GetProperty("google").GetProperty("webClientId").GetString(), "Web client id");
                    Check.True(providers.GetProperty("google").GetProperty("iosClientId").ValueKind == JsonValueKind.Null, "Unset ids are null");
                    Check.Equal(f.Property.Membership.StoreProductId, providers.GetProperty("appleIap").GetProperty("productId").GetString(), "Product id");
                    ApiResult apple = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/auth/apple", null, new { identityToken = "x", nonce = "y" }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.ServiceUnavailable, apple.Status, "503");
                    Check.Equal("IntegrationNotConfigured", apple.Error, "Error");
                    Check.True(Api.Str(apple.Body, "message").Contains("oauth.appleBundleId", StringComparison.Ordinal), "Message names the setting");
                }),
                Case("Groups.Authorization", "Anonymous, signed-in, member, and admin route groups enforce 401, 402, and 403", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/users/" + member.Id, null, null, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/users/" + member.Id, member.AccessToken, null, HttpStatusCode.PaymentRequired, "MembershipRequired", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/members", member.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/settings", member.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/request-history", member.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/membership", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/members", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/auth/me", HttpFixture.AdminToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    await Api.GrantCompAsync(f, member.Id, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/users/" + member.Id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/users/usr_missing", member.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/users/x", "not-a-token", null, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);
                }),
                Case("Cors.ClientHeaders", "Preflight allows X-Client and X-Client-Version with credentials", async (f, ct) =>
                {
                    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/refresh"))
                    {
                        request.Headers.Add("Origin", "http://127.0.0.1:8080");
                        using (HttpResponseMessage response = await f.Client.SendAsync(request, ct).ConfigureAwait(false))
                        {
                            string allowed = response.Headers.TryGetValues("Access-Control-Allow-Headers", out IEnumerable<string>? h) ? String.Join(",", h) : String.Empty;
                            Check.True(allowed.Contains("X-Client", StringComparison.Ordinal) && allowed.Contains("X-Client-Version", StringComparison.Ordinal), "Client headers allowed");
                            Check.Equal("true", response.Headers.TryGetValues("Access-Control-Allow-Credentials", out IEnumerable<string>? c) ? c.First() : null, "Credentials");
                        }
                    }
                })
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, body);
        }

        #endregion
    }
}
