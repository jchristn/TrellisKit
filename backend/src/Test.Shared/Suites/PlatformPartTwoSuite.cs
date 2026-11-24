namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Multi-property part 2 (MultiProperty): property administrators' own domains with DNS TXT ownership verification
    /// (unverified domains are not routed), the platform request history, property metrics, property label and tag
    /// discovery, per-property settings through the platform, SlugInUse, and invitation delivery.
    /// </summary>
    public static class PlatformPartTwoSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "PlatformPartTwo";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Platform part 2 (MultiProperty, " + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Unit(Id, "Invite.LinkBaseUrl", "Platform email links use https on public hosts and, on a local platform host (*.localhost, localhost, 127.x), http with the port of a local web.publicBaseUrl", ct =>
                {
                    Check.Equal("https://platform.example.test", PlatformService.PlatformBaseUrl("platform.example.test", "http://127.0.0.1:8080"), "Public host");
                    Check.Equal("https://platform.example.test", PlatformService.PlatformBaseUrl("Platform.Example.Test.", null), "Normalized");
                    Check.Equal("http://platform.localhost:18080", PlatformService.PlatformBaseUrl("platform.localhost", "http://127.0.0.1:18080"), "Local host takes the local web port");
                    Check.Equal("http://localhost", PlatformService.PlatformBaseUrl("localhost", "https://www.example.test"), "Local host, public web URL");
                    Check.Equal("http://127.0.0.1", PlatformService.PlatformBaseUrl("127.0.0.1", ""), "Loopback without web URL");
                    return Task.CompletedTask;
                }),
                SuiteCases.Http(Id, "Domains.VerifiedBeforeRouted", "A domain a property admin adds starts unverified with a TXT challenge, is not routed, fails verification without the record, and is routed after it; another property's host is 409 DomainInUse", MultiPropertyKit.Multi, async (f, ct) =>
                {
                    string platform = await MultiPropertyKit.PlatformLoginAsync(f, ct).ConfigureAwait(false);
                    string second = await MultiPropertyKit.CreateSecondAsync(f, platform, ct).ConfigureAwait(false);
                    f.PropertyId = TestProperty.SampleId;
                    JsonElement current = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/property/domains", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    List<object> domains = current.GetProperty("objects").EnumerateArray().Select(d => (object)new { host = Api.Str(d, "host"), kind = Api.Str(d, "kind") }).ToList();
                    Check.True(current.GetProperty("objects").EnumerateArray().All(d => d.GetProperty("verifiedUtc").ValueKind != JsonValueKind.Null && Api.Str(d, "source") == "Platform"), "Seeded domains are verified");
                    string host = "community-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".example.test";
                    domains.Add(new { host, kind = "Web" });
                    JsonElement updated = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/domains", HttpFixture.AdminToken, new { domains }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement added = updated.GetProperty("domains").EnumerateArray().First(d => Api.Str(d, "host") == host);
                    Check.True(added.GetProperty("verifiedUtc").ValueKind == JsonValueKind.Null && Api.Str(added, "source") == "Property", "Starts unverified");
                    JsonElement challenge = added.GetProperty("verification");
                    Check.Equal(PlatformService.VerificationRecordPrefix + host, Api.Str(challenge, "recordName"), "Record name");
                    string value = Api.Str(challenge, "recordValue");
                    Check.True(value.StartsWith(PlatformService.VerificationValuePrefix, StringComparison.Ordinal), "Record value");
                    Check.True(updated.GetProperty("domains").EnumerateArray().Where(d => Api.Str(d, "host") != host).All(d => d.GetProperty("verifiedUtc").ValueKind != JsonValueKind.Null), "Unchanged hosts keep their state");

                    f.PropertyId = null;
                    f.HostHeader = host;
                    ApiResult unrouted = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/property", null, null, ct, "web").ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.NotFound, unrouted.Status, "Unverified domains are not routed");
                    f.HostHeader = null;
                    f.PropertyId = TestProperty.SampleId;
                    string domainId = Api.Str(added, "id");
                    JsonElement failed = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/property/domains/" + domainId + "/verify", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(failed.GetProperty("verifiedUtc").ValueKind == JsonValueKind.Null && Api.Str(failed.GetProperty("verification"), "error").Contains("No TXT record", StringComparison.Ordinal), "No record yet");
                    f.Fakes.Txt.Set(PlatformService.VerificationRecordPrefix + host, "unrelated", value);
                    JsonElement verified = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/property/domains/" + domainId + "/verify", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(verified.GetProperty("verifiedUtc").ValueKind != JsonValueKind.Null, "Verified with the TXT record");
                    f.PropertyId = null;
                    f.HostHeader = host;
                    JsonElement routed = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct, "web").ConfigureAwait(false);
                    Check.Equal(TestProperty.SampleId, Api.Str(routed, "id"), "Routed after verification");
                    f.HostHeader = null;
                    f.PropertyId = TestProperty.SampleId;
                    domains.Add(new { host = MultiPropertyKit.SecondWeb, kind = "Web" });
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/property/domains", HttpFixture.AdminToken, new { domains }, HttpStatusCode.Conflict, "DomainInUse", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/property/domains", HttpFixture.AdminToken, new { apps = new[] { new { platform = "Ios", appId = MultiPropertyKit.SecondIosApp } } }, HttpStatusCode.Conflict, "AppIdInUse", ct).ConfigureAwait(false);
                    JsonElement audit = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/audit?action=property.domain.verify", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(audit.GetProperty("totalRecords").GetInt64() >= 2, "Verification audited");
                    Check.True(second.Length > 0, "Second property");
                }),

                SuiteCases.Http(Id, "Platform.HistoryMetricsDiscovery", "Platform request history per property, property metrics, property label and tag discovery, per-property settings through the platform, SlugInUse, and invitation delivery", MultiPropertyKit.Multi, async (f, ct) =>
                {
                    string platform = await MultiPropertyKit.PlatformLoginAsync(f, ct).ConfigureAwait(false);
                    string second = await MultiPropertyKit.CreateSecondAsync(f, platform, ct).ConfigureAwait(false);
                    f.PropertyId = TestProperty.SampleId;
                    await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    JsonElement history = new JsonElement();
                    for (int attempt = 0; attempt < 40; attempt++)
                    {
                        history = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/request-history/enumerate", platform, new { filters = new { propertyId = TestProperty.SampleId, pathContains = "/auth/register" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        if (history.GetProperty("objects").GetArrayLength() > 0) break;
                        await Task.Delay(50, ct).ConfigureAwait(false);
                    }
                    Check.True(history.GetProperty("objects").GetArrayLength() > 0, "Property request history");
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/request-history/enumerate", platform, new { filters = new { propertyId = "unresolved" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/request-history/enumerate", platform, new { filters = new { propertyId = "app_missing_00000000000000000" } }, HttpStatusCode.NotFound, ct).ConfigureAwait(false);

                    JsonElement metrics = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/metrics/properties/enumerate", platform, new { maxResults = 25 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement sample = metrics.GetProperty("objects").EnumerateArray().First(m => Api.Str(m, "propertyId") == TestProperty.SampleId);
                    Check.True(sample.GetProperty("members").GetInt64() >= 1 && sample.GetProperty("requests24h").GetInt64() >= 1, "Members and requests counted");
                    Check.True(metrics.GetProperty("objects").EnumerateArray().Any(m => Api.Str(m, "propertyId") == second), "Every property listed");

                    JsonElement labels = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/labels/enumerate", platform, new { prefix = "test" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(labels.GetProperty("objects").EnumerateArray().Any(l => Api.Str(l, "label") == "test-fixture"), "Property labels discovered");
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/tags/keys/enumerate", platform, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);

                    JsonElement settings = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Put, "/api/v1/platform/properties/" + second + "/settings", platform, new { values = new Dictionary<string, object> { ["email.fromName"] = "Second Mail" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Second Mail", Api.Str(settings.GetProperty("fields").EnumerateArray().First(x => Api.Str(x, "key") == "email.fromName"), "value"), "Platform writes property settings");
                    f.PropertyId = TestProperty.SampleId;
                    JsonElement own = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/property/settings?search=email.fromName", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(Api.Str(own.GetProperty("objects").EnumerateArray().First(x => Api.Str(x, "key") == "email.fromName"), "value") != "Second Mail", "Settings are per property");

                    JsonObject duplicate = MultiPropertyKit.SecondDocument();
                    duplicate["domains"] = new JsonObject { ["web"] = "dupe-slug.test", ["api"] = "api.dupe-slug.test", ["media"] = "media.dupe-slug.test", ["redirects"] = new JsonArray() };
                    duplicate["mobile"]!["bundleId"] = "test.dupe.slug";
                    duplicate["mobile"]!["androidPackage"] = "test.dupe.slug";
                    ApiResult slug = await MultiPropertyKit.PlatformAsync(f, HttpMethod.Post, "/api/v1/platform/properties", platform, new { document = duplicate }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Conflict, slug.Status, "Slug conflict (" + slug.Text + ")");
                    Check.Equal("SlugInUse", slug.Error, "SlugInUse");
                    Check.True(slug.Body.GetProperty("fieldErrors").TryGetProperty("slug", out JsonElement _), "Names the slug field");

                    JsonElement invite = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/admins", platform, new { email = "third@platform.test", displayName = "Third Admin" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Check.Equal("Log", Api.Str(invite, "inviteDelivery"), "No deployment provider: logged");
                    Check.True(Api.Str(invite, "inviteToken").Length > 0, "Token returned only when not emailed");
                }),

                SuiteCases.Http(Id, "Invite.Emailed", "With a deployment email sender the invitation is emailed through the platform_admin_invite template and the token is not returned", s =>
                {
                    MultiPropertyKit.Multi(s);
                    s.Email.SmtpHost = "smtp.invalid";
                    s.Email.FromAddress = "platform@example.test";
                }, async (f, ct) =>
                {
                    f.Server.Platform.PlatformSmtp = f.Fakes.Email;
                    string platform = await MultiPropertyKit.PlatformLoginAsync(f, ct).ConfigureAwait(false);
                    JsonElement invite = await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/admins", platform, new { email = "mailed@platform.test", displayName = "Mailed Admin" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Check.Equal("Email", Api.Str(invite, "inviteDelivery"), "Emailed");
                    Check.True(invite.GetProperty("inviteToken").ValueKind == JsonValueKind.Null, "Token not returned");
                    TrellisKit.Core.Integrations.Models.EmailMessage message = f.Fakes.Email.To("mailed@platform.test", "platform_admin_invite").Single();
                    Check.True(message.TextBody.Contains("/platform/accept-invite?token=", StringComparison.Ordinal), "Acceptance link");
                    string token = Uri.UnescapeDataString(message.TextBody.Split("token=")[1].Split(new[] { '\n', ' ' })[0]);
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/auth/accept-invite", null, new { token, password = "a long platform password" }, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                })
            });
        }

        #endregion
    }
}
