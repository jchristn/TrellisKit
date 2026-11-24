namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Property resolution in both deployment modes (docs/MULTIPROPERTY_CONTRACT.md sections 1, 2, and 8): headers,
    /// hosts, apps, hint agreement, disabled properties, platform routes, special routes, and the single-property
    /// startup checks and folder seeding.
    /// </summary>
    public static class PropertyResolutionSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "PropertyResolution";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Property resolution (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Http(Id, "Single.HeadersValidated", "SingleProperty: no header resolves the one property; its id and app ids work; another id is 404 PropertyNotFound; an unknown app is 403 AppNotRegistered; platform routes are 404 NotAvailable", async (f, ct) =>
                {
                    JsonElement publicProperty = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement navigation = publicProperty.GetProperty("mobile").GetProperty("navigation");
                    Check.Equal("members", navigation.GetProperty("drawerItems")[2].GetString(), "Public property exposes the navigation declaration");
                    Check.Equal(4, navigation.GetProperty("shortcuts").GetArrayLength(), "Public property keeps all configured shortcuts");
                    await SendAsync(f, "/api/v1/property", TestProperty.SampleId, "test.example.trelliskit", HttpStatusCode.OK, null).ConfigureAwait(false);
                    await SendAsync(f, "/api/v1/property", "app_mwczw7zz_Unknown0Property00", null, HttpStatusCode.NotFound, "PropertyNotFound").ConfigureAwait(false);
                    await SendAsync(f, "/api/v1/property", null, "com.unknown.app", HttpStatusCode.Forbidden, "AppNotRegistered").ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/platform/properties", HttpFixture.AdminToken, null, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/health", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    PropertyRecord? record = await f.Raw.Registry.ReadAsync(TestProperty.SampleId, ct).ConfigureAwait(false);
                    Check.True(record != null && record.Status == PropertyStatusEnum.Enabled && record.Slug == "example", "The folder property is registered on first start");
                    Check.True((await f.Raw.Registry.ReadDomainsAsync(TestProperty.SampleId, ct).ConfigureAwait(false)).Count >= 2, "Domains registered from property.json");
                    Check.Equal(2, (await f.Raw.Registry.ReadAppsAsync(TestProperty.SampleId, ct).ConfigureAwait(false)).Count, "Apps registered from property.json");
                    ApiResult byHost = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/property", null, null, ct, "web", new Dictionary<string, string> { { "X-Forwarded-Host", "api.example.test" } }).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, byHost.Status, "Host of the property resolves");
                }),

                SuiteCases.Database(Id, "Single.StartupChecks", "SingleProperty refuses to start when the database holds another property or more than one", async (db, ct) =>
                {
                    DateTime now = DateTime.UtcNow;
                    string other = TrellisKit.Core.Helpers.PropertyIds.Generate();
                    await db.Deployment.Registry.CreateAsync(new PropertyRecord { Id = other, Slug = "other-test", Name = "Other", Status = PropertyStatusEnum.Enabled, DocumentJson = "{}", CreatedUtc = now, UpdatedUtc = now },
                        new List<PropertyDomain>(), new List<PropertyApp>(), ct).ConfigureAwait(false);
                    AppSettings settings = new AppSettings();
                    PropertyDirectory directory = new PropertyDirectory(settings, db.Deployment, p => new Fakes.FakeIntegrations(settings.Auth, settings.Storage, settings.Media).Set, new AppLogger(TestLogging.CreateModule()));
                    await using (directory.ConfigureAwait(false))
                    {
                        directory.Prepare(TestProperty.LoadSample());
                        InvalidOperationException e = await Check.ThrowsAsync<InvalidOperationException>(() => directory.InitializeAsync(ct), "Different property").ConfigureAwait(false);
                        Check.True(e.Message.Contains("property.directory holds property " + TestProperty.SampleId, StringComparison.Ordinal), "Names both properties: " + e.Message);
                    }
                    await db.Deployment.Registry.CreateAsync(new PropertyRecord { Id = TestProperty.SampleId, Slug = "example", Name = "Example", Status = PropertyStatusEnum.Enabled, DocumentJson = "{}", CreatedUtc = now, UpdatedUtc = now },
                        new List<PropertyDomain>(), new List<PropertyApp>(), ct).ConfigureAwait(false);
                    PropertyDirectory second = new PropertyDirectory(settings, db.Deployment, p => new Fakes.FakeIntegrations(settings.Auth, settings.Storage, settings.Media).Set, new AppLogger(TestLogging.CreateModule()));
                    await using (second.ConfigureAwait(false))
                    {
                        second.Prepare(TestProperty.LoadSample());
                        InvalidOperationException e = await Check.ThrowsAsync<InvalidOperationException>(() => second.InitializeAsync(ct), "Two properties").ConfigureAwait(false);
                        Check.True(e.Message.Contains("set deployment.mode to MultiProperty", StringComparison.Ordinal), "Names the fix: " + e.Message);
                    }
                }),

                SuiteCases.Http(Id, "Multi.ResolutionOrder", "MultiProperty: header, host, app, mismatch, disabled, unknown, mobile without X-TK-App, and platform routes only on the platform host", MultiPropertyKit.Multi, async (f, ct) =>
                {
                    string platform = await MultiPropertyKit.PlatformLoginAsync(f, ct).ConfigureAwait(false);
                    string b = await MultiPropertyKit.CreateSecondAsync(f, platform, ct).ConfigureAwait(false);
                    await SendAsync(f, "/api/v1/property", null, null, HttpStatusCode.NotFound, "PropertyNotFound").ConfigureAwait(false);
                    JsonElement a = (await SendAsync(f, "/api/v1/property", TestProperty.SampleId, null, HttpStatusCode.OK, null).ConfigureAwait(false)).Body;
                    JsonElement second = (await SendAsync(f, "/api/v1/property", b, MultiPropertyKit.SecondIosApp, HttpStatusCode.OK, null).ConfigureAwait(false)).Body;
                    Check.Equal("Example Community", Api.Str(a, "name"), "A by header");
                    Check.Equal("Second Example", Api.Str(second, "name"), "B by header and app");
                    ApiResult byHost = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/property", null, null, ct, "web", new Dictionary<string, string> { { "X-Forwarded-Host", MultiPropertyKit.SecondWeb } }).ConfigureAwait(false);
                    Check.Equal("Second Example", Api.Str(byHost.Body, "name"), "B by host");
                    ApiResult mismatch = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/property", null, null, ct, "web", new Dictionary<string, string> { { "X-Forwarded-Host", MultiPropertyKit.SecondWeb }, { "X-TK-Property", TestProperty.SampleId } }).ConfigureAwait(false);
                    Check.Equal("PropertyMismatch", mismatch.Error, "Header and host disagree");
                    await SendAsync(f, "/api/v1/property", TestProperty.SampleId, MultiPropertyKit.SecondIosApp, HttpStatusCode.Forbidden, "AppNotRegistered").ConfigureAwait(false);
                    ApiResult mobile = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/property", null, null, ct, "mobile", new Dictionary<string, string> { { "X-TK-Property", b } }).ConfigureAwait(false);
                    Check.Equal("AppNotRegistered", mobile.Error, "Mobile requests name their app");
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/platform/properties", platform, null, HttpStatusCode.NotFound, "PropertyNotFound", ct).ConfigureAwait(false);
                    await MultiPropertyKit.PlatformExpectAsync(f, HttpMethod.Post, "/api/v1/platform/properties/" + b + "/disable", platform, new { reason = "test" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await SendAsync(f, "/api/v1/property", b, null, HttpStatusCode.NotFound, "PropertyNotFound").ConfigureAwait(false);
                    await SendAsync(f, "/api/v1/property", "app_mwczw7zz_Unknown0Property00", null, HttpStatusCode.NotFound, "PropertyNotFound").ConfigureAwait(false);
                    ApiResult legacyStripe = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/webhooks/stripe", null, "{}", ct).ConfigureAwait(false);
                    Check.Equal("NotAvailable", legacyStripe.Error, "Legacy Stripe URL only in SingleProperty");
                    ApiResult apple = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/webhooks/apple", null, "{\"signedPayload\":\"x.e30.y\"}", ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, apple.Status, "Unmatched store notification is ignored");
                    await Api.ExpectAsync(f, HttpMethod.Get, "/openapi.json", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                }),

                SuiteCases.Http(Id, "Multi.CorsForPropertyDomains", "MultiProperty: browser origins of every enabled property's web domains are allowed, others are not", MultiPropertyKit.Multi, async (f, ct) =>
                {
                    string platform = await MultiPropertyKit.PlatformLoginAsync(f, ct).ConfigureAwait(false);
                    await MultiPropertyKit.CreateSecondAsync(f, platform, ct).ConfigureAwait(false);
                    ApiResult allowed = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/health", null, null, ct, "web", new Dictionary<string, string> { { "Origin", "https://" + MultiPropertyKit.SecondWeb } }).ConfigureAwait(false);
                    Check.Equal("https://" + MultiPropertyKit.SecondWeb, allowed.Headers.TryGetValue("Access-Control-Allow-Origin", out string? origin) ? origin : null, "Property origin allowed");
                    ApiResult denied = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/health", null, null, ct, "web", new Dictionary<string, string> { { "Origin", "https://evil.test" } }).ConfigureAwait(false);
                    Check.True(!denied.Headers.ContainsKey("Access-Control-Allow-Origin"), "Other origin not allowed");
                }),

                SuiteCases.Unit(Id, "Hosts.Normalized", "Hosts are lowercased with the port, trailing dot, and extra forwarded values removed", ct =>
                {
                    Check.Equal("api.example.test", PropertyDirectory.NormalizeHost("API.Example.Test.:8443"), "Normalized");
                    Check.Equal("a.test", PropertyDirectory.NormalizeHost("a.test, proxy.internal"), "First forwarded host");
                    Check.Equal(String.Empty, PropertyDirectory.NormalizeHost(null), "Empty");
                    PropertyConfiguration sample = TestProperty.LoadSample();
                    Check.True(PropertyDirectory.DomainsOf(sample, DateTime.UtcNow).Exists(d => d.Kind == DomainKindEnum.Web && d.Host == "example.test"), "Web domain from property.json");
                    Check.Equal(2, PropertyDirectory.AppsOf(sample, DateTime.UtcNow).Count, "Apps from property.json");
                    return Task.CompletedTask;
                })
            });

            async Task<ApiResult> SendAsync(HttpFixture f, string path, string? property, string? app, HttpStatusCode expected, string? error)
            {
                Dictionary<string, string> headers = new Dictionary<string, string>();
                if (property != null) headers["X-TK-Property"] = property;
                if (app != null) headers["X-TK-App"] = app;
                ApiResult result = await Api.SendAsync(f, HttpMethod.Get, path, null, null, System.Threading.CancellationToken.None, "web", headers).ConfigureAwait(false);
                Check.Equal(expected, result.Status, "GET " + path + " as " + (property ?? "(none)") + "/" + (app ?? "(none)") + " (" + result.Text + ")");
                if (error != null) Check.Equal(error, result.Error, "Error code");
                return result;
            }
        }

        #endregion
    }
}
