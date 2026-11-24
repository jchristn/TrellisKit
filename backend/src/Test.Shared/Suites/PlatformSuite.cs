namespace Test.Shared.Suites
{
    using System;
    using System.Data;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Server.Workers;

    /// <summary>
    /// Platform administration (MultiProperty; docs/MULTIPROPERTY_CONTRACT.md section 6 with the resolved decisions):
    /// separate platform administrator accounts and sessions, property lifecycle with the 25-property limit and the
    /// 30-day restore window then purge, members annotated as property administrators, and short audited delegated
    /// sessions bound to one property.
    /// </summary>
    public static class PlatformSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Platform";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Platform administration (MultiProperty, " + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Http(Id, "Admins.SeparateAccountsAndSessions", "The bootstrap administrator signs in on the platform host only; invitations, sign-out, lockout, the last-admin rule, and member tokens never pass platform routes", MultiPropertyKit.Multi, async (f, ct) =>
                {
                    ApiResult wrong = await MultiPropertyKit.PlatformAsync(f, HttpMethod.Post, "/api/v1/platform/auth/login", null, new { email = MultiPropertyKit.PlatformEmail, password = "not the password" }, ct).ConfigureAwait(false);
                    Check.Equal("InvalidCredentials", wrong.Error, "Wrong password");
                    string token = await MultiPropertyKit.PlatformLoginAsync(f, ct).ConfigureAwait(false);
                    Check.True(token.StartsWith("tkp_", StringComparison.Ordinal), "Platform token prefix");
                    JsonElement me = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Get, "/api/v1/platform/auth/me", token, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(MultiPropertyKit.PlatformEmail, Api.Str(me, "email"), "Me");

                    f.PropertyId = TestProperty.SampleId;
                    TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Get, "/api/v1/platform/properties", admin.AccessToken, null, HttpStatusCode.Unauthorized, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/members", token, null, HttpStatusCode.Unauthorized, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/settings", admin.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);

                    JsonElement invite = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/admins", token, new { email = "second@platform.test", displayName = "Second Admin" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    string inviteToken = Api.Str(invite, "inviteToken");
                    string secondId = Api.Str(invite.GetProperty("admin"), "id");
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/admins", token, new { email = "SECOND@platform.test", displayName = "Dupe" }, HttpStatusCode.Conflict, ct).ConfigureAwait(false);
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/auth/accept-invite", null, new { token = inviteToken, password = "second horse battery" }, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/auth/accept-invite", null, new { token = inviteToken, password = "second horse battery" }, HttpStatusCode.BadRequest, ct).ConfigureAwait(false);
                    JsonElement second = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/auth/login", null, new { email = "second@platform.test", password = "second horse battery" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement list = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/admins/enumerate", token, new { search = "second" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, list.GetProperty("objects").GetArrayLength(), "Search admins");
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Delete, "/api/v1/platform/admins/" + secondId, token, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Get, "/api/v1/platform/auth/me", Api.Str(second, "accessToken"), null, HttpStatusCode.Unauthorized, ct).ConfigureAwait(false);
                    ApiResult last = await MultiPropertyKit.PlatformAsync(f, HttpMethod.Delete, "/api/v1/platform/admins/" + Api.Str(me, "id"), HttpFixture.AdminToken, null, ct).ConfigureAwait(false);
                    Check.Equal("LastPlatformAdmin", last.Error, "Last administrator kept");
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/auth/logout", token, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Get, "/api/v1/platform/auth/me", token, null, HttpStatusCode.Unauthorized, ct).ConfigureAwait(false);
                    JsonElement audit = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/audit/enumerate", HttpFixture.AdminToken, new { filters = new { action = "platform.admin.invite" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, audit.GetProperty("objects").GetArrayLength(), "Invitation audited in the platform realm");
                }),

                SuiteCases.Http(Id, "Properties.LifecycleLimitRestorePurge", "Create (opaque id, Disabled), conflicts, enable, list, the deployment limit, delete (disabled at once, restorable), restore, and purge after the window", (TrellisKit.Core.Settings.AppSettings s) => { MultiPropertyKit.Multi(s); s.Deployment.MaxProperties = 2; }, async (f, ct) =>
                {
                    string token = await MultiPropertyKit.PlatformLoginAsync(f, ct).ConfigureAwait(false);
                    string b = await MultiPropertyKit.CreateSecondAsync(f, token, ct).ConfigureAwait(false);
                    JsonElement record = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Get, "/api/v1/platform/properties/" + b, token, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Enabled", Api.Str(record, "status"), "Enabled");
                    Check.Equal("second", Api.Str(record, "slug"), "Slug is cosmetic");
                    Check.True(record.GetProperty("domains").EnumerateArray().Any(d => Api.Str(d, "host") == MultiPropertyKit.SecondWeb), "Domains from the document");
                    Check.Equal("test-fixture", record.GetProperty("labels")[0].GetString(), "Labels");
                    JsonElement page = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/properties/enumerate", token, new { ordering = "NameAscending" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(2, page.GetProperty("objects").GetArrayLength(), "Two properties");
                    JsonElement byHost = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/properties/enumerate", token, new { filters = new { host = MultiPropertyKit.SecondWeb } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, byHost.GetProperty("objects").GetArrayLength(), "Host filter");

                    ApiResult limit = await MultiPropertyKit.PlatformAsync(f, HttpMethod.Post, "/api/v1/platform/properties", token, new { cloneFromPropertyId = b, domains = new[] { new { host = "third.test", kind = "Web" } }, apps = new object[0] }, ct).ConfigureAwait(false);
                    Check.Equal("PropertyLimitReached", limit.Error, "deployment.maxProperties");
                    ApiResult taken = await MultiPropertyKit.PlatformAsync(f, HttpMethod.Put, "/api/v1/platform/properties/" + b, token, new { domains = new[] { new { host = "example.test", kind = "Web" } } }, ct).ConfigureAwait(false);
                    Check.Equal("DomainInUse", taken.Error, "Hosts are unique across the deployment");

                    f.PropertyId = b;
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    ApiResult confirm = await MultiPropertyKit.PlatformAsync(f, HttpMethod.Delete, "/api/v1/platform/properties/" + b, token, new { confirm = "nope" }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, confirm.Status, "Typed confirmation");
                    JsonElement deleting = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Delete, "/api/v1/platform/properties/" + b, token, new { confirm = b }, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
                    Check.Equal("Deleting", Api.Str(deleting, "status"), "Deleting");
                    DateTime scheduled = deleting.GetProperty("deleteScheduledUtc").GetDateTime();
                    Check.True(scheduled > DateTime.UtcNow.AddDays(29) && scheduled < DateTime.UtcNow.AddDays(31), "Restorable for 30 days");
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/auth/me", member.AccessToken, null, HttpStatusCode.NotFound, "PropertyNotFound", ct).ConfigureAwait(false);
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/properties/" + b + "/restore", token, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/properties/" + b + "/enable", token, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/auth/me", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);

                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Delete, "/api/v1/platform/properties/" + b, token, new { confirm = b }, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
                    PropertyPurgeWorker purge = new PropertyPurgeWorker(f.Raw, f.Server.Directory, f.Fakes.Set.ObjectStore, f.Settings.Storage.Directory, f.Services.Logger) { Clock = () => DateTime.UtcNow.AddDays(31) };
                    Check.Equal(1, await purge.RunOnceAsync(ct).ConfigureAwait(false), "Purged after the window");
                    foreach (string table in PropertyTables.Owned)
                    {
                        DataTable rows = await f.Raw.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM " + table + " WHERE propertyid = '" + b + "';", false, ct).ConfigureAwait(false);
                        Check.Equal(0L, RowReader.GetLong(rows.Rows[0], "cnt"), "No " + table + " rows left");
                    }
                    JsonElement gone = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Get, "/api/v1/platform/properties/" + b, token, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Deleted", Api.Str(gone, "status"), "Tombstone kept");
                    ApiResult restore = await MultiPropertyKit.PlatformAsync(f, HttpMethod.Post, "/api/v1/platform/properties/" + b + "/restore", token, null, ct).ConfigureAwait(false);
                    Check.Equal("PropertyNotRestorable", restore.Error, "Not restorable after the purge");
                    JsonElement audit = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/audit/enumerate", token, new { filters = new { targetId = b } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(audit.GetProperty("objects").GetArrayLength() >= 6, "Lifecycle audited");
                }),

                SuiteCases.Http(Id, "Delegation.ShortAuditedBoundToOneProperty", "A delegated session acts as SystemAdmin in its property only, cannot act on me, is audited in both audits; members annotated as property administrators gain and lose admin rights", MultiPropertyKit.Multi, async (f, ct) =>
                {
                    string token = await MultiPropertyKit.PlatformLoginAsync(f, ct).ConfigureAwait(false);
                    string b = await MultiPropertyKit.CreateSecondAsync(f, token, ct).ConfigureAwait(false);
                    JsonElement session = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/properties/" + TestProperty.SampleId + "/admin-session", token, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    string delegated = Api.Str(session, "accessToken");
                    DateTime expires = session.GetProperty("accessTokenExpiresUtc").GetDateTime();
                    Check.True(expires <= DateTime.UtcNow.AddMinutes(61), "Short-lived");
                    f.PropertyId = TestProperty.SampleId;
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/members", delegated, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    ApiResult me = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/me/notifications", delegated, null, ct).ConfigureAwait(false);
                    Check.True(me.Status == HttpStatusCode.Forbidden || me.Status == HttpStatusCode.NotFound, "No member account behind a delegated session (" + (int)me.Status + ")");
                    f.PropertyId = b;
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/members", delegated, null, HttpStatusCode.Unauthorized, ct).ConfigureAwait(false);
                    f.PropertyId = TestProperty.SampleId;
                    JsonElement propertyAudit = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/audit/enumerate", HttpFixture.AdminToken, new { filters = new { action = "platform.delegate" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, propertyAudit.GetProperty("objects").GetArrayLength(), "Delegation audited in the property");
                    JsonElement platformAudit = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/audit/enumerate", token, new { filters = new { action = "platform.delegate" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, platformAudit.GetProperty("objects").GetArrayLength(), "Delegation audited in the platform");

                    f.PropertyId = b;
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/members", member.AccessToken, null, HttpStatusCode.Forbidden, ct).ConfigureAwait(false);
                    ApiResult foreign = await MultiPropertyKit.PlatformAsync(f, HttpMethod.Post, "/api/v1/platform/properties/" + TestProperty.SampleId + "/admins", token, new { userId = member.Id }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.NotFound, foreign.Status, "A member of B cannot become an admin of A");
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/properties/" + b + "/admins", token, new { userId = member.Id }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/members", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement admins = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/properties/" + b + "/admins/enumerate", token, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, admins.GetProperty("objects").GetArrayLength(), "Property administrators listed");
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Delete, "/api/v1/platform/properties/" + b + "/admins/" + member.Id, token, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/members", member.AccessToken, null, HttpStatusCode.Forbidden, ct).ConfigureAwait(false);
                })
            });
        }

        #endregion
    }
}
