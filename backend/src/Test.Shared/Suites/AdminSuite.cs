namespace Test.Shared.Suites
{
    using System;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Phase 1 admin routes over HTTP: member list and detail, flags, roles, comps, bans, mutes, sessions, audit,
    /// settings audit, and request history redaction of credentials.
    /// </summary>
    public static class AdminSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Admin";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Admin (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Members.ListFilterSort", "The member list searches, filters by membership, rank, role, and ban, and sorts", async (f, ct) =>
                {
                    TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
                    TestMember paid = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember free = await Api.RegisterAsync(f, ct, null, "Zed Free").ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", free.AccessToken, new { handle = "zedfree", rank = "Advanced" }, HttpStatusCode.OK, ct).ConfigureAwait(false);

                    JsonElement all = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/members", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(3L, all.GetProperty("totalRecords").GetInt64(), "Total");
                    JsonElement first = all.GetProperty("objects")[0];
                    foreach (string field in new[] { "id", "displayName", "handle", "email", "avatarUrl", "rank", "rankLevel", "membership", "roles", "isBanned", "isMuted", "createdUtc", "lastSeenUtc", "labels", "tags" })
                        Check.True(first.TryGetProperty(field, out JsonElement _), "Item has " + field);
                    Check.Equal(free.Id, Api.Str(first, "id"), "Newest first by default");

                    async Task<long> Count(string query) => (await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/members?" + query, admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).GetProperty("totalRecords").GetInt64();
                    Check.Equal(1L, await Count("search=zedf").ConfigureAwait(false), "Search by handle");
                    Check.Equal(1L, await Count("search=" + Uri.EscapeDataString(paid.Email.Substring(0, 12))).ConfigureAwait(false), "Search by email");
                    Check.Equal(1L, await Count("membership=Active").ConfigureAwait(false), "Active");
                    Check.Equal(1L, await Count("membership=Free").ConfigureAwait(false), "Free (comp)");
                    Check.Equal(0L, await Count("membership=Paid").ConfigureAwait(false), "Paid");
                    Check.Equal(2L, await Count("membership=Inactive").ConfigureAwait(false), "Inactive");
                    Check.Equal(1L, await Count("rank=Advanced").ConfigureAwait(false), "Rank");
                    Check.Equal(1L, await Count("role=SystemAdmin").ConfigureAwait(false), "Role");
                    Check.Equal(0L, await Count("banned=true").ConfigureAwait(false), "Banned");
                    JsonElement byName = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/members?ordering=DisplayNameDescending", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Zed Free", Api.Str(byName.GetProperty("objects")[0], "displayName"), "Sort by name descending");
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/members?ordering=LastSeenDescending", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/members?ordering=EmailAscending", admin.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/members?membership=Gold&maxResults=500", admin.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                }),
                Case("Regression.Members.LastSeenNullsLast", "LastSeenDescending orders seen members newest first and never-seen members last, across pages", async (f, ct) =>
                {
                    TestMember older = await Api.RegisterAsync(f, ct, null, "Seen Earlier").ConfigureAwait(false);
                    TestMember recent = await Api.RegisterAsync(f, ct, null, "Seen Recently").ConfigureAwait(false);
                    TestMember never = await Api.RegisterAsync(f, ct, null, "Never Seen").ConfigureAwait(false);
                    await f.Raw.ExecuteQueryAsync("UPDATE users SET lastseenutc = NULL WHERE id = '" + never.Id + "';", false, ct).ConfigureAwait(false);
                    await f.Raw.ExecuteQueryAsync("UPDATE users SET lastseenutc = " + NotificationTestKit.Ts(DateTime.UtcNow.AddHours(-2)) + " WHERE id = '" + older.Id + "';", false, ct).ConfigureAwait(false);
                    await f.Raw.ExecuteQueryAsync("UPDATE users SET lastseenutc = " + NotificationTestKit.Ts(DateTime.UtcNow.AddHours(-1)) + " WHERE id = '" + recent.Id + "';", false, ct).ConfigureAwait(false);
                    System.Collections.Generic.List<string> order = new System.Collections.Generic.List<string>();
                    string? cursor = null;
                    for (int page = 0; page < 5; page++)
                    {
                        JsonElement result = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/members?ordering=LastSeenDescending&maxResults=1" + (cursor == null ? String.Empty : "&continuationToken=" + Uri.EscapeDataString(cursor)), HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        foreach (JsonElement o in result.GetProperty("objects").EnumerateArray()) order.Add(Api.Str(o, "id"));
                        if (result.GetProperty("endOfResults").GetBoolean()) break;
                        cursor = Api.Str(result, "continuationToken");
                    }
                    Check.True(order.SequenceEqual(new[] { recent.Id, older.Id, never.Id }), "Seen newest first, never seen last: " + String.Join(",", order));
                }),
                Case("Members.DetailFlagsRoles", "Detail includes admin fields; flags and roles change; an admin cannot drop their own SystemAdmin", async (f, ct) =>
                {
                    TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/members/" + member.Id, admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    foreach (string field in new[] { "email", "membership", "flags", "isBanned", "bannedReason", "lastSeenUtc", "subscriptions", "redemptions", "comps", "sessions", "membershipHistory", "labels", "tags" })
                        Check.True(detail.TryGetProperty(field, out JsonElement _), "Detail has " + field);
                    JsonElement comp = detail.GetProperty("comps").GetProperty("objects")[0];
                    Check.Equal("Lifetime", Api.Str(comp, "grantType"), "Comp grant type");
                    Check.Equal("Lifetime free membership", Api.Str(comp, "grantSummary"), "Comp grant summary");
                    Check.Equal("Active", Api.Str(comp, "status"), "Comp status");
                    Check.True(detail.GetProperty("sessions").GetProperty("objects").GetArrayLength() >= 1, "Sessions listed");

                    JsonElement flagged = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/members/" + member.Id + "/flags", admin.AccessToken, new { canPostToFeed = true, canUploadVideo = false }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(flagged.GetProperty("flags").GetProperty("canPostToFeed").GetBoolean(), "canPostToFeed");
                    Check.True(!flagged.GetProperty("flags").GetProperty("canUploadVideo").GetBoolean(), "canUploadVideo");
                    Check.True(flagged.GetProperty("flags").GetProperty("canSendDirectMessages").GetBoolean(), "Untouched flag");

                    JsonElement roles = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/members/" + member.Id + "/roles", admin.AccessToken,
                        new { roles = new object[] { new { role = "Moderator", resourceType = "Forum", resourceId = "frm_1" }, new { role = "EventManager" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(2, roles.GetProperty("roles").GetArrayLength(), "Two roles");
                    Check.Equal("Events", Api.Str(roles.GetProperty("roles").EnumerateArray().First(r => Api.Str(r, "role") == "EventManager"), "resourceType"), "Default resource type");
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/members/" + member.Id + "/roles", admin.AccessToken, new { roles = new object[] { new { role = "User" } } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/members/" + admin.Id + "/roles", admin.AccessToken, new { roles = new object[0] }, HttpStatusCode.Conflict, "CannotRemoveOwnAdmin", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/members/" + member.Id + "/roles", admin.AccessToken, new { roles = new object[] { new { role = "SystemAdmin" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/audit", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/members/usr_missing", admin.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                }),
                Case("Members.Delete", "Staff delete anonymizes the account, revokes its sessions, and is audited; self, administrators, and missing confirmation are refused", async (f, ct) =>
                {
                    TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    TestMember other = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    string path = "/api/v1/admin/members/" + member.Id;
                    await Api.ExpectErrorAsync(f, HttpMethod.Delete, path, admin.AccessToken, new { reason = "Spam" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Delete, path, admin.AccessToken, new { confirm = "DELETE", reason = " " }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Delete, path, member.AccessToken, new { confirm = "DELETE", reason = "Spam" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/admin/members/" + admin.Id, admin.AccessToken, new { confirm = "DELETE", reason = "x" }, HttpStatusCode.Conflict, "CannotDeleteSelf", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/members/" + other.Id + "/roles", admin.AccessToken, new { roles = new object[] { new { role = "SystemAdmin" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/admin/members/" + other.Id, admin.AccessToken, new { confirm = "DELETE", reason = "x" }, HttpStatusCode.Conflict, "CannotDeleteAdmin", ct).ConfigureAwait(false);

                    JsonElement deleted = await Api.ExpectAsync(f, HttpMethod.Delete, path, admin.AccessToken, new { confirm = "DELETE", reason = "Spam account" }, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
                    Check.Equal("The account has been deleted.", Api.Str(deleted, "message"), "Message");
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/auth/me", member.AccessToken, null, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/auth/login", null, new { email = member.Email, password = member.Password }, HttpStatusCode.Unauthorized, "InvalidCredentials", ct).ConfigureAwait(false);
                    JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, path, admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(detail.GetProperty("isDeleted").GetBoolean(), "Marked deleted");
                    Check.True(detail.GetProperty("email").ValueKind == JsonValueKind.Null && Api.Str(detail, "displayName") == "Deleted member", "Anonymized");
                    Check.Equal(0, detail.GetProperty("signInMethods").GetArrayLength(), "Sign-in methods removed");
                    await Api.ExpectErrorAsync(f, HttpMethod.Delete, path, admin.AccessToken, new { confirm = "DELETE", reason = "Again" }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                    JsonElement audit = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/audit?targetId=" + member.Id, admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement record = audit.GetProperty("objects").EnumerateArray().First(a => Api.Str(a, "action") == "member.delete");
                    Check.Equal(admin.Id, Api.Str(record, "actorUserId"), "Actor recorded");
                    Check.True(Api.Str(record, "summary").Contains("Spam account", StringComparison.Ordinal), "Reason recorded");
                }),
                Case("Members.CompBanMuteSessions", "Comps end, bans block sign-in and revoke sessions, mutes show in flags, and sessions can be revoked", async (f, ct) =>
                {
                    TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    JsonElement granted = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/members/" + member.Id + "/comp", admin.AccessToken, new { grantType = "DurationMonths", months = 6, reason = "Instructor" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(granted.GetProperty("membership").GetProperty("isActive").GetBoolean(), "Comp active");
                    Check.Equal("Free membership for 6 months", Api.Str(granted.GetProperty("comps").GetProperty("objects")[0], "grantSummary"), "Comp summary");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/members/" + member.Id + "/comp", admin.AccessToken, new { grantType = "DurationMonths" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    string compId = Api.Str(granted.GetProperty("comps").GetProperty("objects")[0], "id");
                    JsonElement ended = await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/members/" + member.Id + "/comp/" + compId, admin.AccessToken, new { reason = "Left" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!ended.GetProperty("membership").GetProperty("isActive").GetBoolean(), "Comp ended");
                    Check.Equal("Revoked", Api.Str(ended.GetProperty("comps").GetProperty("objects")[0], "status"), "Comp revoked");

                    JsonElement muted = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/members/" + member.Id + "/mute", admin.AccessToken, new { untilUtc = DateTime.UtcNow.AddDays(1), reason = "Cool off" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(muted.GetProperty("flags").GetProperty("isMuted").GetBoolean(), "Muted");
                    JsonElement unmuted = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/members/" + member.Id + "/unmute", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!unmuted.GetProperty("flags").GetProperty("isMuted").GetBoolean(), "Unmuted");

                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/members/" + admin.Id + "/ban", admin.AccessToken, new { reason = "x" }, HttpStatusCode.Conflict, "CannotBanSelf", ct).ConfigureAwait(false);
                    JsonElement banned = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/members/" + member.Id + "/ban", admin.AccessToken, new { reason = "Spam" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(banned.GetProperty("isBanned").GetBoolean() && Api.Str(banned, "bannedReason") == "Spam", "Banned");
                    Check.Equal(0, banned.GetProperty("sessions").GetProperty("objects").GetArrayLength(), "Sessions revoked");
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/auth/me", member.AccessToken, null, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/auth/login", null, new { email = member.Email, password = member.Password }, HttpStatusCode.Forbidden, "AccountBanned", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/members/" + member.Id + "/unban", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.LoginAsync(f, member, ct).ConfigureAwait(false);
                    JsonElement revoked = await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/members/" + member.Id + "/sessions", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(0, revoked.GetProperty("sessions").GetProperty("objects").GetArrayLength(), "All sessions revoked");
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/auth/me", member.AccessToken, null, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);

                    JsonElement audit = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/audit?targetId=" + member.Id + "&ordering=CreatedAscending", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    string[] actions = audit.GetProperty("objects").EnumerateArray().Select(a => Api.Str(a, "action")).ToArray();
                    foreach (string action in new[] { "member.comp", "member.mute", "member.ban", "member.sessions.revoke" }) Check.True(actions.Contains(action), "Audited " + action);
                    JsonElement record = audit.GetProperty("objects").EnumerateArray().First(a => Api.Str(a, "action") != "document.accept");
                    Check.Equal(admin.Id, Api.Str(record, "actorUserId"), "Actor");
                    Check.Equal("Admin Person", Api.Str(record, "actorName"), "Actor name");
                    Check.Equal("User", Api.Str(record, "targetType"), "Target type");
                    JsonElement byActor = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/audit?actorUserId=" + admin.Id + "&action=member.ban", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(2L, byActor.GetProperty("totalRecords").GetInt64(), "Ban and unban");
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/audit?ordering=ActionAscending", admin.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                }),
                Case("Settings.AuditedAndTokenKey", "Settings changes are audited; the token key is generated and validated", async (f, ct) =>
                {
                    Check.Equal(32, Convert.FromBase64String(f.Settings.Auth.TokenEncryptionKey).Length, "Generated 256-bit key");
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/settings", HttpFixture.AdminToken, "{\"values\":{\"signupCodes.attemptsPerHour\":20,\"auth.bootstrapAdminEmail\":\"\"}}", HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(20, f.Settings.SignupCodes.AttemptsPerHour, "Live");
                    ApiResult bad = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/settings", HttpFixture.AdminToken, "{\"values\":{\"auth.tokenEncryptionKey\":\"short\",\"web.publicBaseUrl\":\"not a url\"}}", ct).ConfigureAwait(false);
                    Check.True(bad.Body.GetProperty("fieldErrors").TryGetProperty("auth.tokenEncryptionKey", out JsonElement _), "Key validated");
                    Check.True(bad.Body.GetProperty("fieldErrors").TryGetProperty("web.publicBaseUrl", out JsonElement _), "URL validated");
                    JsonElement audit = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/audit?action=settings.update", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1L, audit.GetProperty("totalRecords").GetInt64(), "Audited once");
                    Check.Equal("admin token", Api.Str(audit.GetProperty("objects")[0], "actorName"), "Admin token actor");
                    JsonElement settings = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/settings", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!settings.ToString().Contains(f.Settings.Auth.TokenEncryptionKey, StringComparison.Ordinal), "Key never returned");
                }),
                Case("RequestHistory.RedactsCredentials", "Captured auth requests never store passwords or tokens", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.LoginAsync(f, member, ct).ConfigureAwait(false);
                    string? id = null;
                    for (int attempt = 0; attempt < 40 && id == null; attempt++)
                    {
                        JsonElement page = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/request-history?pathContains=%2Fauth%2Flogin", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        if (page.GetProperty("objects").GetArrayLength() > 0) id = Api.Str(page.GetProperty("objects")[0], "id");
                        else await Task.Delay(50, ct).ConfigureAwait(false);
                    }
                    Check.True(id != null, "Login captured");
                    JsonElement entry = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/request-history/" + id, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    string request = Api.Str(entry, "requestBody");
                    string response = Api.Str(entry, "responseBody");
                    Check.True(!request.Contains(member.Password, StringComparison.Ordinal) && request.Contains("***redacted***", StringComparison.Ordinal), "Password redacted");
                    Check.True(!response.Contains(member.AccessToken, StringComparison.Ordinal) && !response.Contains(member.RefreshToken!, StringComparison.Ordinal), "Tokens redacted");
                    Check.True(request.Contains(member.Email, StringComparison.Ordinal), "Other fields kept");
                }),
                Case("Regression.API08.RequestHistoryRedaction", "Request history never stores live media cookies or URL signatures (sig and friends) in URLs, headers, or bodies", async (f, ct) =>
                {
                    string grantBody = "{\"mode\":\"Local\",\"cookies\":[{\"name\":\"tk_media\",\"value\":\"SECRETCOOKIE\",\"path\":\"/api/v1/media/files\"}]}";
                    string redactedGrant = RequestHistoryCaptureService.RedactBody(grantBody, false)!;
                    Check.True(!redactedGrant.Contains("SECRETCOOKIE", StringComparison.Ordinal) && redactedGrant.Contains("tk_media", StringComparison.Ordinal), "Cookie value redacted, name kept: " + redactedGrant);
                    string signed = RequestHistoryCaptureService.RedactBody("{\"url\":\"/api/v1/me/export/aex_1/download?exp=1794455920&sig=SECRETSIG\",\"list\":[\"https://cdn.example.test/a.jpg?X-Amz-Signature=SECRETAMZ&X-Amz-Date=1\"]}", false)!;
                    Check.True(!signed.Contains("SECRETSIG", StringComparison.Ordinal) && !signed.Contains("SECRETAMZ", StringComparison.Ordinal), "Signatures redacted: " + signed);
                    Check.True(signed.Contains("exp=1794455920", StringComparison.Ordinal) && signed.Contains("X-Amz-Date=1", StringComparison.Ordinal), "Other parameters kept");
                    Check.Equal("/x?exp=1&sig=" + RequestHistoryCaptureService.RedactedValue + "&y=2", RequestHistoryCaptureService.RedactUrl("/x?exp=1&sig=abc&y=2"), "URL redaction");

                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    ApiResult grant = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/media/grant", member.AccessToken, null, ct, "web").ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, grant.Status, "Grant (" + grant.Text + ")");
                    string cookie = Api.Str(grant.Body.GetProperty("cookies")[0], "value");
                    await Api.SendAsync(f, HttpMethod.Get, "/api/v1/media/files/none?exp=1&sig=SECRETQUERY", member.AccessToken, null, ct).ConfigureAwait(false);
                    string? grantText = null;
                    string? fileUrl = null;
                    for (int attempt = 0; attempt < 40 && (grantText == null || fileUrl == null); attempt++)
                    {
                        JsonElement page = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/request-history?pathContains=%2Fmedia%2F", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        foreach (JsonElement row in page.GetProperty("objects").EnumerateArray())
                        {
                            JsonElement entry = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/request-history/" + Api.Str(row, "id"), HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                            if (Api.Str(entry, "path").EndsWith("/media/grant", StringComparison.Ordinal)) grantText = entry.GetRawText();
                            if (Api.Str(entry, "path").Contains("/media/files/", StringComparison.Ordinal)) fileUrl = Api.Str(entry, "url");
                        }
                        if (grantText == null || fileUrl == null) await Task.Delay(50, ct).ConfigureAwait(false);
                    }
                    Check.True(grantText != null && !grantText.Contains(cookie, StringComparison.Ordinal), "The stored grant response has no live cookie value");
                    Check.True(fileUrl != null && !fileUrl.Contains("SECRETQUERY", StringComparison.Ordinal) && fileUrl.Contains("exp=1", StringComparison.Ordinal), "The stored URL has no signature: " + fileUrl);
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
